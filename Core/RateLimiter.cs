using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ArtnetNode.Core
{
    public class RateLimiter
    {
        private readonly int _maxRequests;
        private readonly int _windowSeconds;
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _requests = new();
        private readonly object _cleanupLock = new object();
        
        private long _totalRequests;
        private long _totalRejected;

        public long TotalRequestsProcessed => Interlocked.Read(ref _totalRequests);
        public long TotalRequestsRejected => Interlocked.Read(ref _totalRejected);
        public int ActiveClients => _requests.Count;

        public RateLimiter(int maxRequests, int windowSeconds)
        {
            _maxRequests = maxRequests;
            _windowSeconds = windowSeconds;
        }

        public bool IsAllowed(string clientIp)
        {
            if (string.IsNullOrEmpty(clientIp)) clientIp = "unknown";

            Interlocked.Increment(ref _totalRequests);
            
            if (!_requests.TryGetValue(clientIp, out var queue))
            {
                queue = new Queue<DateTime>();
                _requests[clientIp] = queue;
            }

            var cutoff = DateTime.UtcNow.AddSeconds(-_windowSeconds);
            while (queue.Count > 0 && queue.Peek() < cutoff)
            {
                queue.Dequeue();
            }

            if (queue.Count >= _maxRequests)
            {
                Interlocked.Increment(ref _totalRejected);
                return false;
            }

            queue.Enqueue(DateTime.UtcNow);
            return true;
        }

        public void Cleanup()
        {
            lock (_cleanupLock)
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-_windowSeconds * 2);
                var keysToRemove = new List<string>();

                foreach (var kvp in _requests)
                {
                    while (kvp.Value.Count > 0 && kvp.Value.Peek() < cutoff)
                    {
                        kvp.Value.Dequeue();
                    }
                    if (kvp.Value.Count == 0)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _requests.TryRemove(key, out _);
                }
            }
        }
        
        public RateLimitStats GetStats()
        {
            return new RateLimitStats
            {
                TotalRequests = TotalRequestsProcessed,
                TotalRejected = TotalRequestsRejected,
                ActiveClients = ActiveClients,
                MaxRequestsPerWindow = _maxRequests,
                WindowSeconds = _windowSeconds
            };
        }
        
        public class RateLimitStats
        {
            public long TotalRequests { get; set; }
            public long TotalRejected { get; set; }
            public int ActiveClients { get; set; }
            public int MaxRequestsPerWindow { get; set; }
            public int WindowSeconds { get; set; }
        }
    }
}
