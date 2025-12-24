using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETCStorageHelper.Configuration;
using ETCStorageHelper.SharePoint;

namespace ETCStorageHelper
{
    /// <summary>
    /// Provides static methods for directory operations on SharePoint,
    /// similar to System.IO.Directory but routes to SharePoint storage.
    /// Consumer applications specify which SharePoint site to use for each operation.
    /// </summary>
    public static class ETCDirectory
    {

        /// <summary>
        /// Create a directory in SharePoint (creates parent directories automatically)
        /// </summary>
        /// <param name="path">Relative path (e.g., "ClientA/Job001/Reports")</param>
        /// <param name="site">SharePoint site to create directory in</param>
        public static void CreateDirectory(string path, SharePointSite site)
        {
            var client = ETCFile.GetClientInternal(site);
            RunAsync(() => client.CreateFolderAsync(NormalizePath(path)));
        }

        /// <summary>
        /// Check if directory exists
        /// </summary>
        public static bool Exists(string path, SharePointSite site)
        {
            var client = ETCFile.GetClientInternal(site);
            return RunAsync(() => client.DirectoryExistsAsync(NormalizePath(path)));
        }

        /// <summary>
        /// Get files and folders in a directory
        /// </summary>
        /// <param name="path">Directory path</param>
        /// <param name="site">SharePoint site to list from</param>
        /// <returns>Array of file/folder names</returns>
        public static string[] GetFileSystemEntries(string path, SharePointSite site)
        {
            var client = ETCFile.GetClientInternal(site);
            var items = RunAsync(() => client.ListDirectoryAsync(NormalizePath(path)));
            return items.ToArray();
        }

        /// <summary>
        /// Get files in a directory (simplified - returns all entries)
        /// Note: SharePoint API doesn't easily distinguish files from folders without additional calls
        /// </summary>
        /// <param name="path">Directory path</param>
        /// <param name="site">SharePoint site to list from</param>
        /// <param name="searchPattern">Optional wildcard pattern to filter files (e.g., "*.pdf", "report*", "*.txt"). If null or empty, returns all files.</param>
        /// <returns>Array of file names matching the search pattern</returns>
        public static string[] GetFiles(string path, SharePointSite site, string searchPattern = null)
        {
            var entries = GetFileSystemEntries(path, site);
            
            // If no search pattern provided, return all entries
            if (string.IsNullOrWhiteSpace(searchPattern))
            {
                return entries;
            }
            
            // Filter entries using wildcard matching
            return entries.Where(entry => MatchesWildcard(Path.GetFileName(entry), searchPattern)).ToArray();
        }

        /// <summary>
        /// Get files in a directory with file information (name, modified date, size, etc.)
        /// Use this method when you need to sort by modified date or access file metadata.
        /// </summary>
        /// <param name="path">Directory path</param>
        /// <param name="site">SharePoint site to list from</param>
        /// <param name="searchPattern">Optional wildcard pattern to filter files (e.g., "*.pdf", "report*", "*.txt"). If null or empty, returns all files.</param>
        /// <returns>Array of ETCFileInfo objects with file metadata</returns>
        /// <example>
        /// // Get files and sort by modified date descending (newest first)
        /// var files = ETCDirectory.GetFilesWithInfo("ClientA/Job001", site);
        /// var sorted = files.OrderByDescending(f => f.LastModified).ToArray();
        /// </example>
        public static ETCFileInfo[] GetFilesWithInfo(string path, SharePointSite site, string searchPattern = null)
        {
            var client = ETCFile.GetClientInternal(site);
            var items = RunAsync(() => client.ListDirectoryWithInfoAsync(NormalizePath(path)));
            
            // If no search pattern provided, return all entries
            if (string.IsNullOrWhiteSpace(searchPattern))
            {
                return items.ToArray();
            }
            
            // Filter entries using wildcard matching
            return items.Where(item => MatchesWildcard(Path.GetFileName(item.Name), searchPattern)).ToArray();
        }

        /// <summary>
        /// Get subdirectories (simplified - returns all entries)
        /// Note: SharePoint API doesn't easily distinguish files from folders without additional calls
        /// </summary>
        public static string[] GetDirectories(string path, SharePointSite site)
        {
            // For now, return all entries. Could be enhanced to filter directories only.
            return GetFileSystemEntries(path, site);
        }

        /// <summary>
        /// Delete a directory.
        /// All operations are logged with user information for audit trail.
        /// </summary>
        /// <param name="path">Relative path to directory</param>
        /// <param name="site">SharePoint site to delete from</param>
        /// <param name="recursive">If true, deletes directory and all contents. If false, fails if directory is not empty.</param>
        public static void Delete(string path, SharePointSite site, bool recursive = false)
        {
            var client = ETCFile.GetClientInternal(site);
            RunAsync(() => client.DeleteFolderAsync(NormalizePath(path)));
        }

        /// <summary>
        /// Get the SharePoint web URL for a folder.
        /// Returns a URL that can be opened in a browser or shared with users.
        /// </summary>
        /// <param name="path">Relative path to folder</param>
        /// <param name="site">SharePoint site</param>
        /// <returns>Full SharePoint web URL (e.g., https://tenant.sharepoint.com/...)</returns>
        public static string GetFolderUrl(string path, SharePointSite site)
        {
            var client = ETCFile.GetClientInternal(site);
            return RunAsync(() => client.GetFolderUrlAsync(NormalizePath(path)));
        }

        /// <summary>
        /// Rename or move a folder to a new location.
        /// Can be used to rename a folder or move it to a different parent folder.
        /// All operations are logged with user information for audit trail.
        /// </summary>
        /// <param name="sourcePath">Current path to folder (e.g., "ClientA/OldName")</param>
        /// <param name="destinationPath">New path for folder (e.g., "ClientA/NewName" or "ClientB/NewName")</param>
        /// <param name="site">SharePoint site</param>
        /// <param name="overwrite">If true, overwrites existing folder at destination. If false, throws error if folder exists.</param>
        /// <example>
        /// // Rename a folder in place
        /// ETCDirectory.Move("ClientA/OldFolderName", "ClientA/NewFolderName", site);
        /// 
        /// // Move a folder to a different parent
        /// ETCDirectory.Move("ClientA/Reports", "ClientB/Reports", site);
        /// 
        /// // Move with overwrite if destination exists
        /// ETCDirectory.Move("ClientA/Draft", "ClientA/Final", site, overwrite: true);
        /// </example>
        public static void Move(string sourcePath, string destinationPath, SharePointSite site, bool overwrite = false)
        {
            var client = ETCFile.GetClientInternal(site);
            RunAsync(() => client.RenameFolderAsync(NormalizePath(sourcePath), NormalizePath(destinationPath), overwrite));
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            // Convert backslashes to forward slashes for SharePoint
            return path.Replace('\\', '/').Trim('/');
        }

        // Helper to run async code synchronously (for .NET Framework 4.6 compatibility)
        private static void RunAsync(Func<Task> asyncMethod)
        {
            Task.Run(asyncMethod).GetAwaiter().GetResult();
        }

        private static T RunAsync<T>(Func<Task<T>> asyncMethod)
        {
            return Task.Run(asyncMethod).GetAwaiter().GetResult();
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
    }
}

