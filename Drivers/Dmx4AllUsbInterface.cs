using System;
using System.IO.Ports;
using System.Threading;

namespace ArtnetNode.Drivers
{
    public class Dmx4AllUsbInterface : IDmxInterface
    {
        private SerialPort? _serialPort;
        private readonly object _lock = new object();
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

                _serialPort = new SerialPort(portName, 38400, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    WriteTimeout = 500,
                    DtrEnable = true,
                    RtsEnable = true
                };

                _serialPort.Open();
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                Array.Clear(_sharedBuffer, 0, _sharedBuffer.Length);

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

        public void SendDmx(byte[] dmxData)
        {
            lock (_lock)
            {
                int copyLength = Math.Min(dmxData.Length, 512);
                Array.Copy(dmxData, 0, _sharedBuffer, 0, copyLength);
            }
        }

        private void TxLoop()
        {
            byte[] packet1 = new byte[259];
            packet1[0] = 0xFF;
            packet1[1] = 0x01;
            packet1[2] = 0x00;
            packet1[3] = 255;

            byte[] packet2 = new byte[259];
            packet2[0] = 0xFF;
            packet2[1] = 0x00;
            packet2[2] = 0x01;
            packet2[3] = 255;

            byte[] packet3 = new byte[6];
            packet3[0] = 0xFF;
            packet3[1] = 0xFF;
            packet3[2] = 0x01;
            packet3[3] = 2;

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
                    port.Write(packet1, 0, packet1.Length);
                    port.Write(packet2, 0, packet2.Length);
                    port.Write(packet3, 0, packet3.Length);
                    Thread.Sleep(45);
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
