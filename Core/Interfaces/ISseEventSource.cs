using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArtnetNode.Core
{
    public class SseEvent
    {
        public string Event { get; set; } = "";
        public string Data { get; set; } = "";
        public int Id { get; set; }
    }

    public interface ISseEventSource
    {
        event EventHandler<SseEvent>? OnEvent;
        void Start(CancellationToken token);
        void Stop();
    }
}
