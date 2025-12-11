using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ETCStorageHelper.Caching;
using ETCStorageHelper.SharePoint;

namespace ETCStorageHelper
{
    /// <summary>
    /// Async file operations with caching support for reduced latency
    /// </summary>
    public static class ETCFileAsync
    {
        private static readonly Dictionary<string, LocalFileCache> _caches = new Dictionary<string, LocalFileCache>();
        private static readonly Dictionary<string, BackgroundUploadQueue> _uploadQueues = new Dictionary<string, BackgroundUploadQueue>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Write file bytes asynchronously (returns immediately, uploads in background)
        /// MUCH FASTER: Returns in ~1ms instead of waiting minutes for large files
        /// </summary>
        /// <param name="path">Virtual file path (e.g., "ClientA/Job001/Reports/report.pdf")</param>
        /// <param name="data">File contents as byte array</param>
        /// <param name="site">Target SharePoint site</param>
        /// <param name="onSuccess">Optional callback when upload completes</param>
        /// <param name="onError">Optional callback if upload fails</param>
        /// <returns>Upload handle to track status</returns>
        public static UploadHandle WriteAllBytesAsync(
            string path,
            byte[] data,
            SharePointSite site,
            Action<string> onSuccess = null,
            Action<string, Exception> onError = null)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (site == null)
                throw new ArgumentNullException(nameof(site));

            site.Validate();

            var queue = GetOrCreateUploadQueue(site);

            // Queue upload and return immediately
            var handle = queue.QueueUpload(
                path,
                data,
                async (p, d) =>
                {
                    // This runs in background thread
                    var client = ETCFile.GetOrCreateClient(site);
                    await client.UploadFileAsync(p, d);
                },
                onSuccess,
                onError
            );

            System.Diagnostics.Debug.WriteLine(
                $"[ETCFileAsync] Queued upload: {path} ({data.Length / 1024.0 / 1024.0:F2} MB) - " +
                $"Upload ID: {handle.UploadId}");

            return handle;
        }

