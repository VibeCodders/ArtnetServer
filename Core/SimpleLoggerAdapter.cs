using System;
using ArtnetNode.Core.Logging;

namespace ArtnetNode.Core
{
    public class SimpleLoggerAdapter
    {
        private readonly Action<string> _logAction;

        public SimpleLoggerAdapter(Action<string> logAction)
        {
            _logAction = logAction;
        }

        public void Log(string message)
        {
            _logAction(message);
        }
    }
}
