using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace ArtnetNode
{
    public class OpenDmxInterface : IDmxInterface
    {
        private SerialPort? _serialPort;
        private readonly object _lock = new object();
        private byte[] _txBuffer = new byte[513]; // 1 start code + 512 channels

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public string ConnectionStatus => IsConnected 
            ? $"Connesso su {_serialPort?.PortName} (Open DMX)" 
            : "Sconnesso";

        public OpenDmxInterface()
        {
            _txBuffer[0] = 0x00; // DMX Start Code
        }

        public void Connect(string portName)
        {
            lock (_lock)
            {
                Disconnect();

                if (string.IsNullOrEmpty(portName))
                    throw new ArgumentException("Il nome della porta COM non può essere vuoto.");

                // Configure serial port for Open DMX: 250000 baud, 8 data bits, 2 stop bits, no parity
                _serialPort = new SerialPort(portName, 250000, Parity.None, 8, StopBits.Two)
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
                    Array.Copy(dmxData, 0, _txBuffer, 1, copyLength);
                    
                    // Zero out remaining channels if DMX data is shorter than 512
                    if (copyLength < 512)
                    {
                        Array.Clear(_txBuffer, 1 + copyLength, 512 - copyLength);
                    }

                    // 1. Generate BREAK (low for at least 88us)
                    _serialPort.BreakState = true;
                    HighResolutionDelay(100); // 100 microseconds delay

                    // 2. Generate MAB (Mark After Break - high for at least 8us)
                    _serialPort.BreakState = false;
                    HighResolutionDelay(12); // 12 microseconds delay

                    // 3. Write data (513 bytes: start code + 512 DMX channels)
                    _serialPort.Write(_txBuffer, 0, _txBuffer.Length);
                }
                catch (Exception)
                {
                    // Silent catch, wait for next frame
                }
            }
        }

        /// <summary>
        /// Busy-waits using Stopwatch for high-precision delay in microseconds.
        /// </summary>
        private void HighResolutionDelay(double microseconds)
        {
            long ticks = (long)(microseconds * Stopwatch.Frequency / 1_000_000);
            long startTicks = Stopwatch.GetTimestamp();
            while (Stopwatch.GetTimestamp() - startTicks < ticks)
            {
                // Spin wait to keep high precision
                Thread.SpinWait(1);
            }
        }
    }
}
