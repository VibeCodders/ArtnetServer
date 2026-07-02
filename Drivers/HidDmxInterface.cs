using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ArtnetNode.Drivers
{
    public class HidDmxInterface : IDmxInterface
    {
        private const string DllName = "K8062D.dll";

        [DllImport(DllName, EntryPoint = "StartDevice")]
        private static extern void StartDevice();

        [DllImport(DllName, EntryPoint = "StopDevice")]
        private static extern void StopDevice();

        [DllImport(DllName, EntryPoint = "SetChannelCount")]
        private static extern void SetChannelCount(int count);

        [DllImport(DllName, EntryPoint = "SetData")]
        private static extern void SetData(int channel, int data);

        private bool _connectedState = false;
        private string _statusMessage = "Sconnesso";

        // Transmission Thread State
        private Thread? _txThread;
        private bool _isTxRunning;
        private readonly byte[] _sharedBuffer = new byte[512];
        private readonly object _lock = new object();

        public bool IsConnected => _connectedState;

        public string ConnectionStatus => _statusMessage;

        public void Connect(string portName)
        {
            try
            {
                // Start K8062 device
                StartDevice();
                SetChannelCount(512);

                _connectedState = true;
                _statusMessage = "Connesso (K8062 HID)";

                // Clear shared buffer
                lock (_lock)
                {
                    Array.Clear(_sharedBuffer, 0, _sharedBuffer.Length);
                }

                // Start transmission thread for differential updates
                _isTxRunning = true;
                _txThread = new Thread(TxLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Normal,
                    Name = "HidDmxTxThread"
                };
                _txThread.Start();
            }
            catch (DllNotFoundException)
            {
                _connectedState = false;
                _statusMessage = "Errore: K8062D.dll non trovata. Copiala nella directory dell'applicazione.";
                throw new Exception("Libreria 'K8062D.dll' non trovata nel percorso di sistema o dell'applicazione.");
            }
            catch (BadImageFormatException)
            {
                _connectedState = false;
                _statusMessage = "Errore: Architettura K8062D.dll non corrispondente (32/64 bit).";
                throw new Exception("Impossibile caricare 'K8062D.dll'. Verifica l'architettura x86/x64 dell'applicazione.");
            }
            catch (Exception ex)
            {
                _connectedState = false;
                _statusMessage = $"Errore HID DMX: {ex.Message}";
                throw;
            }
        }

        public void Disconnect()
        {
            _isTxRunning = false;

            if (_txThread != null)
            {
                if (_txThread.IsAlive)
                {
                    _txThread.Join(500);
                }
                _txThread = null;
            }

            if (_connectedState)
            {
                try
                {
                    StopDevice();
                }
                catch
                {
                    // Ignore exceptions during close
                }
                _connectedState = false;
            }

            _statusMessage = "Sconnesso";
        }

        public void SendDmx(byte[] dmxData)
        {
            if (!_connectedState) return;

            lock (_lock)
            {
                int copyLength = Math.Min(dmxData.Length, 512);
                Array.Copy(dmxData, 0, _sharedBuffer, 0, copyLength);
            }
        }

        private void TxLoop()
        {
            byte[] localBuffer = new byte[512];
            byte[] lastBuffer = new byte[512];

            // Initialize both to 0, but set lastBuffer to 255 to force initial synchronization of any non-zero values
            Array.Fill<byte>(lastBuffer, 255);

            while (_isTxRunning)
            {
                bool hasChanged = false;

                lock (_lock)
                {
                    Array.Copy(_sharedBuffer, 0, localBuffer, 0, 512);
                }

                try
                {
                    // Write only channels that have changed to minimize P/Invoke overhead
                    for (int i = 0; i < 512; i++)
                    {
                        byte newVal = localBuffer[i];
                        if (newVal != lastBuffer[i])
                        {
                            // K8062 channel index is 1-indexed (1 to 512)
                            SetData(i + 1, newVal);
                            lastBuffer[i] = newVal;
                            hasChanged = true;
                        }
                    }
                }
                catch (Exception)
                {
                    // If error occurs, break loop
                    break;
                }

                // If nothing changed, we can sleep a bit longer to save CPU.
                // If something changed, sleep 30ms to maintain smooth transition timing.
                Thread.Sleep(hasChanged ? 30 : 100);
            }
        }
    }
}

