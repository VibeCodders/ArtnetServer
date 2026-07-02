using System;
using System.Runtime.InteropServices;

namespace ArtnetNode.Drivers
{
    public class UDmxInterface : IDmxInterface
    {
        private const string DllName = "uDMX.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        private static extern bool Configure();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        private static extern bool Connected();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        private static extern bool ChannelSet(int channel, byte value);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        private static extern bool ChannelsSet(int channelCnt, int channel, byte[] values);

        private bool _connectedState = false;
        private string _statusMessage = "Sconnesso";

        public bool IsConnected => _connectedState;

        public string ConnectionStatus => _statusMessage;

        public void Connect(string portName)
        {
            try
            {
                // uDMX doesn't use standard COM ports, it communicates via USB directly.
                // We attempt to call Configure or Connected to verify the DLL and device presence.
                bool success = Configure();
                
                if (success && Connected())
                {
                    _connectedState = true;
                    _statusMessage = "Connesso (uDMX)";
                }
                else
                {
                    _connectedState = false;
                    _statusMessage = "Errore: uDMX non rilevato o impossibile configurare.";
                }
            }
            catch (DllNotFoundException)
            {
                _connectedState = false;
                _statusMessage = "Errore: uDMX.dll non trovata. Copiala nella directory dell'applicazione.";
                throw new Exception("Libreria 'uDMX.dll' non trovata nel percorso di sistema o dell'applicazione.");
            }
            catch (BadImageFormatException)
            {
                _connectedState = false;
                _statusMessage = "Errore: Architettura uDMX.dll non corrispondente (32/64 bit).";
                throw new Exception("Impossibile caricare 'uDMX.dll'. Verifica l'architettura x86/x64 dell'applicazione.");
            }
            catch (Exception ex)
            {
                _connectedState = false;
                _statusMessage = $"Errore uDMX: {ex.Message}";
                throw;
            }
        }

        public void Disconnect()
        {
            _connectedState = false;
            _statusMessage = "Sconnesso";
        }

        public void SendDmx(byte[] dmxData)
        {
            if (!_connectedState) return;

            try
            {
                int sendLength = Math.Min(dmxData.Length, 512);
                if (sendLength > 0)
                {
                    // ChannelsSet starts channel index at 1
                    bool result = ChannelsSet(sendLength, 1, dmxData);
                    if (!result)
                    {
                        _statusMessage = "Errore durante l'invio dei dati DMX.";
                    }
                }
            }
            catch (Exception ex)
            {
                _connectedState = false;
                _statusMessage = $"Errore durante la trasmissione: {ex.Message}";
            }
        }
    }
}

