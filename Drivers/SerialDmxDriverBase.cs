using System;
using System.Diagnostics;
using System.IO.Ports;

namespace ArtnetNode.Drivers
{
    public abstract class SerialDmxDriverBase : IDmxInterface
    {
        protected SerialPort? _serialPort;
        protected readonly object _lock = new object();
        protected Thread? _txThread;
        protected bool _isTxRunning;
        protected readonly byte[] _sharedBuffer = new byte[512];

        protected const long MinFrameIntervalUs = 25000;

        private Stopwatch? _txWatchdog = new Stopwatch();
        private long _lastWriteTimestampTicks;
        private readonly object _watchdogLock = new object();
        protected const int TxWatchdogTimeoutMs = 1000;

        protected bool IsTxStalled
        {
            get
            {
                lock (_watchdogLock)
                {
                    if (_txWatchdog == null || !_txWatchdog.IsRunning)
                        return false;
                    return _txWatchdog.ElapsedMilliseconds > TxWatchdogTimeoutMs;
                }
            }
        }

        public bool TxStalled => IsTxStalled;

        protected void MarkTxAlive()
        {
            lock (_watchdogLock)
            {
                if (_txWatchdog != null)
                {
                    _txWatchdog.Restart();
                }
                _lastWriteTimestampTicks = Stopwatch.GetTimestamp();
            }
        }

        public string ConnectionStatus => IsConnected 
            ? $"Connesso su {_serialPort?.PortName} ({DriverName})" 
            : "Sconnesso";

        public abstract string DriverName { get; }
        public abstract int BaudRate { get; }
        public abstract Parity Parity { get; }
        public abstract int DataBits { get; }
        public abstract StopBits StopBits { get; }
        public abstract Handshake Handshake { get; }
        public abstract int TxThreadPriority { get; }
        public abstract string TxThreadName { get; }

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public virtual void Connect(string portName)
        {
            lock (_lock)
            {
                Disconnect();

                if (string.IsNullOrEmpty(portName))
                    throw new ArgumentException("Il nome della porta COM non può essere vuoto.");

                _serialPort = new SerialPort(portName, BaudRate, Parity, DataBits, StopBits)
                {
                    Handshake = Handshake,
                    WriteTimeout = 500,
                    ReadTimeout = 500,
                    WriteBufferSize = 4096,
                    ReadBufferSize = 4096,
                    DtrEnable = true,
                    RtsEnable = true
                };

                _serialPort.Open();
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                Array.Clear(_sharedBuffer, 0, _sharedBuffer.Length);

                _isTxRunning = true;
                lock (_watchdogLock)
                {
                    _txWatchdog = Stopwatch.StartNew();
                    _lastWriteTimestampTicks = Stopwatch.GetTimestamp();
                }
                _txThread = new Thread(TxLoop)
                {
                    IsBackground = true,
                    Priority = (ThreadPriority)TxThreadPriority,
                    Name = TxThreadName
                };
                _txThread.Start();
            }
        }

        public virtual void Disconnect()
        {
            lock (_lock)
            {
                _isTxRunning = false;
            }

            if (_txThread != null)
            {
                if (_txThread.IsAlive)
                {
                    _txThread.Join(500);
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
                    }
                    finally
                    {
                        _serialPort.Dispose();
                        _serialPort = null;
                    }
                }
            }
        }

        public virtual void SendDmx(byte[] dmxData)
        {
            lock (_lock)
            {
                int copyLength = Math.Min(dmxData.Length, 512);
                Array.Copy(dmxData, 0, _sharedBuffer, 0, copyLength);
            }
        }

        protected abstract void TxLoop();

        protected void SafeWrite(byte[] buffer, int offset, int count, int baudRate)
        {
            try
            {
                _serialPort?.Write(buffer, offset, count);
            }
            catch
            {
            }
            finally
            {
                MarkTxAlive();

                long transmissionTimeUs = (long)((double)count * 10 * 1_000_000 / baudRate);
                long minFrameSpacingUs = Math.Max(0, MinFrameIntervalUs - transmissionTimeUs);
                if (minFrameSpacingUs > 0)
                {
                    Thread.Sleep(TimeSpan.FromMicroseconds(minFrameSpacingUs));
                }
            }
        }
    }
}
