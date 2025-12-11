using System;
using System.IO;

namespace ETCStorageHelper.Caching
{
    /// <summary>
    /// Configuration for local file caching
    /// </summary>
    public class CacheConfig
    {
        /// <summary>
        /// Enable local file caching for downloads
        /// </summary>
        public bool EnableCache { get; set; } = true;

        /// <summary>
        /// Enable background uploads (async operations)
        /// </summary>
        public bool EnableBackgroundUpload { get; set; } = true;

        /// <summary>
        /// Cache directory path (default: %TEMP%\ETCStorageCache)
        /// </summary>
        public string CacheDirectory { get; set; } = 
            Path.Combine(Path.GetTempPath(), "ETCStorageCache");

        /// <summary>
        /// Maximum cache size in MB (default: 1000 MB = 1 GB)
        /// </summary>
        public long MaxCacheSizeMB { get; set; } = 1000;

        /// <summary>
        /// Cache expiration time in hours (default: 24 hours)
        /// </summary>
        public int CacheExpirationHours { get; set; } = 24;

        /// <summary>
        /// Maximum number of concurrent background uploads
        /// </summary>
        public int MaxConcurrentUploads { get; set; } = 3;

        /// <summary>
        /// Upload queue timeout in seconds (default: 30 minutes)
        /// </summary>
        public int UploadQueueTimeoutSeconds { get; set; } = 1800;
    }
}

