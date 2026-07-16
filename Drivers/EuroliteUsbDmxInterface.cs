using System;
using System.IO.Ports;
using System.Threading;

namespace ArtnetNode.Drivers
{
    public class EuroliteUsbDmxInterface : SerialDmxDriverBase
    {
        public override string DriverName => "Eurolite USB-DMX512 Pro";
        public override int BaudRate => 115200;
        public override Parity Parity => Parity.None;
        public override int DataBits => 8;
        public override StopBits StopBits => StopBits.One;
        public override Handshake Handshake => Handshake.None;
        public override int TxThreadPriority => (int)ThreadPriority.Normal;
        public override string TxThreadName => "EuroliteProTxThread";

        protected override void TxLoop()
        {
            byte[] localBuffer = new byte[518];
            localBuffer[0] = 0x7E;
            localBuffer[1] = 6;
            localBuffer[2] = (byte)(513 & 0xFF);
            localBuffer[3] = (byte)((513 >> 8) & 0xFF);
            localBuffer[4] = 0x00;
            localBuffer[517] = 0xE7;

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
                    SafeWrite(localBuffer, 0, localBuffer.Length, BaudRate);
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
