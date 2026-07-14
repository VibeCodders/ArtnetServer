using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ArtnetNode.Core
{
    public class RateLimiter
    {
        private readonly int _maxRequests;
        private readonly int _windowSeconds;
        private readonly Dictionary<string, Queue<DateTime>> _requests = new Dictionary<string, Queue<DateTime>>();
        private readonly object _lock = new object();

        public RateLimiter(int maxRequests, int windowSeconds)
        {
            _maxRequests = maxRequests;
            _windowSeconds = windowSeconds;
        }

        public bool IsAllowed(string clientIp)
        {
            if (string.IsNullOrEmpty(clientIp)) clientIp = "unknown";

            lock (_lock)
            {
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
                    return false;
                }

                queue.Enqueue(DateTime.UtcNow);
                return true;
            }
        }

        public void Cleanup()
        {
            lock (_lock)
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
                    _requests.Remove(key);
                }
            }
        }
    }
}