        /// <summary>
        /// Write text file asynchronously (returns immediately)
        /// </summary>
        public static UploadHandle WriteAllTextAsync(
            string path,
            string text,
            SharePointSite site,
            Action<string> onSuccess = null,
            Action<string, Exception> onError = null)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            return WriteAllBytesAsync(path, data, site, onSuccess, onError);
        }

        /// <summary>
        /// Read file bytes with caching (checks local cache first)
        /// MUCH FASTER: ~1ms for cached files vs seconds/minutes for download
        /// </summary>
        /// <param name="path">Virtual file path</param>
        /// <param name="site">Source SharePoint site</param>
        /// <param name="bypassCache">If true, always downloads from SharePoint</param>
        /// <returns>File contents</returns>
        public static byte[] ReadAllBytesCached(
            string path,
            SharePointSite site,
            bool bypassCache = false)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (site == null)
                throw new ArgumentNullException(nameof(site));

            site.Validate();

            var cache = GetOrCreateCache(site);

            // Check cache first (unless bypass requested)
            if (!bypassCache && cache.TryGetCached(path, site.Name, out byte[] cachedData))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ETCFileAsync] Cache hit: {path} ({cachedData.Length / 1024.0 / 1024.0:F2} MB) - " +
                    $"Saved download time!");
                return cachedData;
            }

            // Cache miss - download from SharePoint
            System.Diagnostics.Debug.WriteLine($"[ETCFileAsync] Cache miss: {path} - Downloading...");
            
            byte[] data = ETCFile.ReadAllBytes(path, site);

            // Store in cache for next time
            cache.Store(path, site.Name, data);

            return data;
        }

        /// <summary>
        /// Read text file with caching
        /// </summary>
        public static string ReadAllTextCached(string path, SharePointSite site, bool bypassCache = false)
        {
            byte[] data = ReadAllBytesCached(path, site, bypassCache);
            return Encoding.UTF8.GetString(data);
        }

        /// <summary>
        /// Delete a file asynchronously (returns immediately, deletion happens in background)
        /// </summary>
        /// <param name="path">Virtual file path</param>
        /// <param name="site">Target SharePoint site</param>
        /// <param name="onSuccess">Optional callback when deletion completes</param>
        /// <param name="onError">Optional callback if deletion fails</param>
        /// <returns>Upload handle to track status</returns>
        public static UploadHandle DeleteAsync(
            string path,
            SharePointSite site,
            Action<string> onSuccess = null,
            Action<string, Exception> onError = null)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (site == null)
                throw new ArgumentNullException(nameof(site));

            site.Validate();

            var queue = GetOrCreateUploadQueue(site);

            // Queue deletion and return immediately
            var handle = queue.QueueUpload(
                path,
                null,  // No data for deletion
                async (p, d) =>
                {
                    // This runs in background thread
                    var client = ETCFile.GetOrCreateClient(site);
                    await client.DeleteFileAsync(p);
                },
                onSuccess,
                onError
            );

            System.Diagnostics.Debug.WriteLine(
                $"[ETCFileAsync] Queued deletion: {path} - Operation ID: {handle.UploadId}");

            return handle;
        }

        /// <summary>
        /// Check if file exists (async)
        /// </summary>
        /// <param name="path">Virtual file path</param>
        /// <param name="site">Target SharePoint site</param>
        /// <returns>Task that returns true if file exists</returns>
        public static async Task<bool> ExistsAsync(string path, SharePointSite site)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (site == null)
                throw new ArgumentNullException(nameof(site));

            site.Validate();

            var client = ETCFile.GetOrCreateClient(site);
            return await client.FileExistsAsync(path);
        }

        /// <summary>
        /// Copy a file asynchronously (returns immediately, copy happens in background)
        /// </summary>
        /// <param name="sourceFileName">Source file path</param>
        /// <param name="destFileName">Destination file path</param>
        /// <param name="site">SharePoint site (same site for source and destination)</param>
        /// <param name="onSuccess">Optional callback when copy completes</param>
        /// <param name="onError">Optional callback if copy fails</param>
        /// <returns>Upload handle to track status</returns>
        public static UploadHandle CopyAsync(
            string sourceFileName,
            string destFileName,
            SharePointSite site,
            Action<string> onSuccess = null,
            Action<string, Exception> onError = null)
        {
            if (sourceFileName == null)
                throw new ArgumentNullException(nameof(sourceFileName));
            if (destFileName == null)
                throw new ArgumentNullException(nameof(destFileName));
            if (site == null)
                throw new ArgumentNullException(nameof(site));

            site.Validate();

            var queue = GetOrCreateUploadQueue(site);

            // Queue copy operation and return immediately
            var handle = queue.QueueUpload(
                destFileName,
                null,  // Data will be read during copy
                async (p, d) =>
                {
                    // This runs in background thread
                    var data = ETCFile.ReadAllBytes(sourceFileName, site);
                    var client = ETCFile.GetOrCreateClient(site);
                    await client.UploadFileAsync(destFileName, data);
                },
                onSuccess,
                onError
            );

            System.Diagnostics.Debug.WriteLine(
                $"[ETCFileAsync] Queued copy: {sourceFileName} -> {destFileName} - Operation ID: {handle.UploadId}");

            return handle;
        }

        /// <summary>
        /// Copy a file between different sites asynchronously
        /// </summary>
        /// <param name="sourceFileName">Source file path</param>
        /// <param name="sourceSite">Source SharePoint site</param>
        /// <param name="destFileName">Destination file path</param>
        /// <param name="destSite">Destination SharePoint site</param>
        /// <param name="onSuccess">Optional callback when copy completes</param>
        /// <param name="onError">Optional callback if copy fails</param>
        /// <returns>Upload handle to track status</returns>
        public static UploadHandle CopyAsync(
            string sourceFileName,
            SharePointSite sourceSite,
            string destFileName,
            SharePointSite destSite,
            Action<string> onSuccess = null,
            Action<string, Exception> onError = null)
        {
            if (sourceFileName == null)
                throw new ArgumentNullException(nameof(sourceFileName));
            if (sourceSite == null)
                throw new ArgumentNullException(nameof(sourceSite));
            if (destFileName == null)
                throw new ArgumentNullException(nameof(destFileName));
            if (destSite == null)
                throw new ArgumentNullException(nameof(destSite));

            sourceSite.Validate();
            destSite.Validate();

            var queue = GetOrCreateUploadQueue(destSite);

            // Queue copy operation and return immediately
            var handle = queue.QueueUpload(
                destFileName,
                null,  // Data will be read during copy
                async (p, d) =>
                {
                    // This runs in background thread
                    var data = ETCFile.ReadAllBytes(sourceFileName, sourceSite);
                    var client = ETCFile.GetOrCreateClient(destSite);
                    await client.UploadFileAsync(destFileName, data);
                },
                onSuccess,
                onError
            );

            System.Diagnostics.Debug.WriteLine(
                $"[ETCFileAsync] Queued cross-site copy: {sourceFileName} ({sourceSite.Name}) -> {destFileName} ({destSite.Name}) - Operation ID: {handle.UploadId}");

            return handle;
        }

        /// <summary>
        /// Get the SharePoint web URL for a file (async)
        /// </summary>
        /// <param name="path">Relative path to file</param>
        /// <param name="site">SharePoint site</param>
        /// <returns>Task that returns the full SharePoint web URL</returns>
        public static async Task<string> GetFileUrlAsync(string path, SharePointSite site)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (site == null)
                throw new ArgumentNullException(nameof(site));

            site.Validate();

            var client = ETCFile.GetOrCreateClient(site);
            return await client.GetFileUrlAsync(path);
        }

        /// <summary>
        /// Wait for all pending uploads for a specific site to complete
        /// </summary>
        public static void WaitForUploads(SharePointSite site, int timeoutSeconds = 300)
        {
            var queue = GetOrCreateUploadQueue(site);
            queue.WaitForAll(timeoutSeconds);
        }

        /// <summary>
        /// Get upload queue statistics for a site
        /// </summary>
        public static QueueStats GetUploadStats(SharePointSite site)
        {
            var queue = GetOrCreateUploadQueue(site);
            return queue.GetStats();
        }

        /// <summary>
        /// Get cache statistics for a site
        /// </summary>
        public static CacheStats GetCacheStats(SharePointSite site)
        {
            var cache = GetOrCreateCache(site);
            return cache.GetStats();
        }

        /// <summary>
        /// Clear cache for a specific site
        /// </summary>
        public static void ClearCache(SharePointSite site)
        {
            var cache = GetOrCreateCache(site);
            cache.Clear();
            System.Diagnostics.Debug.WriteLine($"[ETCFileAsync] Cleared cache for site: {site.Name}");
        }

        /// <summary>
        /// Clear all caches
        /// </summary>
        public static void ClearAllCaches()
        {
            lock (_lock)
            {
                foreach (var cache in _caches.Values)
                {
                    cache.Clear();
                }
            }
            System.Diagnostics.Debug.WriteLine("[ETCFileAsync] Cleared all caches");
        }

        #region Private Methods

        private static LocalFileCache GetOrCreateCache(SharePointSite site)
        {
            lock (_lock)
            {
                if (!_caches.ContainsKey(site.Name))
                {
                    _caches[site.Name] = new LocalFileCache(site.CacheConfig);
                    System.Diagnostics.Debug.WriteLine(
                        $"[ETCFileAsync] Created cache for site: {site.Name} " +
                        $"(Max: {site.CacheConfig.MaxCacheSizeMB} MB, " +
                        $"Expiration: {site.CacheConfig.CacheExpirationHours} hours)");
                }
                return _caches[site.Name];
            }
        }

        private static BackgroundUploadQueue GetOrCreateUploadQueue(SharePointSite site)
        {
            lock (_lock)
            {
                if (!_uploadQueues.ContainsKey(site.Name))
                {
                    _uploadQueues[site.Name] = new BackgroundUploadQueue(site.CacheConfig);
                    System.Diagnostics.Debug.WriteLine(
                        $"[ETCFileAsync] Created upload queue for site: {site.Name} " +
                        $"(Workers: {site.CacheConfig.MaxConcurrentUploads})");
                }
                return _uploadQueues[site.Name];
            }
        }

        #endregion
    }
}

