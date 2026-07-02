using System;

namespace ArtnetNode.Drivers
{
    public class ChauvetUsbDmxInterface : IDmxInterface
    {
        private bool _connected = false;

        public bool IsConnected => _connected;

        public string ConnectionStatus => _connected 
            ? "Connesso (Chauvet USB - Modalità Simulata)" 
            : "Sconnesso";

        public void Connect(string portName)
        {
            _connected = true;
            // Log informative messages regarding Chauvet integration limitations.
            // Since Chauvet Xpress uses closed proprietary USB interfaces, direct drive is simulated.
            Console.WriteLine("[INFO] Chauvet Xpress USB driver caricato in modalità simulata.");
            Console.WriteLine("[INFO] NOTA: L'hardware Chauvet Xpress è protetto da cifratura proprietaria.");
            Console.WriteLine("[INFO] Per controllare Chauvet ShowXpress, abilitare l'External Control TCP o MIDI.");
        }

        public void Disconnect()
        {
            _connected = false;
        }

        public void SendDmx(byte[] dmxData)
        {
            // Simulation mode: does nothing. DMX data is successfully consumed.
        }
    }
}

