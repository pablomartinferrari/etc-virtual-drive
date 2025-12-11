using System;
using System.IO;

namespace ETCStorageHelper.Logging
{
    /// <summary>
    /// Simple file-based logger (writes to local log file)
    /// For production, consider using database or centralized logging system
    /// </summary>
    public class FileLogger : IETCLogger
    {
        private readonly string _logFilePath;
        private readonly object _lock = new object();

        /// <summary>
        /// Creates a file logger
        /// </summary>
        /// <param name="logFilePath">Path to log file (default: %TEMP%\ETCStorageHelper.log)</param>
        public FileLogger(string logFilePath = null)
        {
            _logFilePath = logFilePath ?? Path.Combine(
                Path.GetTempPath(),
                "ETCStorageHelper.log"
            );

            // Create log file with header if it doesn't exist
            if (!File.Exists(_logFilePath))
            {
                lock (_lock)
                {
                    try
                    {
                        File.WriteAllText(_logFilePath, 
                            "=== ETC Storage Helper - Transaction Log ===" + Environment.NewLine +
                            $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                            Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FileLogger] Failed to create log file: {ex.Message}");
                    }
                }
            }
        }

        public void Log(LogEntry entry)
        {
            if (entry == null)
                return;

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, entry.ToString() + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    // Don't throw - logging failures shouldn't break the application
                    System.Diagnostics.Debug.WriteLine($"[FileLogger] Failed to write log: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Get the log file path
        /// </summary>
        public string GetLogFilePath()
        {
            return _logFilePath;
        }
    }

    /// <summary>
    /// CSV file logger for easy import into Excel/databases
    /// </summary>
    public class CsvLogger : IETCLogger
    {
        private readonly string _logFilePath;
        private readonly object _lock = new object();

        public CsvLogger(string logFilePath = null)
        {
            _logFilePath = logFilePath ?? Path.Combine(
                Path.GetTempPath(),
                "ETCStorageHelper.csv"
            );

            // Create CSV with header if it doesn't exist
            if (!File.Exists(_logFilePath))
            {
                lock (_lock)
                {
                    try
                    {
                        File.WriteAllText(_logFilePath, LogEntry.CsvHeader() + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CsvLogger] Failed to create CSV file: {ex.Message}");
                    }
                }
            }
        }

        public void Log(LogEntry entry)
        {
            if (entry == null)
                return;

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, entry.ToCsv() + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CsvLogger] Failed to write log: {ex.Message}");
                }
            }
        }

        public string GetLogFilePath()
        {
            return _logFilePath;
        }
    }

    /// <summary>
    /// Logs to Debug output (visible in Visual Studio Output window)
    /// </summary>
    public class DebugLogger : IETCLogger
    {
        public void Log(LogEntry entry)
        {
            if (entry == null)
                return;

            string levelIcon = entry.Level == LogLevel.Info ? "✓" : 
                             entry.Level == LogLevel.Warning ? "⚠" : "✗";
            System.Diagnostics.Debug.WriteLine($"[ETCStorage] {levelIcon} [{entry.Level}] {entry}");
        }
    }

    /// <summary>
    /// Composite logger - logs to multiple destinations
    /// </summary>
    public class CompositeLogger : IETCLogger
    {
        private readonly IETCLogger[] _loggers;

        public CompositeLogger(params IETCLogger[] loggers)
        {
            _loggers = loggers ?? new IETCLogger[0];
        }

        public void Log(LogEntry entry)
        {
            foreach (var logger in _loggers)
            {
                try
                {
                    logger?.Log(entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CompositeLogger] Logger failed: {ex.Message}");
                }
            }
        }
    }
}

