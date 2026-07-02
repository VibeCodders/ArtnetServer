using System;

namespace ArtnetNode
{
    public class DmxInterfaceConfig
    {
        public int Universe { get; set; } = 0;
        public string DriverType { get; set; } = "simulation";
        public string ComPort { get; set; } = "";

        public override string ToString()
        {
            string portText = string.IsNullOrEmpty(ComPort) ? "" : $" ({ComPort})";
            return $"Universo {Universe}: {DriverType}{portText}";
        }
    }
}
