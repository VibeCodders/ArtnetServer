using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ArtnetNode.Core
{
    public class HealthCheckService : IDisposable
    {
        private readonly ArtnetNodeEngine _engine;
        private readonly ILogger _logger;
        private readonly int _intervalMs;
        private Timer? _timer;
        private bool _disposed;
        private long _totalChecks;
        private long _failedChecks;
        private DateTime? _lastCheckTime;
        private readonly ConcurrentQueue<HealthCheckResult> _history = new();
        private const int MaxHistory = 100;
        
        public event Action<HealthCheckResult>? OnHealthCheckCompleted;

        public HealthCheckService(ArtnetNodeEngine engine, ILogger logger, int intervalMs = 5000)
        {
            _engine = engine;
            _logger = logger;
            _intervalMs = intervalMs;
        }

        public void Start()
        {
            _timer = new Timer(CheckHealth, null, _intervalMs, _intervalMs);
            _logger.LogInformation("Health check service avviato (intervallo: {Interval}ms)", _intervalMs);
        }

        public void Stop()
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.LogInformation("Health check service arrestato. Totali: {Total}, Falliti: {Failed}", _totalChecks, _failedChecks);
        }

        private void CheckHealth(object? state)
        {
            var startTime = DateTime.UtcNow;
            var result = new HealthCheckResult { Timestamp = startTime };

            try
            {
                Interlocked.Increment(ref _totalChecks);
                _lastCheckTime = startTime;

                foreach (var inst in _engine.ActiveInterfaces)
                {
                    var interfaceResult = new InterfaceHealthResult
                    {
                        Universe = inst.Config.Universe,
                        DriverType = inst.Config.DriverType,
                        IsConnected = inst.Interface.IsConnected,
                        IsReconnecting = inst.IsReconnecting,
                        ReconnectAttempt = inst.ReconnectAttempt
                    };

                    if (inst.IsReconnecting)
                    {
                        interfaceResult.Status = HealthStatus.Degraded;
                        interfaceResult.Message = "Riconnessione in corso";
                        result.DegradedCount++;
                    }
                    else if (!inst.Interface.IsConnected)
                    {
                        interfaceResult.Status = HealthStatus.Unhealthy;
                        interfaceResult.Message = "Disconnesso";
                        result.UnhealthyCount++;
                        _engine.HandleDisconnectAndScheduleReconnect(inst);
                    }
                    else
                    {
                        interfaceResult.Status = HealthStatus.Healthy;
                        interfaceResult.Message = "OK";
                        result.HealthyCount++;
                    }

                    result.Interfaces.Add(interfaceResult);
                }

                result.OverallStatus = result.UnhealthyCount > 0 ? HealthStatus.Unhealthy 
                    : result.DegradedCount > 0 ? HealthStatus.Degraded 
                    : HealthStatus.Healthy;

                _logger.LogDebug("Health check completato: {Healthy} OK, {Degraded} Degraded, {Unhealthy} Down", 
                    result.HealthyCount, result.DegradedCount, result.UnhealthyCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedChecks);
                result.OverallStatus = HealthStatus.Unhealthy;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Errore durante health check");
            }
            finally
            {
                result.DurationMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                AddToHistory(result);
                OnHealthCheckCompleted?.Invoke(result);
            }
        }

        private void AddToHistory(HealthCheckResult result)
        {
            _history.Enqueue(result);
            while (_history.Count > MaxHistory)
            {
                _history.TryDequeue(out _);
            }
        }

        public HealthStats GetStats()
        {
            var recent = _history.ToArray();
            var last = recent.LastOrDefault();
            
            return new HealthStats
            {
                TotalChecks = _totalChecks,
                FailedChecks = _failedChecks,
                LastCheckTime = _lastCheckTime,
                LastCheckDurationMs = last?.DurationMs ?? 0,
                LastOverallStatus = last?.OverallStatus ?? HealthStatus.Unknown,
                RecentHistory = recent.TakeLast(10).ToList(),
                IntervalMs = _intervalMs
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer?.Dispose();
                _disposed = true;
            }
        }
    }

    public enum HealthStatus
    {
        Unknown,
        Healthy,
        Degraded,
        Unhealthy
    }

    public class HealthCheckResult
    {
        public DateTime Timestamp { get; set; }
        public HealthStatus OverallStatus { get; set; }
        public int HealthyCount { get; set; }
        public int DegradedCount { get; set; }
        public int UnhealthyCount { get; set; }
        public long DurationMs { get; set; }
        public string? ErrorMessage { get; set; }
        public List<InterfaceHealthResult> Interfaces { get; set; } = new();
    }

    public class InterfaceHealthResult
    {
        public int Universe { get; set; }
        public string DriverType { get; set; } = "";
        public bool IsConnected { get; set; }
        public bool IsReconnecting { get; set; }
        public int ReconnectAttempt { get; set; }
        public HealthStatus Status { get; set; }
        public string Message { get; set; } = "";
    }

    public class HealthStats
    {
        public long TotalChecks { get; set; }
        public long FailedChecks { get; set; }
        public DateTime? LastCheckTime { get; set; }
        public long LastCheckDurationMs { get; set; }
        public HealthStatus LastOverallStatus { get; set; }
        public List<HealthCheckResult> RecentHistory { get; set; } = new();
        public int IntervalMs { get; set; }
    }
}
