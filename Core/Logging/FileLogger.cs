using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ArtnetNode.Core.Logging
{
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _logPath;
        private readonly int _maxFileSizeBytes;
        private readonly int _maxRetainedFiles;
        private readonly object _fileLock = new object();
        private StreamWriter? _writer;

        public FileLoggerProvider(string logPath, int maxFileSizeBytes = 5 * 1024 * 1024, int maxRetainedFiles = 3)
        {
            _logPath = logPath;
            _maxFileSizeBytes = maxFileSizeBytes;
            _maxRetainedFiles = maxRetainedFiles;
            InitializeWriter();
        }

        private void InitializeWriter()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _writer = new StreamWriter(new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };
            }
            catch
            {
                _writer = null;
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(this, categoryName);
        }

        private void WriteMessage(string message)
        {
            if (_writer == null) return;

            lock (_fileLock)
            {
                try
                {
                    string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{message}]";
                    _writer.WriteLine(logEntry);

                    var fileInfo = new FileInfo(_logPath);
                    if (fileInfo.Exists && fileInfo.Length > _maxFileSizeBytes)
                    {
                        RollLogFiles();
                    }
                }
                catch
                {
                }
            }
        }

        private void RollLogFiles()
        {
            try
            {
                _writer?.Dispose();
                _writer = null;

                for (int i = _maxRetainedFiles - 1; i >= 1; i--)
                {
                    string source = _logPath + (i == 1 ? "" : $".{i - 1}");
                    string target = _logPath + $".{i}";
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                    if (File.Exists(source))
                    {
                        File.Move(source, target);
                    }
                }

                InitializeWriter();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            lock (_fileLock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private class FileLogger : ILogger
        {
            private readonly FileLoggerProvider _provider;
            private readonly string _categoryName;

            public FileLogger(FileLoggerProvider provider, string categoryName)
            {
                _provider = provider;
                _categoryName = categoryName;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                var message = formatter(state, exception);
                var fullMessage = $"{_categoryName}: {message}";
                if (exception != null)
                {
                    fullMessage += $" | Exception: {exception}";
                }
                _provider.WriteMessage(fullMessage);
            }
        }
    }
}
