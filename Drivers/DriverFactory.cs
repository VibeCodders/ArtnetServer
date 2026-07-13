using System;
using System.Linq;
using ArtnetNode.Core.Interfaces;

namespace ArtnetNode.Drivers
{
    public class DriverFactory : IDriverFactory
    {
        public IDmxInterface CreateDriver(string driverType, int universe)
        {
            return driverType.ToLowerInvariant() switch
            {
                "simulation" or "sim" => new SimulationDmxInterface(),
                "enttec" or "pro" or "enttecpro" => new EnttecProDmxInterface(),
                "open" or "opendmx" => new OpenDmxInterface(),
                "enttec_mk2" or "enttecmk2" => new EnttecProMk2DmxInterface { UniversePort = (universe % 2) == 0 ? 1 : 2 },
                "ftdi_generic" or "ftdigeneric" => new FtdiGenericDmxInterface(),
                "udmx" => new UDmxInterface(),
                "dmx4all" => new Dmx4AllUsbInterface(),
                "chauvet" => new ChauvetUsbDmxInterface(),
                "eurolite_pro" or "eurolitepro" => new EuroliteUsbDmxInterface(),
                "hid_dmx" or "hiddmx" => new HidDmxInterface(),
                _ => throw new ArgumentException($"Driver DMX non riconosciuto: '{driverType}'")
            };
        }
    }
}
