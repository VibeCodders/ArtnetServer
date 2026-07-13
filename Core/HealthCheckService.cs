using System;
using System.Threading;
using System.Threading.Tasks;
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
            _logger.LogInformation("Health check service arrestato");
        }

        private void CheckHealth(object? state)
        {
            try
            {
                foreach (var inst in _engine.ActiveInterfaces)
                {
                    if (inst.IsReconnecting)
                    {
                        _logger.LogWarning("Health check: Universo {Universe} in stato riconnessione", inst.Config.Universe);
                        continue;
                    }

                    if (!inst.Interface.IsConnected)
                    {
                        _logger.LogError("Health check: Universo {Universe} disconnesso", inst.Config.Universe);
                        _engine.HandleDisconnectAndScheduleReconnect(inst);
                    }
                }

                _logger.LogDebug("Health check completato: {Count} interfacce monitorate", _engine.ActiveInterfaces.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante health check");
            }
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
}
