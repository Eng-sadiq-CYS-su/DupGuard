using System;
using System.IO;
using System.Threading;

namespace Services
{
    /// <summary>
    /// Simple file and console logger
    /// </summary>
    public class Logger : ILogger
    {
        private readonly string _logFile;
        private readonly object _lock = new();

        public Logger()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appData, "DupGuard", "Logs");
            Directory.CreateDirectory(logDir);
            _logFile = Path.Combine(logDir, $"DupGuard_{DateTime.Now:yyyyMMdd}.log");
        }

        public void LogInfo(string message)
        {
            Log("INFO", message);
        }

        public void LogWarning(string message)
        {
            Log("WARN", message);
        }

        public void LogError(string message, Exception? exception = null)
        {
            var fullMessage = exception != null ? $"{message}: {exception.Message}" : message;
            Log("ERROR", fullMessage);

            if (exception != null)
            {
                Log("ERROR", $"Stack trace: {exception.StackTrace}");
            }
        }

        public void LogDebug(string message)
        {
#if DEBUG
            Log("DEBUG", message);
#endif
        }

        private void Log(string level, string message)
        {
            var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFile, logEntry + Environment.NewLine);
                }
                catch
                {
                    // If file logging fails, continue with console logging
                }

#if DEBUG
                Console.WriteLine(logEntry);
#endif
            }
        }
    }
}
