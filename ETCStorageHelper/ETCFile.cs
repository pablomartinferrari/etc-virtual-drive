using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETCStorageHelper.Configuration;
using ETCStorageHelper.SharePoint;
using ETCStorageHelper.Logging;

namespace ETCStorageHelper
{
    /// <summary>
    /// Provides static methods for file operations on SharePoint,
    /// similar to System.IO.File but routes to SharePoint storage.
    /// Consumer applications specify which SharePoint site to use for each operation.
    /// </summary>
    public static class ETCFile
    {
        private static readonly Dictionary<string, SharePointClient> _clients = new Dictionary<string, SharePointClient>();
        private static readonly object _clientLock = new object();

        /// <summary>
        /// Register a SharePoint site for use.
        /// Call this once per site at application startup.
        /// </summary>
        /// <param name="site">SharePoint site configuration</param>
        public static void RegisterSite(SharePointSite site)
        {
            if (site == null)
                throw new ArgumentNullException(nameof(site));

            site.Validate();

            lock (_clientLock)
            {
                // Convert SharePointSite to ETCStorageConfig
                var config = new ETCStorageConfig
                {
                    TenantId = site.TenantId,
                    ClientId = site.ClientId,
                    ClientSecret = site.ClientSecret,
                    SiteUrl = site.SiteUrl,
                    LibraryName = site.LibraryName,
                    TimeoutSeconds = site.TimeoutSeconds,
                    RetryAttempts = site.RetryAttempts,
                    Environment = site.Environment
                };

                _clients[site.Name] = new SharePointClient(config);
            }
        }

        /// <summary>
        /// Initialize with a default site from app.config.
        /// Registers a site named "Default" using ETCStorage.* config keys.
        /// </summary>
        [Obsolete("Use RegisterSite() instead to explicitly specify sites. This method will be removed in v2.0")]
        public static void Initialize()
        {
            var config = ETCStorageConfig.LoadFromAppSettings();
            var site = new SharePointSite(
                "Default",
                config.TenantId,
                config.ClientId,
                config.ClientSecret,
                config.SiteUrl,
                config.LibraryName
            );
            RegisterSite(site);
        }

        internal static SharePointClient GetClientInternal(SharePointSite site)
        {
            if (site == null)
                throw new ArgumentNullException(nameof(site), "SharePoint site must be specified");

            lock (_clientLock)
            {
                // Check if client already exists for this site
                if (_clients.ContainsKey(site.Name))
                {
                    return _clients[site.Name];
                }

                // Auto-register if not already registered
                RegisterSite(site);
                return _clients[site.Name];
            }
        }

        private static SharePointClient GetClient(SharePointSite site)
        {
            return GetClientInternal(site);
        }

        /// <summary>
        /// Internal method to get or create client (used by ETCFileAsync)
        /// </summary>
        internal static SharePointClient GetOrCreateClient(SharePointSite site)
        {
            return GetClientInternal(site);
        }

        /// <summary>
        /// Write byte array to SharePoint file.
        /// Automatically creates parent directories if they don't exist.
        /// All operations are logged with user information for audit trail.
        /// </summary>
        /// <param name="path">Relative path (e.g., "ClientA/Job001/Reports/report.pdf")</param>
        /// <param name="data">File content as byte array</param>
        /// <param name="site">SharePoint site to write to</param>
        public static void WriteAllBytes(string path, byte[] data, SharePointSite site)
        {
            ETCLogHelper.LogOperation(
                site,
                "WriteFile",
                path,
                () => {
                    var normalizedPath = NormalizePath(path);
                    var client = GetClient(site);
                    
                    // Automatically create parent directories if they don't exist
                    var directory = GetDirectoryFromPath(normalizedPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        RunAsync(() => client.CreateFolderAsync(directory));
                    }
                    
                    // Upload the file
                    RunAsync(() => client.UploadFileAsync(normalizedPath, data));
                },
                fileSize: data.Length
            );
        }

        /// <summary>
        /// Read file from SharePoint as byte array.
        /// All operations are logged with user information for audit trail.
        /// </summary>
        /// <param name="path">Relative path to file</param>
        /// <param name="site">SharePoint site to read from</param>
        /// <returns>File content as byte array</returns>
        public static byte[] ReadAllBytes(string path, SharePointSite site)
        {
            return ETCLogHelper.LogOperation(
                site,
                "ReadFile",
                path,
                () => {
                    var client = GetClient(site);
                    return RunAsync(() => client.DownloadFileAsync(NormalizePath(path)));
                },
                getFileSize: data => data.Length
            );
        }

        /// <summary>
        /// Write string content to file (UTF-8 encoding).
        /// Automatically creates parent directories if they don't exist.
        /// </summary>
        public static void WriteAllText(string path, string contents, SharePointSite site)
        {
            var data = System.Text.Encoding.UTF8.GetBytes(contents);
            WriteAllBytes(path, data, site);
        }

        /// <summary>
        /// Read file content as string (UTF-8 encoding)
        /// </summary>
        public static string ReadAllText(string path, SharePointSite site)
        {
            var data = ReadAllBytes(path, site);
            return System.Text.Encoding.UTF8.GetString(data);
        }

        /// <summary>
        /// Check if file exists
        /// </summary>
        public static bool Exists(string path, SharePointSite site)
        {
            var client = GetClient(site);
            return RunAsync(() => client.FileExistsAsync(NormalizePath(path)));
        }

        /// <summary>
        /// Delete a file.
        /// All operations are logged with user information for audit trail.
        /// </summary>
        public static void Delete(string path, SharePointSite site)
        {
            ETCLogHelper.LogOperation(
                site,
                "DeleteFile",
                path,
                () => {
                    var client = GetClient(site);
                    RunAsync(() => client.DeleteFileAsync(NormalizePath(path)));
                }
            );
        }

        /// <summary>
        /// Copy a file within the same site (download then upload).
        /// All operations are logged with user information for audit trail.
        /// </summary>
        public static void Copy(string sourceFileName, string destFileName, SharePointSite site)
        {
            ETCLogHelper.LogOperation(
                site,
                "CopyFile",
                sourceFileName,
                () => {
                    var data = ReadAllBytes(sourceFileName, site);
                    WriteAllBytes(destFileName, data, site);
                },
                fileSize: null,
                destinationPath: destFileName
            );
        }

        /// <summary>
        /// Copy a file between different sites
        /// </summary>
        public static void Copy(string sourceFileName, SharePointSite sourceSite, 
                               string destFileName, SharePointSite destSite)
        {
            var data = ReadAllBytes(sourceFileName, sourceSite);
            WriteAllBytes(destFileName, data, destSite);
        }

        /// <summary>
        /// Get the SharePoint web URL for a file.
        /// Returns a URL that can be opened in a browser or shared with users.
        /// </summary>
        /// <param name="path">Relative path to file</param>
        /// <param name="site">SharePoint site</param>
        /// <returns>Full SharePoint web URL (e.g., https://tenant.sharepoint.com/...)</returns>
        public static string GetFileUrl(string path, SharePointSite site)
        {
            var client = GetClient(site);
            return RunAsync(() => client.GetFileUrlAsync(NormalizePath(path)));
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            // Convert backslashes to forward slashes for SharePoint
            return path.Replace('\\', '/').Trim('/');
        }

        private static string GetDirectoryFromPath(string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath))
                return null;

            var lastSlash = normalizedPath.LastIndexOf('/');
            if (lastSlash < 0)
                return null; // No directory, file is at root

            return normalizedPath.Substring(0, lastSlash);
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
    }
}

