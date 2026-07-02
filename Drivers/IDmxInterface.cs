using System;

namespace ArtnetNode.Drivers
{
    public interface IDmxInterface
    {
        void Connect(string portName);
        void Disconnect();
        void SendDmx(byte[] dmxData);
        bool IsConnected { get; }
        string ConnectionStatus { get; }
    }
}

