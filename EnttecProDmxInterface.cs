using System;
using System.IO.Ports;

namespace ArtnetNode
{
    public class EnttecProDmxInterface : IDmxInterface
    {
        private SerialPort? _serialPort;
        private readonly object _lock = new object();
        private byte[] _txBuffer = new byte[518]; // 5 bytes header + 1 byte start code + 512 channels + 1 byte end footer

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public string ConnectionStatus => IsConnected 
            ? $"Connesso su {_serialPort?.PortName} (Enttec Pro)" 
            : "Sconnesso";

        public EnttecProDmxInterface()
        {
            // Pre-fill constants in TX Buffer
            _txBuffer[0] = 0x7E; // Start of message
            _txBuffer[1] = 6;    // Label: Output Only Send DMX Packet Request
            
            // Length is 513 (1 start code + 512 DMX channels)
            int dataLength = 513;
            _txBuffer[2] = (byte)(dataLength & 0xFF);        // LSB
            _txBuffer[3] = (byte)((dataLength >> 8) & 0xFF); // MSB
            
            _txBuffer[4] = 0x00; // DMX Start Code
            _txBuffer[517] = 0xE7; // End of message
        }

        public void Connect(string portName)
        {
            lock (_lock)
            {
                Disconnect();

                if (string.IsNullOrEmpty(portName))
                    throw new ArgumentException("Il nome della porta COM non può essere vuoto.");

                // Configure serial port for Enttec Pro (Baud rate doesn't strictly matter for FTDI, but 115200 is default/stable)
                _serialPort = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    WriteTimeout = 500
                };

                _serialPort.Open();
                
                // Clear any buffers
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
            }
        }

        public void Disconnect()
        {
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
            if (!IsConnected || _serialPort == null)
                return;

            lock (_lock)
            {
                try
                {
                    // Copy DMX data into TX buffer (up to 512 bytes)
                    int copyLength = Math.Min(dmxData.Length, 512);
                    Array.Copy(dmxData, 0, _txBuffer, 5, copyLength);
                    
                    // Zero out remaining channels if DMX data is shorter than 512
                    if (copyLength < 512)
                    {
                        Array.Clear(_txBuffer, 5 + copyLength, 512 - copyLength);
                    }

                    _serialPort.Write(_txBuffer, 0, _txBuffer.Length);
                }
                catch (Exception)
                {
                    // If write fails, we could handle it or let the caller know, but typically we silent catch and wait for next packet
                    // or let connection status reflect the error.
                }
            }
        }
    }
}
