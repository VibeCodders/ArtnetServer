using ArtnetNode.Core;
using Xunit;

namespace Artnet.Tests
{
    public class ConfigurationValidatorTests
    {
        [Fact]
        public void Validate_ValidOptions_ReturnsNoWarnings()
        {
            var options = new ArtnetOptions();
            var warnings = ConfigurationValidator.Validate(options);
            Assert.Empty(warnings);
        }

        [Fact]
        public void Validate_InvalidPort_ReturnsWarning()
        {
            var options = new ArtnetOptions { HttpPort = 0 };
            var warnings = ConfigurationValidator.Validate(options);
            Assert.Single(warnings);
            Assert.Contains("HttpPort", warnings[0]);
        }

        [Fact]
        public void Validate_NegativeTimeout_ReturnsWarning()
        {
            var options = new ArtnetOptions { HealthCheckIntervalMs = -1 };
            var warnings = ConfigurationValidator.Validate(options);
            Assert.Single(warnings);
            Assert.Contains("HealthCheckIntervalMs", warnings[0]);
        }

        [Fact]
        public void Validate_MaxDelayLessThanBase_ReturnsWarning()
        {
            var options = new ArtnetOptions
            {
                ReconnectBaseDelayMs = 5000,
                ReconnectMaxDelayMs = 1000
            };
            var warnings = ConfigurationValidator.Validate(options);
            Assert.Single(warnings);
            Assert.Contains("ReconnectMaxDelayMs", warnings[0]);
        }
    }
}
