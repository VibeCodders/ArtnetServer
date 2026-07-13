using System;

namespace ArtnetNode.Core
{
    public class ArtnetOptions
    {
        public string BindIpAddress { get; set; } = "0.0.0.0";
        public int TargetUniverse { get; set; } = 0;
        public int ArtNetPort { get; set; } = 6454;
        public string DefaultDriver { get; set; } = "simulation";
        public string DefaultComPort { get; set; } = "";
        public int HttpPort { get; set; } = 8080;
        public string LogPath { get; set; } = "logs/artnet.log";
        public int MaxLogFileSizeBytes { get; set; } = 5 * 1024 * 1024;
        public int MaxRetainedLogFiles { get; set; } = 3;
        public int ReconnectBaseDelayMs { get; set; } = 1000;
        public int ReconnectMaxDelayMs { get; set; } = 30000;
        public double ReconnectBackoffMultiplier { get; set; } = 2.0;
        public bool EnableHealthChecks { get; set; } = true;
        public int HealthCheckIntervalMs { get; set; } = 5000;
        public bool EnableHtpMerge { get; set; } = true;
        public int HtpTimeoutMs { get; set; } = 1000;
        public MergeMode DefaultMergeMode { get; set; } = MergeMode.Htp;
        public string ApiToken { get; set; } = "";
        public string CorsOrigins { get; set; } = "http://localhost:*";
    }

    public enum MergeMode
    {
        Htp,
        Ltp
    }
}
