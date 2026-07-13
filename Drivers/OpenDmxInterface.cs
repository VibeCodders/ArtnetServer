using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace ArtnetNode.Drivers
{
    public class OpenDmxInterface : SerialDmxDriverBase
    {
        public override string DriverName => "Open DMX";
        public override int BaudRate => 250000;
        public override Parity Parity => Parity.None;
        public override int DataBits => 8;
        public override StopBits StopBits => StopBits.Two;
        public override Handshake Handshake => Handshake.None;
        public override int TxThreadPriority => (int)ThreadPriority.Highest;
        public override string TxThreadName => "OpenDmxTxThread";

        protected override void TxLoop()
        {
            byte[] localBuffer = new byte[513];
            localBuffer[0] = 0x00;

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
                    port.BreakState = true;
                    HighResolutionDelay(100);

                    port.BreakState = false;
                    HighResolutionDelay(12);

                    SafeWrite(localBuffer, 0, localBuffer.Length, 30);
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
        }

        private void HighResolutionDelay(double microseconds)
        {
            long ticks = (long)(microseconds * Stopwatch.Frequency / 1_000_000);
            long startTicks = Stopwatch.GetTimestamp();
            while (Stopwatch.GetTimestamp() - startTicks < ticks)
            {
                Thread.SpinWait(1);
            }
        }
    }
}
