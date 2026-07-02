using System;
using System.IO.Ports;
using System.Threading;

namespace ArtnetNode.Drivers
{
    public class EnttecProDmxInterface : IDmxInterface
    {
        private SerialPort? _serialPort;
        private readonly object _lock = new object();
        
        // Transmission Thread State
        private Thread? _txThread;
        private bool _isTxRunning;
        private readonly byte[] _sharedBuffer = new byte[512];

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public string ConnectionStatus => IsConnected 
            ? $"Connesso su {_serialPort?.PortName} (Enttec Pro)" 
            : "Sconnesso";

        public void Connect(string portName)
        {
            lock (_lock)
            {
                Disconnect();

                if (string.IsNullOrEmpty(portName))
                    throw new ArgumentException("Il nome della porta COM non può essere vuoto.");

                // Configure serial port for Enttec Pro (115200 baud is standard/stable for host communication)
                _serialPort = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    WriteTimeout = 500
                };

                _serialPort.Open();
                
                // Clear any buffers
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                // Clear shared buffer
                Array.Clear(_sharedBuffer, 0, _sharedBuffer.Length);

                // Start transmission thread
                _isTxRunning = true;
                _txThread = new Thread(TxLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Normal,
                    Name = "EnttecProTxThread"
                };
                _txThread.Start();
            }
        }

        public void Disconnect()
        {
            lock (_lock)
            {
                _isTxRunning = false;
            }

            if (_txThread != null)
            {
                if (_txThread.IsAlive)
                {
                    _txThread.Join(500); // Wait for thread to exit
                }
                _txThread = null;
            }

            lock (_lock)
            {
                if (_serialPort != null)
                {
                    try
                    {
                        if (_serialPort.IsOpen)
                        {
                            _serialPort.DiscardOutBuffer();
                            _serialPort.Close();
                        }
                    }
                    catch
                    {
                        // Ignore exceptions during close
                    }
                    finally
                    {
                        _serialPort.Dispose();
                        _serialPort = null;
                    }
                }
            }
        }

        public void SendDmx(byte[] dmxData)
        {
            lock (_lock)
            {
                // Update shared buffer (up to 512 channels)
                int copyLength = Math.Min(dmxData.Length, 512);
                Array.Copy(dmxData, 0, _sharedBuffer, 0, copyLength);
            }
        }

        private void TxLoop()
        {
            // Message format: 
            // Byte 0: 0x7E (Start of Message)
            // Byte 1: 6 (Label: Output Only Send DMX Packet Request)
            // Byte 2-3: Data Length LSB, MSB (513 bytes: 1 byte DMX Start Code + 512 DMX channels)
            // Byte 4: 0x00 (DMX Start Code)
            // Byte 5..516: DMX Channel values
            // Byte 517: 0xE7 (End of Message)
            byte[] localBuffer = new byte[518];
            localBuffer[0] = 0x7E;
            localBuffer[1] = 6;
            
            int dataLength = 513;
            localBuffer[2] = (byte)(dataLength & 0xFF);        // LSB
            localBuffer[3] = (byte)((dataLength >> 8) & 0xFF); // MSB
            
            localBuffer[4] = 0x00; // DMX Start Code
            localBuffer[517] = 0xE7; // End of Message

            while (true)
            {
                SerialPort? port = null;
                bool running = false;

                lock (_lock)
                {
                    running = _isTxRunning;
                    port = _serialPort;
                    if (running && port != null && port.IsOpen)
                    {
                        Array.Copy(_sharedBuffer, 0, localBuffer, 5, 512);
                    }
                    else
                    {
                        running = false;
                    }
                }

                if (!running || port == null)
                    break;

                try
                {
                    // Write full DMX frame to Enttec Pro hardware
                    port.Write(localBuffer, 0, localBuffer.Length);

                    // Sleep to allow 115200 baud transmission of 518 bytes (~45ms)
                    // This protects the FTDI chip and Enttec Pro controller buffers from overflowing.
                    Thread.Sleep(45);
                }
                catch (Exception)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}

