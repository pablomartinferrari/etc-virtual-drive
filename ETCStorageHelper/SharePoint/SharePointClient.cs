using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using ETCStorageHelper.Authentication;
using ETCStorageHelper.Configuration;
using ETCStorageHelper.Resilience;
using Newtonsoft.Json.Linq;

namespace ETCStorageHelper.SharePoint
{
    /// <summary>
    /// Client for SharePoint operations via Microsoft Graph API with built-in retry and resilience
    /// </summary>
    internal class SharePointClient
    {
        private readonly ETCStorageConfig _config;
        private readonly AuthenticationManager _authManager;
        private readonly RetryPolicy _retryPolicy;
        private string _siteId;
        private string _driveId;

        public SharePointClient(ETCStorageConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _authManager = new AuthenticationManager(config);
            _retryPolicy = new RetryPolicy(
                maxRetries: config.RetryAttempts,
                initialDelayMs: 1000,
                maxDelayMs: 30000
            );
        }

        /// <summary>
        /// Initialize the client by getting site and drive IDs (with retry)
        /// </summary>
        public async Task InitializeAsync()
        {
            if (!string.IsNullOrEmpty(_siteId) && !string.IsNullOrEmpty(_driveId))
                return; // Already initialized

            await _retryPolicy.ExecuteAsync(async () =>
            {
                var token = await _authManager.GetAccessTokenAsync();

                using (var client = CreateHttpClient(token))
                {
                    // Parse site URL
                    var uri = new Uri(_config.SiteUrl);
                    var hostname = uri.Host;
                    var sitePath = uri.AbsolutePath; // e.g., /sites/etc-dev-projects

                    // Get site ID
                    var siteResponse = await client.GetAsync($"https://graph.microsoft.com/v1.0/sites/{hostname}:{sitePath}");
                    if (!siteResponse.IsSuccessStatusCode)
                    {
                        var error = await siteResponse.Content.ReadAsStringAsync();
                        throw new Exception($"Failed to get site info: {siteResponse.StatusCode} - {error}");
                    }

                    var siteJson = JObject.Parse(await siteResponse.Content.ReadAsStringAsync());
                    _siteId = siteJson["id"].Value<string>();

                    // Get drive ID (document library)
                    var drivesResponse = await client.GetAsync($"https://graph.microsoft.com/v1.0/sites/{_siteId}/drives");
                    if (!drivesResponse.IsSuccessStatusCode)
                    {
                        var error = await drivesResponse.Content.ReadAsStringAsync();
                        throw new Exception($"Failed to get drives: {drivesResponse.StatusCode} - {error}");
                    }

                    var drivesJson = JObject.Parse(await drivesResponse.Content.ReadAsStringAsync());
                    var drives = drivesJson["value"].Children<JObject>();
                    var drive = drives.FirstOrDefault(d => d["name"].Value<string>() == _config.LibraryName);

                    if (drive == null)
                    {
                        // Show available libraries to help user
                        var availableLibraries = string.Join(", ", 
                            drives.Select(d => $"'{d["name"].Value<string>()}'"));
                        
                        throw new Exception(
                            $"Library '{_config.LibraryName}' not found in site.\n" +
                            $"Available libraries: {availableLibraries}\n\n" +
                            $"Fix: Update App.config to use one of the available library names (case-sensitive):\n" +
                            $"<add key=\"ETCStorage.Commercial.LibraryName\" value=\"EXACT-LIBRARY-NAME\" />");
                    }

                    _driveId = drive["id"].Value<string>();
                }
            }, "Initialize SharePoint connection");
        }

        /// <summary>
        /// Upload a file to SharePoint (with retry)
        /// Uses chunked upload for files larger than 4MB
        /// </summary>
        public async Task UploadFileAsync(string path, byte[] data)
        {
            await InitializeAsync();
            
            // Use chunked upload for files larger than 4MB
            const int chunkThreshold = 4 * 1024 * 1024; // 4 MB
            
            if (data.Length > chunkThreshold)
            {
                await UploadLargeFileAsync(path, data);
            }
            else
            {
                await UploadSmallFileAsync(path, data);
            }
        }

