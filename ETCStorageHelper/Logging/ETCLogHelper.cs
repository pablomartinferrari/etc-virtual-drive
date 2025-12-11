using System;
using System.Diagnostics;

namespace ETCStorageHelper.Logging
{
    /// <summary>
    /// Helper for logging ETC Storage operations
    /// </summary>
    internal static class ETCLogHelper
    {
        /// <summary>
        /// Execute an operation with automatic logging
        /// </summary>
        public static T LogOperation<T>(
            SharePointSite site,
            string operation,
            string path,
            Func<T> action,
            Func<T, long?> getFileSize = null,
            string destinationPath = null)
        {
            // Logging is MANDATORY - cannot be disabled
            if (site.Logger == null)
            {
                throw new InvalidOperationException(
                    "Logger is required for compliance. Use SharePointSite.FromConfig() to ensure proper initialization.");
            }

            var sw = Stopwatch.StartNew();
            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Info,  // Default to Info, will change if error
                UserId = site.UserId ?? Environment.UserName,
                UserName = site.UserName ?? Environment.UserName,
                Operation = operation,
                SiteName = site.Name,
                Path = path,
                DestinationPath = destinationPath,
                MachineName = Environment.MachineName,
                ApplicationName = site.ApplicationName
            };

            try
            {
                T result = action();
                sw.Stop();

                entry.DurationMs = sw.ElapsedMilliseconds;
                entry.Success = true;
                
                // Check for slow operations (Warning level)
                if (sw.ElapsedMilliseconds > 30000)  // > 30 seconds
                {
                    entry.Level = LogLevel.Warning;
                }
                else
                {
                    entry.Level = LogLevel.Info;
                }
                
                if (getFileSize != null)
                {
                    entry.FileSizeBytes = getFileSize(result);
                }

                Console.WriteLine($"[ETCLogHelper] Calling Logger.Log() for {entry.Operation}");
                site.Logger.Log(entry);
                Console.WriteLine($"[ETCLogHelper] Logger.Log() returned");
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();

                entry.DurationMs = sw.ElapsedMilliseconds;
                entry.Success = false;
                entry.Level = LogLevel.Error;  // Failed operations are Error level
                entry.ErrorMessage = ex.Message;

                site.Logger.Log(entry);
                throw;
            }
        }

        /// <summary>
        /// Execute an operation with automatic logging (void return)
        /// </summary>
        public static void LogOperation(
            SharePointSite site,
            string operation,
            string path,
            Action action,
            long? fileSize = null,
            string destinationPath = null)
        {
            LogOperation<object>(
                site,
                operation,
                path,
                () => {
                    action();
                    return null;
                },
                _ => fileSize,
                destinationPath
            );
        }
    }
}

