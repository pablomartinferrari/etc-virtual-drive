using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETCStorageHelper.Caching;
using ETCStorageHelper.SharePoint;

namespace ETCStorageHelper
{
    /// <summary>
    /// Async directory operations for non-blocking folder management
    /// </summary>
    public static class ETCDirectoryAsync
    {
        private static readonly Dictionary<string, BackgroundUploadQueue> _uploadQueues = new Dictionary<string, BackgroundUploadQueue>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Create a directory asynchronously (returns immediately, creation happens in background)
        /// </summary>
        /// <param name="path">Relative path (e.g., "ClientA/Job001/Reports")</param>
        /// <param name="site">SharePoint site to create directory in</param>
        /// <param name="onSuccess">Optional callback when creation completes</param>
        /// <param name="onError">Optional callback if creation fails</param>
        /// <returns>Upload handle to track status</returns>
        public static UploadHandle CreateDirectoryAsync(
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

            // Queue directory creation and return immediately
            var handle = queue.QueueUpload(
                path,
                null,  // No data for directory creation
                async (p, d) =>
                {
                    // This runs in background thread
                    var client = ETCFile.GetOrCreateClient(site);
                    await client.CreateFolderAsync(p);
                },
                onSuccess,
                onError
            );

            System.Diagnostics.Debug.WriteLine(
                $"[ETCDirectoryAsync] Queued directory creation: {path} - Operation ID: {handle.UploadId}");

            return handle;
        }

        /// <summary>
        /// Check if directory exists (async)
        /// </summary>
        /// <param name="path">Relative path to directory</param>
        /// <param name="site">SharePoint site</param>
        /// <returns>Task that returns true if directory exists</returns>
        public static async Task<bool> ExistsAsync(string path, SharePointSite site)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (site == null)
                throw new ArgumentNullException(nameof(site));

            site.Validate();

            var client = ETCFile.GetOrCreateClient(site);
            return await client.DirectoryExistsAsync(path);
        }

        /// <summary>
        /// Delete a directory asynchronously (returns immediately, deletion happens in background)
        /// </summary>
        /// <param name="path">Relative path to directory</param>
        /// <param name="site">SharePoint site to delete from</param>
        /// <param name="recursive">If true, deletes directory and all contents</param>
        /// <param name="onSuccess">Optional callback when deletion completes</param>
        /// <param name="onError">Optional callback if deletion fails</param>
        /// <returns>Upload handle to track status</returns>
        public static UploadHandle DeleteAsync(
            string path,
            SharePointSite site,
            bool recursive = false,
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
                    await client.DeleteFolderAsync(p);
                },
                onSuccess,
                onError
            );

            System.Diagnostics.Debug.WriteLine(
                $"[ETCDirectoryAsync] Queued directory deletion: {path} (recursive: {recursive}) - Operation ID: {handle.UploadId}");

            return handle;
        }

        /// <summary>
        /// Get files and folders in a directory (async)
        /// </summary>
        /// <param name="path">Directory path</param>
        /// <param name="site">SharePoint site to list from</param>
        /// <returns>Task that returns array of file/folder names</returns>
        public static async Task<string[]> GetFileSystemEntriesAsync(string path, SharePointSite site)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (site == null)
                throw new ArgumentNullException(nameof(site));

            site.Validate();

            var client = ETCFile.GetOrCreateClient(site);
            var items = await client.ListDirectoryAsync(path);
            return items.ToArray();
        }

        /// <summary>
        /// Get files in a directory (async)
        /// Note: SharePoint API doesn't easily distinguish files from folders without additional calls
        /// </summary>
        /// <param name="path">Directory path</param>
        /// <param name="site">SharePoint site to list from</param>
        /// <param name="searchPattern">Optional wildcard pattern to filter files (e.g., "*.pdf", "report*", "*.txt"). If null or empty, returns all files.</param>
        /// <returns>Task that returns array of file names matching the search pattern</returns>
        public static async Task<string[]> GetFilesAsync(string path, SharePointSite site, string searchPattern = null)
        {
            var entries = await GetFileSystemEntriesAsync(path, site);
            
            // If no search pattern provided, return all entries
            if (string.IsNullOrWhiteSpace(searchPattern))
            {
                return entries;
            }
            
            // Filter entries using wildcard matching
            return entries.Where(entry => MatchesWildcard(Path.GetFileName(entry), searchPattern)).ToArray();
        }

        /// <summary>
        /// Get subdirectories (async)
        /// Note: SharePoint API doesn't easily distinguish files from folders without additional calls
        /// </summary>
        /// <param name="path">Directory path</param>
        /// <param name="site">SharePoint site to list from</param>
        /// <returns>Task that returns array of subdirectory names</returns>
        public static async Task<string[]> GetDirectoriesAsync(string path, SharePointSite site)
        {
            // For now, return all entries. Could be enhanced to filter directories only.
            return await GetFileSystemEntriesAsync(path, site);
        }

        /// <summary>
        /// Get the SharePoint web URL for a folder (async)
        /// </summary>
        /// <param name="path">Relative path to folder</param>
        /// <param name="site">SharePoint site</param>
        /// <returns>Task that returns the full SharePoint web URL</returns>
        public static async Task<string> GetFolderUrlAsync(string path, SharePointSite site)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (site == null)
                throw new ArgumentNullException(nameof(site));

            site.Validate();

            var client = ETCFile.GetOrCreateClient(site);
            return await client.GetFolderUrlAsync(path);
        }

        /// <summary>
        /// Wait for all pending operations for a specific site to complete
        /// </summary>
        public static void WaitForOperations(SharePointSite site, int timeoutSeconds = 300)
        {
            var queue = GetOrCreateUploadQueue(site);
            queue.WaitForAll(timeoutSeconds);
        }

        /// <summary>
        /// Get operation queue statistics for a site
        /// </summary>
        public static QueueStats GetOperationStats(SharePointSite site)
        {
            var queue = GetOrCreateUploadQueue(site);
            return queue.GetStats();
        }

        #region Private Methods

        private static BackgroundUploadQueue GetOrCreateUploadQueue(SharePointSite site)
        {
            lock (_lock)
            {
                if (!_uploadQueues.ContainsKey(site.Name))
                {
                    _uploadQueues[site.Name] = new BackgroundUploadQueue(site.CacheConfig);
                    System.Diagnostics.Debug.WriteLine(
                        $"[ETCDirectoryAsync] Created operation queue for site: {site.Name}");
                }
                return _uploadQueues[site.Name];
            }
        }

        /// <summary>
        /// Matches a filename against a wildcard pattern (supports * and ?)
        /// </summary>
        private static bool MatchesWildcard(string fileName, string pattern)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(pattern))
                return false;

            // Convert wildcard pattern to regex
            // Escape special regex characters, then convert * to .* and ? to .
            string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        #endregion
    }
}