        /// <summary>
        /// Upload small file (under 4MB) using simple PUT
        /// </summary>
        private async Task UploadSmallFileAsync(string path, byte[] data)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                var token = await _authManager.GetAccessTokenAsync();
                using (var client = CreateHttpClient(token))
                {
                    var url = $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{path}:/content";
                    
                    var content = new ByteArrayContent(data);
                    var response = await client.PutAsync(url, content);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        throw new Exception($"Failed to upload file '{path}': {response.StatusCode} - {error}");
                    }
                }
            }, $"Upload file '{path}'");
        }

        /// <summary>
        /// Upload large file (4MB+) using chunked upload session
        /// </summary>
        private async Task UploadLargeFileAsync(string path, byte[] data)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                var token = await _authManager.GetAccessTokenAsync();
                using (var client = CreateHttpClient(token))
                {
                    // Increase timeout for large files
                    client.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds * 3);

                    // Step 1: Create upload session
                    var sessionUrl = $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{path}:/createUploadSession";
                    var sessionBody = new JObject
                    {
                        ["item"] = new JObject
                        {
                            ["@microsoft.graph.conflictBehavior"] = "replace"
                        }
                    };

                    var sessionContent = new StringContent(sessionBody.ToString(), Encoding.UTF8, "application/json");
                    var sessionResponse = await client.PostAsync(sessionUrl, sessionContent);

                    if (!sessionResponse.IsSuccessStatusCode)
                    {
                        var error = await sessionResponse.Content.ReadAsStringAsync();
                        throw new Exception($"Failed to create upload session: {sessionResponse.StatusCode} - {error}");
                    }

                    var sessionJson = JObject.Parse(await sessionResponse.Content.ReadAsStringAsync());
                    var uploadUrl = sessionJson["uploadUrl"].Value<string>();

                    // Step 2: Upload in chunks
                    const int chunkSize = 5 * 1024 * 1024; // 5 MB chunks (recommended by Microsoft)
                    int totalBytes = data.Length;
                    int offset = 0;

                    while (offset < totalBytes)
                    {
                        int currentChunkSize = Math.Min(chunkSize, totalBytes - offset);
                        byte[] chunk = new byte[currentChunkSize];
                        Array.Copy(data, offset, chunk, 0, currentChunkSize);

                        // Upload chunk
                        var chunkContent = new ByteArrayContent(chunk);
                        chunkContent.Headers.ContentLength = currentChunkSize;
                        chunkContent.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(
                            offset,
                            offset + currentChunkSize - 1,
                            totalBytes
                        );

                        var chunkResponse = await client.PutAsync(uploadUrl, chunkContent);

                        if (!chunkResponse.IsSuccessStatusCode)
                        {
                            var error = await chunkResponse.Content.ReadAsStringAsync();
                            throw new Exception($"Failed to upload chunk at offset {offset}: {chunkResponse.StatusCode} - {error}");
                        }

                        offset += currentChunkSize;

                        // Log progress
                        System.Diagnostics.Debug.WriteLine(
                            $"[Upload Progress] {path}: {offset * 100.0 / totalBytes:F1}% " +
                            $"({offset / 1024.0 / 1024.0:F1} MB / {totalBytes / 1024.0 / 1024.0:F1} MB)");
                    }
                }
            }, $"Upload large file '{path}' ({data.Length / 1024.0 / 1024.0:F1} MB)");
        }

        /// <summary>
        /// Download a file from SharePoint (with retry)
        /// </summary>
        public async Task<byte[]> DownloadFileAsync(string path)
        {
            await InitializeAsync();
            
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                var token = await _authManager.GetAccessTokenAsync();
                using (var client = CreateHttpClient(token))
                {
                    // First, get the file metadata to get the download URL
                    var url = $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{path}";
                    var response = await client.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        throw new FileNotFoundException($"File '{path}' not found: {response.StatusCode} - {error}");
                    }

                    var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                    var downloadUrl = json["@microsoft.graph.downloadUrl"].Value<string>();

                    // Download the file content
                    var downloadResponse = await client.GetAsync(downloadUrl);
                    if (!downloadResponse.IsSuccessStatusCode)
                    {
                        throw new Exception($"Failed to download file '{path}': {downloadResponse.StatusCode}");
                    }

                    return await downloadResponse.Content.ReadAsByteArrayAsync();
                }
            }, $"Download file '{path}'");
        }

        /// <summary>
        /// Check if a file exists
        /// </summary>
        public async Task<bool> FileExistsAsync(string path)
        {
            await InitializeAsync();
            
            var token = await _authManager.GetAccessTokenAsync();
            using (var client = CreateHttpClient(token))
            {
                var url = $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{path}";
                var response = await client.GetAsync(url);

                return response.IsSuccessStatusCode;
            }
        }

        /// <summary>
        /// Delete a file
        /// </summary>
        public async Task DeleteFileAsync(string path)
        {
            await InitializeAsync();
            
            var token = await _authManager.GetAccessTokenAsync();
            using (var client = CreateHttpClient(token))
            {
                var url = $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{path}";
                var response = await client.DeleteAsync(url);

                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to delete file '{path}': {response.StatusCode} - {error}");
                }
            }
        }

        /// <summary>
        /// Delete a folder (and all its contents)
        /// </summary>
        public async Task DeleteFolderAsync(string path)
        {
            await InitializeAsync();
            
            var token = await _authManager.GetAccessTokenAsync();
            using (var client = CreateHttpClient(token))
            {
                var url = $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{path}";
                var response = await client.DeleteAsync(url);

                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to delete folder '{path}': {response.StatusCode} - {error}");
                }
            }
        }

        /// <summary>
        /// Create a folder (creates parent folders automatically, with retry)
        /// </summary>
        public async Task CreateFolderAsync(string path)
        {
            await InitializeAsync();
            
            await _retryPolicy.ExecuteAsync(async () =>
            {
                var token = await _authManager.GetAccessTokenAsync();
                using (var client = CreateHttpClient(token))
                {
                    var pathParts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    var currentPath = "";

                    foreach (var part in pathParts)
                    {
                        var parentPath = currentPath;
                        currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

                        // Check if folder exists
                        var checkUrl = $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{currentPath}";
                        var checkResponse = await client.GetAsync(checkUrl);

                        if (checkResponse.IsSuccessStatusCode)
                            continue; // Folder already exists

                        // Create folder
                        var createUrl = string.IsNullOrEmpty(parentPath)
                            ? $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root/children"
                            : $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{parentPath}:/children";

                        var folderData = new JObject
                        {
                            ["name"] = part,
                            ["folder"] = new JObject(),
                            ["@microsoft.graph.conflictBehavior"] = "rename"
                        };

                        var content = new StringContent(folderData.ToString(), Encoding.UTF8, "application/json");
                        var createResponse = await client.PostAsync(createUrl, content);

                        if (!createResponse.IsSuccessStatusCode)
                        {
                            var error = await createResponse.Content.ReadAsStringAsync();
                            // Ignore conflict errors (folder might have been created by another process)
                            if (!error.Contains("nameAlreadyExists"))
                            {
                                throw new Exception($"Failed to create folder '{currentPath}': {createResponse.StatusCode} - {error}");
                            }
                        }
                    }
                }
            }, $"Create folder '{path}'");
        }

        /// <summary>
        /// Check if a directory exists
        /// </summary>
        public async Task<bool> DirectoryExistsAsync(string path)
        {
            await InitializeAsync();
            
            var token = await _authManager.GetAccessTokenAsync();
            using (var client = CreateHttpClient(token))
            {
                var url = $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{path}";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return false;

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                return json["folder"] != null; // It's a folder if "folder" property exists
            }
        }

        /// <summary>
        /// List files and folders in a directory
        /// </summary>
        public async Task<List<string>> ListDirectoryAsync(string path)
        {
            await InitializeAsync();
            
            var token = await _authManager.GetAccessTokenAsync();
            using (var client = CreateHttpClient(token))
            {
                var url = string.IsNullOrEmpty(path)
                    ? $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root/children"
                    : $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{path}:/children";

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new DirectoryNotFoundException($"Directory '{path}' not found: {response.StatusCode} - {error}");
                }

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                var items = json["value"].Children<JObject>();
                
                return items.Select(item => item["name"].Value<string>()).ToList();
            }
        }

        /// <summary>
        /// Get the SharePoint web URL for a file
        /// </summary>
        public async Task<string> GetFileUrlAsync(string path)
        {
            await InitializeAsync();
            
            var token = await _authManager.GetAccessTokenAsync();
            using (var client = CreateHttpClient(token))
            {
                var url = $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{path}";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new FileNotFoundException($"File '{path}' not found: {response.StatusCode} - {error}");
                }

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                return json["webUrl"].Value<string>();
            }
        }

        /// <summary>
        /// Get the SharePoint web URL for a folder
        /// </summary>
        public async Task<string> GetFolderUrlAsync(string path)
        {
            await InitializeAsync();
            
            var token = await _authManager.GetAccessTokenAsync();
            using (var client = CreateHttpClient(token))
            {
                var url = string.IsNullOrEmpty(path)
                    ? $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root"
                    : $"https://graph.microsoft.com/v1.0/drives/{_driveId}/root:/{path}";

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new DirectoryNotFoundException($"Folder '{path}' not found: {response.StatusCode} - {error}");
                }

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                return json["webUrl"].Value<string>();
            }
        }

        private HttpClient CreateHttpClient(string accessToken)
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return client;
        }
    }
}

