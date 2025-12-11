using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ETCStorageHelper.Caching
{
    /// <summary>
    /// Local file cache for downloaded SharePoint files
    /// Reduces latency by caching frequently accessed files locally
    /// </summary>
    public class LocalFileCache
    {
        private readonly CacheConfig _config;
        private readonly object _lock = new object();

        public LocalFileCache(CacheConfig config)
        {
            _config = config;
            
            if (_config.EnableCache)
            {
                EnsureCacheDirectory();
            }
        }

        /// <summary>
        /// Get file from cache if available and not expired
        /// </summary>
        public bool TryGetCached(string path, string siteId, out byte[] data)
        {
            data = null;

            if (!_config.EnableCache)
                return false;

            try
            {
                string cacheKey = GenerateCacheKey(path, siteId);
                string cachePath = GetCachePath(cacheKey);
                string metaPath = GetMetaPath(cacheKey);

                if (!File.Exists(cachePath) || !File.Exists(metaPath))
                    return false;

                // Check expiration
                var meta = ReadMetadata(metaPath);
                if (meta.IsExpired(_config.CacheExpirationHours))
                {
                    DeleteCachedFile(cacheKey);
                    return false;
                }

                // Read from cache
                lock (_lock)
                {
                    data = File.ReadAllBytes(cachePath);
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[Cache Hit] {path} ({data.Length / 1024.0 / 1024.0:F2} MB) - " +
                    $"Age: {(DateTime.UtcNow - meta.CachedTime).TotalMinutes:F1} minutes");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cache Error] Failed to read cache: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Store file in cache
        /// </summary>
        public void Store(string path, string siteId, byte[] data)
        {
            if (!_config.EnableCache)
                return;

            try
            {
                // Check if adding this file would exceed cache size limit
                long cacheSizeBytes = _config.MaxCacheSizeMB * 1024 * 1024;
                if (data.Length > cacheSizeBytes)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Cache Skip] File too large: {data.Length / 1024.0 / 1024.0:F2} MB " +
                        $"(max: {_config.MaxCacheSizeMB} MB)");
                    return;
                }

                string cacheKey = GenerateCacheKey(path, siteId);
                string cachePath = GetCachePath(cacheKey);
                string metaPath = GetMetaPath(cacheKey);

                // Ensure cache directory exists
                EnsureCacheDirectory();

                // Evict old files if needed
                EvictIfNeeded(data.Length);

                // Write file
                lock (_lock)
                {
                    File.WriteAllBytes(cachePath, data);
                    
                    var meta = new CacheMetadata
                    {
                        OriginalPath = path,
                        SiteId = siteId,
                        CachedTime = DateTime.UtcNow,
                        FileSize = data.Length
                    };
                    
                    File.WriteAllText(metaPath, meta.ToJson());
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[Cache Store] {path} ({data.Length / 1024.0 / 1024.0:F2} MB)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cache Error] Failed to store in cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear entire cache
        /// </summary>
        public void Clear()
        {
            if (!_config.EnableCache)
                return;

            try
            {
                lock (_lock)
                {
                    if (Directory.Exists(_config.CacheDirectory))
                    {
                        Directory.Delete(_config.CacheDirectory, recursive: true);
                        EnsureCacheDirectory();
                    }
                }

                System.Diagnostics.Debug.WriteLine("[Cache] Cleared all cached files");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cache Error] Failed to clear cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStats GetStats()
        {
            var stats = new CacheStats();

            if (!_config.EnableCache || !Directory.Exists(_config.CacheDirectory))
                return stats;

            try
            {
                lock (_lock)
                {
                    var files = Directory.GetFiles(_config.CacheDirectory, "*.cache");
                    stats.FileCount = files.Length;
                    stats.TotalSizeBytes = files.Sum(f => new FileInfo(f).Length);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cache Error] Failed to get stats: {ex.Message}");
            }

            return stats;
        }

        #region Private Methods

        private void EnsureCacheDirectory()
        {
            if (!Directory.Exists(_config.CacheDirectory))
            {
                Directory.CreateDirectory(_config.CacheDirectory);
            }
        }

        private string GenerateCacheKey(string path, string siteId)
        {
            string combined = $"{siteId}:{path}".ToLowerInvariant();
            
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private string GetCachePath(string cacheKey)
        {
            return Path.Combine(_config.CacheDirectory, $"{cacheKey}.cache");
        }

        private string GetMetaPath(string cacheKey)
        {
            return Path.Combine(_config.CacheDirectory, $"{cacheKey}.meta");
        }

        private CacheMetadata ReadMetadata(string metaPath)
        {
            string json = File.ReadAllText(metaPath);
            return CacheMetadata.FromJson(json);
        }

        private void DeleteCachedFile(string cacheKey)
        {
            try
            {
                lock (_lock)
                {
                    string cachePath = GetCachePath(cacheKey);
                    string metaPath = GetMetaPath(cacheKey);

                    if (File.Exists(cachePath))
                        File.Delete(cachePath);
                    
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cache Error] Failed to delete cached file: {ex.Message}");
            }
        }

        private void EvictIfNeeded(long newFileSize)
        {
            try
            {
                lock (_lock)
                {
                    long maxCacheBytes = _config.MaxCacheSizeMB * 1024 * 1024;
                    long currentSize = GetCurrentCacheSize();

                    if (currentSize + newFileSize <= maxCacheBytes)
                        return;

                    // Get all cached files sorted by last access time
                    var files = Directory.GetFiles(_config.CacheDirectory, "*.cache")
                        .Select(f => new FileInfo(f))
                        .OrderBy(f => f.LastAccessTime)
                        .ToList();

                    // Evict oldest files until we have enough space
                    long targetSize = maxCacheBytes - newFileSize;
                    long freedSpace = 0;

                    foreach (var file in files)
                    {
                        if (currentSize - freedSpace <= targetSize)
                            break;

                        string cacheKey = Path.GetFileNameWithoutExtension(file.Name);
                        DeleteCachedFile(cacheKey);
                        freedSpace += file.Length;

                        System.Diagnostics.Debug.WriteLine(
                            $"[Cache Evict] {file.Name} ({file.Length / 1024.0 / 1024.0:F2} MB)");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cache Error] Failed to evict files: {ex.Message}");
            }
        }

        private long GetCurrentCacheSize()
        {
            if (!Directory.Exists(_config.CacheDirectory))
                return 0;

            return Directory.GetFiles(_config.CacheDirectory, "*.cache")
                .Sum(f => new FileInfo(f).Length);
        }

        #endregion
    }

    /// <summary>
    /// Cache metadata for a cached file
    /// </summary>
    internal class CacheMetadata
    {
        public string OriginalPath { get; set; }
        public string SiteId { get; set; }
        public DateTime CachedTime { get; set; }
        public long FileSize { get; set; }

        public bool IsExpired(int expirationHours)
        {
            return (DateTime.UtcNow - CachedTime).TotalHours > expirationHours;
        }

        public string ToJson()
        {
            return $"{{\"path\":\"{OriginalPath}\",\"site\":\"{SiteId}\"," +
                   $"\"time\":\"{CachedTime:O}\",\"size\":{FileSize}}}";
        }

        public static CacheMetadata FromJson(string json)
        {
            // Simple JSON parsing (avoiding external dependencies)
            var meta = new CacheMetadata();
            
            json = json.Trim('{', '}');
            var parts = json.Split(',');
            
            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv.Length != 2) continue;

                string key = kv[0].Trim().Trim('"');
                string value = kv[1].Trim().Trim('"');

                switch (key)
                {
                    case "path":
                        meta.OriginalPath = value;
                        break;
                    case "site":
                        meta.SiteId = value;
                        break;
                    case "time":
                        meta.CachedTime = DateTime.Parse(value);
                        break;
                    case "size":
                        meta.FileSize = long.Parse(value);
                        break;
                }
            }

            return meta;
        }
    }

    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStats
    {
        public int FileCount { get; set; }
        public long TotalSizeBytes { get; set; }

        public double TotalSizeMB => TotalSizeBytes / 1024.0 / 1024.0;

        public override string ToString()
        {
            return $"{FileCount} files, {TotalSizeMB:F2} MB";
        }
    }
}

