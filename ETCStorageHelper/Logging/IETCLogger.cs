using System;

namespace ETCStorageHelper.Logging
{
    /// <summary>
    /// Log level for categorizing operations
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Informational - normal operation (e.g., successful read/write)
        /// </summary>
        Info = 0,

        /// <summary>
        /// Warning - operation succeeded but with issues (e.g., slow performance, retry succeeded)
        /// </summary>
        Warning = 1,

        /// <summary>
        /// Error - operation failed (e.g., file not found, access denied)
        /// </summary>
        Error = 2
    }

    /// <summary>
    /// Interface for logging ETC Storage operations
    /// Implement this to send logs to your system (database, event log, SIEM, etc.)
    /// </summary>
    public interface IETCLogger
    {
        /// <summary>
        /// Log an operation
        /// </summary>
        void Log(LogEntry entry);
    }

    /// <summary>
    /// Represents a logged operation
    /// </summary>
    public class LogEntry
    {
        /// <summary>
        /// Timestamp (UTC)
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Log level (Info, Warning, Error)
        /// </summary>
        public LogLevel Level { get; set; }

        /// <summary>
        /// User ID (e.g., "pferrari009", "DOMAIN\\username")
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// User display name (e.g., "Pablo Ferrari")
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Operation type (Read, Write, Delete, Copy, CreateDirectory, etc.)
        /// </summary>
        public string Operation { get; set; }

        /// <summary>
        /// SharePoint site name (e.g., "Commercial", "GCCHigh")
        /// </summary>
        public string SiteName { get; set; }

        /// <summary>
        /// File/folder path
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Optional: Destination path (for copy operations)
        /// </summary>
        public string DestinationPath { get; set; }

        /// <summary>
        /// File size in bytes (if applicable)
        /// </summary>
        public long? FileSizeBytes { get; set; }

        /// <summary>
        /// Operation duration in milliseconds
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Whether operation succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message (if failed)
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Machine name where operation occurred
        /// </summary>
        public string MachineName { get; set; }

        /// <summary>
        /// Application name (optional)
        /// </summary>
        public string ApplicationName { get; set; }

        public double FileSizeMB => FileSizeBytes.HasValue ? FileSizeBytes.Value / 1024.0 / 1024.0 : 0;

        public override string ToString()
        {
            string levelIcon = Level == LogLevel.Info ? "✓" : Level == LogLevel.Warning ? "⚠" : "✗";
            string sizeInfo = FileSizeBytes.HasValue ? $", Size: {FileSizeMB:F2} MB" : "";
            string destInfo = !string.IsNullOrEmpty(DestinationPath) ? $" -> {DestinationPath}" : "";
            string errorInfo = !string.IsNullOrEmpty(ErrorMessage) ? $", Error: {ErrorMessage}" : "";
            
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {levelIcon} [{Level}] {UserId} ({UserName}) | " +
                   $"{Operation} | {SiteName} | {Path}{destInfo}{sizeInfo} | " +
                   $"{DurationMs}ms | {(Success ? "SUCCESS" : "FAILED")}{errorInfo}";
        }

        /// <summary>
        /// Convert to CSV format for easy export
        /// </summary>
        public string ToCsv()
        {
            return $"\"{Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{Level}\",\"{UserId}\",\"{UserName}\"," +
                   $"\"{Operation}\",\"{SiteName}\",\"{Path}\",\"{DestinationPath}\"," +
                   $"{FileSizeBytes},{DurationMs},{Success},\"{ErrorMessage}\"," +
                   $"\"{MachineName}\",\"{ApplicationName}\"";
        }

        /// <summary>
        /// CSV header
        /// </summary>
        public static string CsvHeader()
        {
            return "Timestamp,Level,UserId,UserName,Operation,SiteName,Path,DestinationPath," +
                   "FileSizeBytes,DurationMs,Success,ErrorMessage,MachineName,ApplicationName";
        }
    }
}

