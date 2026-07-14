using ArtnetNode.Drivers;
using Xunit;

namespace Artnet.Tests
{
    public class DriverFactoryTests
    {
        private readonly DriverFactory _factory = new DriverFactory();

        [Theory]
        [InlineData("simulation", typeof(SimulationDmxInterface))]
        [InlineData("sim", typeof(SimulationDmxInterface))]
        [InlineData("enttec", typeof(EnttecProDmxInterface))]
        [InlineData("pro", typeof(EnttecProDmxInterface))]
        [InlineData("open", typeof(OpenDmxInterface))]
        [InlineData("enttec_mk2", typeof(EnttecProMk2DmxInterface))]
        [InlineData("ftdi_generic", typeof(FtdiGenericDmxInterface))]
        [InlineData("udmx", typeof(UDmxInterface))]
        [InlineData("dmx4all", typeof(Dmx4AllUsbInterface))]
        [InlineData("chauvet", typeof(ChauvetUsbDmxInterface))]
        [InlineData("eurolite_pro", typeof(EuroliteUsbDmxInterface))]
        [InlineData("hid_dmx", typeof(HidDmxInterface))]
        public void CreateDriver_KnownTypes_ReturnsExpectedInterface(string driverType, Type expected)
        {
            IDmxInterface driver = _factory.CreateDriver(driverType, 0);
            Assert.IsAssignableFrom(expected, driver);
        }

        [Fact]
        public void CreateDriver_UnknownType_Throws()
        {
            Assert.Throws<ArgumentException>(() => _factory.CreateDriver("does-not-exist", 0));
        }
    }

    public class MockSerialDriverTests
    {
        private class MockDmxInterface : IDmxInterface
        {
            private bool _connected;
            public byte[]? LastSent { get; private set; }
            public bool IsConnected => _connected;
            public string ConnectionStatus => _connected ? "Mock connected" : "Mock disconnected";

            public void Connect(string portName) => _connected = true;
            public void Disconnect() => _connected = false;
            public void SendDmx(byte[] dmxData)
            {
                LastSent = new byte[dmxData.Length];
                Array.Copy(dmxData, LastSent, dmxData.Length);
            }
        }

        [Fact]
        public void MockDriver_SendDmx_StoresProvidedData()
        {
            var driver = new MockDmxInterface();
            byte[] data = new byte[512];
            data[0] = 255;
            data[10] = 128;

            driver.Connect("MOCK");
            Assert.True(driver.IsConnected);

            driver.SendDmx(data);

            Assert.NotNull(driver.LastSent);
            Assert.Equal(255, driver.LastSent![0]);
            Assert.Equal(128, driver.LastSent[10]);
        }

        [Fact]
        public void MockDriver_Disconnect_ClearsConnectionState()
        {
            var driver = new MockDmxInterface();
            driver.Connect("MOCK");
            Assert.True(driver.IsConnected);

            driver.Disconnect();
            Assert.False(driver.IsConnected);
        }
    }
}