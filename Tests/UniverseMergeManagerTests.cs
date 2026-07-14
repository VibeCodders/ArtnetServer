using ArtnetNode.Core;
using Xunit;

namespace Artnet.Tests
{
    public class UniverseMergeManagerTests
    {
        [Fact]
        public void HtpMerge_TakesHighestValueAcrossSources()
        {
            var manager = new UniverseMergeManager(htpTimeoutMs: 1000, defaultMode: MergeMode.Htp);
            manager.RegisterUniverse(0);

            manager.UpdateUniverse(0, MakeDmx(1, 100), "192.168.0.1", 0);
            manager.UpdateUniverse(0, MakeDmx(1, 80), "192.168.0.2", 0);

            byte[] merged = manager.GetMergedDmx(0);
            Assert.Equal(100, merged[0]);
        }

        [Fact]
        public void LtpMerge_LastWriterWins()
        {
            var manager = new UniverseMergeManager(htpTimeoutMs: 1000, defaultMode: MergeMode.Ltp);
            manager.RegisterUniverse(0);

            manager.UpdateUniverse(0, MakeDmx(1, 100), "192.168.0.1", 0);
            manager.UpdateUniverse(0, MakeDmx(1, 80), "192.168.0.2", 0);

            byte[] merged = manager.GetMergedDmx(0);
            Assert.Equal(80, merged[0]);
        }

        [Fact]
        public void HtpMerge_PerChannelHighestIsSelected()
        {
            var manager = new UniverseMergeManager(htpTimeoutMs: 1000, defaultMode: MergeMode.Htp);

            byte[] sourceA = new byte[512];
            sourceA[0] = 200;
            sourceA[1] = 10;

            byte[] sourceB = new byte[512];
            sourceB[0] = 50;
            sourceB[1] = 180;

            manager.UpdateUniverse(0, sourceA, "A", 0);
            manager.UpdateUniverse(0, sourceB, "B", 0);

            byte[] merged = manager.GetMergedDmx(0);
            Assert.Equal(200, merged[0]);
            Assert.Equal(180, merged[1]);
        }

        [Fact]
        public void GetMergedDmx_UnknownUniverse_ReturnsEmptyBuffer()
        {
            var manager = new UniverseMergeManager();
            byte[] merged = manager.GetMergedDmx(99);

            Assert.Equal(512, merged.Length);
            Assert.All(merged, v => Assert.Equal(0, v));
        }

        [Fact]
        public void UpdateUniverse_ExplicitLtpOverridesDefault()
        {
            var manager = new UniverseMergeManager(defaultMode: MergeMode.Htp);

            manager.UpdateUniverse(0, MakeDmx(1, 100), "A", 0, MergeMode.Ltp);
            manager.UpdateUniverse(0, MakeDmx(1, 40), "B", 0, MergeMode.Ltp);

            byte[] merged = manager.GetMergedDmx(0);
            Assert.Equal(40, merged[0]);
        }

        private static byte[] MakeDmx(int channelIndex, byte value)
        {
            byte[] dmx = new byte[512];
            if (channelIndex >= 0 && channelIndex < dmx.Length)
                dmx[channelIndex] = value;
            return dmx;
        }
    }
}