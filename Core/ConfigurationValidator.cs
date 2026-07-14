using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using ArtnetNode.Core;

namespace ArtnetNode.Core
{
    public static class ConfigurationValidator
    {
        public static List<string> Validate(ArtnetOptions options)
        {
            var warnings = new List<string>();

            if (options.HttpPort < 1 || options.HttpPort > 65535)
            {
                warnings.Add($"HttpPort {options.HttpPort} non valido. Deve essere tra 1 e 65535.");
            }

            if (options.ArtNetPort < 1 || options.ArtNetPort > 65535)
            {
                warnings.Add($"ArtNetPort {options.ArtNetPort} non valido. Deve essere tra 1 e 65535.");
            }

            if (options.HealthCheckIntervalMs < 100)
            {
                warnings.Add("HealthCheckIntervalMs troppo basso (minimo 100ms).");
            }

            if (options.ReconnectBaseDelayMs < 100)
            {
                warnings.Add("ReconnectBaseDelayMs troppo basso (minimo 100ms).");
            }

            if (options.ReconnectMaxDelayMs < options.ReconnectBaseDelayMs)
            {
                warnings.Add("ReconnectMaxDelayMs non puo essere minore di ReconnectBaseDelayMs.");
            }

            if (options.ReconnectBackoffMultiplier < 1.0)
            {
                warnings.Add("ReconnectBackoffMultiplier deve essere >= 1.0.");
            }

            if (options.HtpTimeoutMs < 0)
            {
                warnings.Add("HtpTimeoutMs non puo essere negativo.");
            }

            if (options.MaxLogFileSizeBytes < 1024)
            {
                warnings.Add("MaxLogFileSizeBytes troppo basso (minimo 1024 bytes).");
            }

            if (options.MaxRetainedLogFiles < 1)
            {
                warnings.Add("MaxRetainedLogFiles deve essere >= 1.");
            }

            if (options.EnableRateLimiting && options.RateLimitMaxRequests < 1)
            {
                warnings.Add("RateLimitMaxRequests deve essere >= 1 quando rate limiting e abilitato.");
            }

            if (options.EnableRateLimiting && options.RateLimitWindowSeconds < 1)
            {
                warnings.Add("RateLimitWindowSeconds deve essere >= 1 quando rate limiting e abilitato.");
            }

            if (options.DmxHeartbeatIntervalMs < 100)
            {
                warnings.Add("DmxHeartbeatIntervalMs troppo basso (minimo 100ms).");
            }

            if (options.RequestTimeoutMs < 1000)
            {
                warnings.Add("RequestTimeoutMs troppo basso (minimo 1000ms).");
            }

            return warnings;
        }

        public static void LogWarnings(ILogger logger, List<string> warnings)
        {
            foreach (var warning in warnings)
            {
                logger.LogWarning(warning);
            }
        }
    }
}
