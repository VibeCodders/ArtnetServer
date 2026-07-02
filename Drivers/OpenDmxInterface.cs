using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace ArtnetNode.Drivers
{
    public class OpenDmxInterface : IDmxInterface
    {
        private SerialPort? _serialPort;
        private readonly object _lock = new object();
        
        // Transmission Thread State
        private Thread? _txThread;
        private bool _isTxRunning;
        private readonly byte[] _sharedBuffer = new byte[512];

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public string ConnectionStatus => IsConnected 
            ? $"Connesso su {_serialPort?.PortName} (Open DMX)" 
            : "Sconnesso";

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

                // Clear shared buffer
                Array.Clear(_sharedBuffer, 0, _sharedBuffer.Length);

                // Start transmission thread
                _isTxRunning = true;
                _txThread = new Thread(TxLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Highest, // High priority to reduce transmission jitter
                    Name = "OpenDmxTxThread"
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
                // Update shared buffer (up to 512 channels).
                // Do not clear the rest of the buffer to preserve states of other channels
                int copyLength = Math.Min(dmxData.Length, 512);
                Array.Copy(dmxData, 0, _sharedBuffer, 0, copyLength);
            }
        }

        private void TxLoop()
        {
            byte[] localBuffer = new byte[513];
            localBuffer[0] = 0x00; // DMX Start Code

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
                        Array.Copy(_sharedBuffer, 0, localBuffer, 1, 512);
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
                    // 1. Generate BREAK (low for at least 88us)
                    port.BreakState = true;
                    HighResolutionDelay(100); // 100 microseconds delay

                    // 2. Generate MAB (Mark After Break - high for at least 8us)
                    port.BreakState = false;
                    HighResolutionDelay(12); // 12 microseconds delay

                    // 3. Write data (513 bytes: start code + 512 DMX channels)
                    port.Write(localBuffer, 0, localBuffer.Length);

                    // Sleep to maintain stable frame rate (~30ms interval -> ~33 Hz)
                    Thread.Sleep(30);
                }
                catch (Exception)
                {
                    // Sleep and retry if there's a temporary error
                    Thread.Sleep(100);
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

