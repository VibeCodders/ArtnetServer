using System;
using System.IO.Ports;
using System.Threading;

namespace ArtnetNode.Drivers
{
    public class Dmx4AllUsbInterface : IDmxInterface
    {
        private SerialPort? _serialPort;
        private readonly object _lock = new object();
        
        // Transmission Thread State
        private Thread? _txThread;
        private bool _isTxRunning;
        private readonly byte[] _sharedBuffer = new byte[512];

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public string ConnectionStatus => IsConnected 
            ? $"Connesso su {_serialPort?.PortName} (DMX4ALL)" 
            : "Sconnesso";

        public void Connect(string portName)
        {
            lock (_lock)
            {
                Disconnect();

                if (string.IsNullOrEmpty(portName))
                    throw new ArgumentException("Il nome della porta COM non può essere vuoto.");

                // Configure serial port for DMX4ALL: 38400 Baud, 8 data bits, 1 stop bit, no parity
                _serialPort = new SerialPort(portName, 38400, Parity.None, 8, StopBits.One)
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
                    Name = "Dmx4AllTxThread"
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
            // Packets structure:
            // Header: 0xFF
            // Start Channel LSB, Start Channel MSB
            // Length (max 255)
            // Data bytes...
            
            // To transmit 512 channels, we split into 3 packets:
            // Packet 1: Start 1 (0x01, 0x00), Count 255 (values 0..254) -> size 259 bytes
            // Packet 2: Start 256 (0x00, 0x01), Count 255 (values 255..509) -> size 259 bytes
            // Packet 3: Start 511 (0xFF, 0x01), Count 2 (values 510..511) -> size 6 bytes
            
            byte[] packet1 = new byte[259];
            packet1[0] = 0xFF;
            packet1[1] = 0x01; // Start channel 1 LSB
            packet1[2] = 0x00; // Start channel 1 MSB
            packet1[3] = 255;  // Count

            byte[] packet2 = new byte[259];
            packet2[0] = 0xFF;
            packet2[1] = 0x00; // Start channel 256 LSB (256 & 0xFF = 0x00)
            packet2[2] = 0x01; // Start channel 256 MSB (256 >> 8 = 0x01)
            packet2[3] = 255;  // Count

            byte[] packet3 = new byte[6];
            packet3[0] = 0xFF;
            packet3[1] = 0xFF; // Start channel 511 LSB (511 & 0xFF = 0xFF)
            packet3[2] = 0x01; // Start channel 511 MSB (511 >> 8 = 0x01)
            packet3[3] = 2;    // Count

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
                        Array.Copy(_sharedBuffer, 0, packet1, 4, 255);
                        Array.Copy(_sharedBuffer, 255, packet2, 4, 255);
                        Array.Copy(_sharedBuffer, 510, packet3, 4, 2);
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
                    // Write the 3 blocks sequentially
                    port.Write(packet1, 0, packet1.Length);
                    port.Write(packet2, 0, packet2.Length);
                    port.Write(packet3, 0, packet3.Length);

                    // Sleep to allow transmission of 524 bytes at 38400 baud (~137ms)
                    // We sleep 45ms to balance CPU cycles, and the serial port hardware queue handles buffer flow.
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

