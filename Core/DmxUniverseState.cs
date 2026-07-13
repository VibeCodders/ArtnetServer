using System;
using System.Threading;
using System.Threading.Tasks;
using ArtnetNode.Core.Interfaces;

namespace ArtnetNode.Core
{
    public class DmxUniverseState
    {
        public int Universe { get; }
        public byte[] CurrentDmx { get; }
        public byte[] LastReceivedDmx { get; }
        public DateTime LastUpdate { get; set; } = DateTime.MinValue;
        public string LastSourceIp { get; set; } = "";
        public byte LastSequence { get; set; }
        public bool IsStale => (DateTime.Now - LastUpdate).TotalMilliseconds > 2000;

        public DmxUniverseState(int universe)
        {
            Universe = universe;
            CurrentDmx = new byte[512];
            LastReceivedDmx = new byte[512];
        }

        public void Update(byte[] dmxData, string sourceIp, byte sequence)
        {
            int len = Math.Min(dmxData.Length, 512);
            Array.Clear(LastReceivedDmx, 0, 512);
            Array.Copy(dmxData, 0, LastReceivedDmx, 0, len);
            LastUpdate = DateTime.Now;
            LastSourceIp = sourceIp;
            LastSequence = sequence;
        }
    }
}
