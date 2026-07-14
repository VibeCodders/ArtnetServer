using ArtnetNode.Core;
using Xunit;

namespace Artnet.Tests
{
    public class RateLimiterTests
    {
        [Fact]
        public void IsAllowed_UnderLimit_ReturnsTrue()
        {
            var limiter = new RateLimiter(5, 60);
            Assert.True(limiter.IsAllowed("192.168.1.1"));
            Assert.True(limiter.IsAllowed("192.168.1.1"));
        }

        [Fact]
        public void IsAllowed_OverLimit_ReturnsFalse()
        {
            var limiter = new RateLimiter(2, 60);
            Assert.True(limiter.IsAllowed("192.168.1.1"));
            Assert.True(limiter.IsAllowed("192.168.1.1"));
            Assert.False(limiter.IsAllowed("192.168.1.1"));
        }

        [Fact]
        public void IsAllowed_DifferentClients_Independent()
        {
            var limiter = new RateLimiter(2, 60);
            Assert.True(limiter.IsAllowed("192.168.1.1"));
            Assert.True(limiter.IsAllowed("192.168.1.1"));
            Assert.False(limiter.IsAllowed("192.168.1.1"));
            Assert.True(limiter.IsAllowed("192.168.1.2"));
        }

        [Fact]
        public void IsAllowed_NullIp_UsesUnknown()
        {
            var limiter = new RateLimiter(2, 60);
            Assert.True(limiter.IsAllowed(string.Empty));
            Assert.True(limiter.IsAllowed(string.Empty));
            Assert.False(limiter.IsAllowed(string.Empty));
        }
    }
}
