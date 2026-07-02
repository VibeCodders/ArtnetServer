using System;

namespace ArtnetNode.Drivers
{
    public class SimulationDmxInterface : IDmxInterface
    {
        private bool _isConnected;
        private string _portName = "Simulatore";

        public bool IsConnected => _isConnected;

        public string ConnectionStatus => _isConnected 
            ? $"Simulazione Attiva (Mock)" 
            : "Simulazione Spenta";

        public void Connect(string portName)
        {
            _portName = string.IsNullOrEmpty(portName) ? "Simulatore" : portName;
            _isConnected = true;
        }

        public void Disconnect()
        {
            _isConnected = false;
        }

        public void SendDmx(byte[] dmxData)
        {
            // Non fa nulla all'hardware, i dati vengono visualizzati direttamente tramite la GUI
        }
    }
}

