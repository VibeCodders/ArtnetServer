using System;
using System.Text;
using ArtnetNode.Core;
using ArtnetNode.Drivers;
using Xunit;

namespace Artnet.Tests
{
    public class ArtNetPacketParsingTests
    {
        private static byte[] BuildPacket(int universe, byte[] dmx, byte sequence = 0)
        {
            var packet = new byte[18 + dmx.Length];
            var id = Encoding.ASCII.GetBytes("Art-Net\0");
            Array.Copy(id, packet, 8);
            packet[8] = 0x00;
            packet[9] = 0x50; // OpCode 0x5000 (ArtDMX), little-endian
            packet[10] = 0x00;
            packet[11] = 0x0E; // Protocol version 14
            packet[12] = sequence;
            packet[13] = 0x00; // physical
            packet[14] = (byte)(universe & 0xFF);
            packet[15] = (byte)((universe >> 8) & 0x7F);
            packet[16] = (byte)((dmx.Length >> 8) & 0xFF);
            packet[17] = (byte)(dmx.Length & 0xFF);
            Array.Copy(dmx, 0, packet, 18, dmx.Length);
            return packet;
        }

        [Fact]
        public void TryParseDmxPacket_ValidPacket_ParsesUniverseAndData()
        {
            byte[] dmx = new byte[24];
            for (int i = 0; i < dmx.Length; i++) dmx[i] = (byte)(i * 10);

            var packet = BuildPacket(3, dmx, sequence: 42);

            bool ok = ArtNetServer.TryParseDmxPacket(packet, out int universe, out byte sequence, out byte[] parsed);

            Assert.True(ok);
            Assert.Equal(3, universe);
            Assert.Equal(42, sequence);
            Assert.Equal(dmx, parsed);
        }

        [Fact]
        public void TryParseDmxPacket_InvalidHeader_ReturnsFalse()
        {
            var packet = BuildPacket(0, new byte[10]);
            packet[0] = (byte)'X';

            Assert.False(ArtNetServer.TryParseDmxPacket(packet, out _, out _, out _));
        }

        [Fact]
        public void TryParseDmxPacket_WrongOpCode_ReturnsFalse()
        {
            var packet = BuildPacket(0, new byte[10]);
            packet[9] = 0x21; // 0x2100 = ArtPoll

            Assert.False(ArtNetServer.TryParseDmxPacket(packet, out _, out _, out _));
        }

        [Fact]
        public void TryParseDmxPacket_TooShort_ReturnsFalse()
        {
            Assert.False(ArtNetServer.TryParseDmxPacket(new byte[10], out _, out _, out _));
        }

        [Fact]
        public void TryParseDmxPacket_LengthExceeds512_ReturnsFalse()
        {
            var packet = BuildPacket(0, new byte[513]);

            Assert.False(ArtNetServer.TryParseDmxPacket(packet, out _, out _, out _));
        }

        [Fact]
        public void TryParseDmxPacket_TruncatedPayload_ReturnsFalse()
        {
            var full = BuildPacket(0, new byte[100]);
            var truncated = new byte[full.Length - 20];
            Array.Copy(full, truncated, truncated.Length);

            Assert.False(ArtNetServer.TryParseDmxPacket(truncated, out _, out _, out _));
        }

        [Fact]
        public void TryParseDmxPacket_HighUniverse_ParsesCorrectly()
        {
            var packet = BuildPacket(0x123, new byte[5], 0);

            Assert.True(ArtNetServer.TryParseDmxPacket(packet, out int universe, out _, out _));
            Assert.Equal(0x123, universe);
        }
    }
}