using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using ArtnetNode.Core;

namespace ArtnetNode.Core
{
    public class DmxHeartbeatService : IDisposable
    {
        private readonly ArtnetNodeEngine _engine;
        private readonly ILogger _logger;
        private readonly int _intervalMs;
        private Timer? _timer;
        private bool _disposed;
        private int _heartbeatCount;

        public DmxHeartbeatService(ArtnetNodeEngine engine, ILogger logger, int intervalMs = 5000)
        {
            _engine = engine;
            _logger = logger;
            _intervalMs = intervalMs;
        }

        public void Start()
        {
            _timer = new Timer(SendHeartbeat, null, _intervalMs, _intervalMs);
            _logger.LogInformation("DMX Heartbeat service avviato (intervallo: {Interval}ms)", _intervalMs);
        }

        public void Stop()
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.LogInformation("DMX Heartbeat service arrestato (heartbeat inviati: {Count})", _heartbeatCount);
        }

        private void SendHeartbeat(object? state)
        {
            try
            {
                if (!_engine.IsRunning || _engine.ActiveInterfaces.Count == 0) return;

                foreach (var inst in _engine.ActiveInterfaces)
                {
                    if (!inst.Interface.IsConnected || inst.IsReconnecting) continue;

                    try
                    {
                        byte[] currentDmx = _engine.GetCurrentMergedDmx(inst.Config.Universe);
                        inst.Interface.SendDmx(currentDmx);
                        _heartbeatCount++;
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante l'invio del heartbeat DMX");
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
