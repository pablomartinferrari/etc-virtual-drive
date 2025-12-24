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

        /// <summary>
        /// Gets the Graph API base URL (e.g., https://graph.microsoft.com or https://graph.microsoft.us)
        /// </summary>
        private string GraphUrl => $"{_config.GraphBaseUrl}/v1.0";

        public SharePointClient(ETCStorageConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _authManager = new AuthenticationManager(config);
            _retryPolicy = new RetryPolicy(
                maxRetries: config.RetryAttempts,
                initialDelayMs: 2000,    // Increased from 1000ms for better network recovery
                maxDelayMs: 60000        // Increased from 30000ms for severe issues
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
                    var graphSiteUrl = $"{GraphUrl}/sites/{hostname}:{sitePath}";
                    Console.WriteLine($"[SharePoint] Environment: {_config.Environment}");
                    Console.WriteLine($"[SharePoint] Graph Base URL: {_config.GraphBaseUrl}");
                    Console.WriteLine($"[SharePoint] Site URL from config: {_config.SiteUrl}");
                    Console.WriteLine($"[SharePoint] Parsed hostname: {hostname}");
                    Console.WriteLine($"[SharePoint] Parsed site path: {sitePath}");
                    Console.WriteLine($"[SharePoint] Calling Graph API: {graphSiteUrl}");
                    System.Diagnostics.Debug.WriteLine($"[SharePointClient] Graph URL: {graphSiteUrl}");
                    
                    var siteResponse = await client.GetAsync(graphSiteUrl);
                    if (!siteResponse.IsSuccessStatusCode)
                    {
                        var error = await siteResponse.Content.ReadAsStringAsync();
                        var errorMessage = $"Failed to get site info: {siteResponse.StatusCode} - {error}";
                        
                        // Provide helpful error messages for common issues
                        if (siteResponse.StatusCode == System.Net.HttpStatusCode.BadRequest && error.Contains("Invalid hostname"))
                        {
                            errorMessage += $"\n\n⚠ The site URL '{_config.SiteUrl}' does not belong to tenant '{_config.TenantId}'.";
                            errorMessage += $"\n   Please verify:";
                            errorMessage += $"\n   1. The Site URL is correct";
                            errorMessage += $"\n   2. The Tenant ID matches the tenant that owns the SharePoint site";
                            errorMessage += $"\n   3. The site exists and is accessible";
                        }
                        
                        throw new Exception(errorMessage);
                    }

                    var siteJson = JObject.Parse(await siteResponse.Content.ReadAsStringAsync());
                    _siteId = siteJson["id"].Value<string>();

                    // Get drive ID (document library)
                    var drivesResponse = await client.GetAsync($"{GraphUrl}/sites/{_siteId}/drives");
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
                    var url = $"{GraphUrl}/drives/{_driveId}/root:/{path}:/content";
                    
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
                    // Increase timeout for large files - calculate based on file size
                    // Assume 1 MB/sec minimum upload speed, plus overhead
                    var fileSizeMB = data.Length / 1024.0 / 1024.0;
                    var calculatedTimeout = Math.Max(
                        _config.TimeoutSeconds * 3,
                        (int)(fileSizeMB + 120)  // File size in seconds + 2 min overhead
                    );
                    client.Timeout = TimeSpan.FromSeconds(calculatedTimeout);
                    System.Diagnostics.Debug.WriteLine(
                        $"[SharePointClient] Large file upload timeout set to {calculatedTimeout}s for {fileSizeMB:F1} MB file");

                    // Step 1: Create upload session
                    var sessionUrl = $"{GraphUrl}/drives/{_driveId}/root:/{path}:/createUploadSession";
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
                    var url = $"{GraphUrl}/drives/{_driveId}/root:/{path}";
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
                var url = $"{GraphUrl}/drives/{_driveId}/root:/{path}";
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
                var url = $"{GraphUrl}/drives/{_driveId}/root:/{path}";
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
                var url = $"{GraphUrl}/drives/{_driveId}/root:/{path}";
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
                        var checkUrl = $"{GraphUrl}/drives/{_driveId}/root:/{currentPath}";
                        var checkResponse = await client.GetAsync(checkUrl);

                        if (checkResponse.IsSuccessStatusCode)
                            continue; // Folder already exists

                        // Create folder
                        var createUrl = string.IsNullOrEmpty(parentPath)
                            ? $"{GraphUrl}/drives/{_driveId}/root/children"
                            : $"{GraphUrl}/drives/{_driveId}/root:/{parentPath}:/children";

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
                var url = $"{GraphUrl}/drives/{_driveId}/root:/{path}";
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
                    ? $"{GraphUrl}/drives/{_driveId}/root/children"
                    : $"{GraphUrl}/drives/{_driveId}/root:/{path}:/children";

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
        /// List files and folders in a directory with file information (name, modified date, size)
        /// </summary>
        public async Task<List<ETCFileInfo>> ListDirectoryWithInfoAsync(string path)
        {
            await InitializeAsync();
            
            var token = await _authManager.GetAccessTokenAsync();
            using (var client = CreateHttpClient(token))
            {
                var url = string.IsNullOrEmpty(path)
                    ? $"{GraphUrl}/drives/{_driveId}/root/children"
                    : $"{GraphUrl}/drives/{_driveId}/root:/{path}:/children";

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new DirectoryNotFoundException($"Directory '{path}' not found: {response.StatusCode} - {error}");
                }

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                var items = json["value"].Children<JObject>();
                
                var fileInfos = new List<ETCFileInfo>();
                foreach (var item in items)
                {
                    var fileInfo = new ETCFileInfo
                    {
                        Name = item["name"]?.Value<string>() ?? "",
                        IsFolder = item["folder"] != null,
                        FullPath = string.IsNullOrEmpty(path) 
                            ? item["name"]?.Value<string>() ?? ""
                            : $"{path}/{item["name"]?.Value<string>() ?? ""}"
                    };

                    // Parse last modified date
                    if (item["lastModifiedDateTime"] != null)
                    {
                        var dateStr = item["lastModifiedDateTime"].Value<string>();
                        if (DateTime.TryParse(dateStr, out DateTime lastModified))
                        {
                            fileInfo.LastModified = lastModified.ToUniversalTime();
                        }
                    }

                    // Parse file size
                    if (item["size"] != null && !fileInfo.IsFolder)
                    {
                        fileInfo.Size = item["size"].Value<long>();
                    }

                    fileInfos.Add(fileInfo);
                }
                
                return fileInfos;
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
                var url = $"{GraphUrl}/drives/{_driveId}/root:/{path}";
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
                    ? $"{GraphUrl}/drives/{_driveId}/root"
                    : $"{GraphUrl}/drives/{_driveId}/root:/{path}";

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

        /// <summary>
        /// Rename or move a folder to a new path
        /// </summary>
        /// <param name="sourcePath">Source folder path</param>
        /// <param name="destinationPath">Destination folder path</param>
        /// <param name="overwriteIfExists">If true, overwrites existing folder at destination. If false, throws error if folder exists.</param>
        public async Task RenameFolderAsync(string sourcePath, string destinationPath, bool overwriteIfExists = false)
        {
            await InitializeAsync();

            await _retryPolicy.ExecuteAsync(async () =>
            {
                var token = await _authManager.GetAccessTokenAsync();
                using (var client = CreateHttpClient(token))
                {
                    // Parse destination path into parent folder and new name
                    var destParts = destinationPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    var newName = destParts[destParts.Length - 1];
                    var parentPath = destParts.Length > 1 
                        ? string.Join("/", destParts.Take(destParts.Length - 1))
                        : "";

                    // Build the PATCH request
                    var url = $"{GraphUrl}/drives/{_driveId}/root:/{sourcePath}";
                    
                    var updateData = new JObject
                    {
                        ["name"] = newName
                    };

                    // Set conflict behavior (mimics System.IO.Directory.Move behavior)
                    if (overwriteIfExists)
                    {
                        updateData["@microsoft.graph.conflictBehavior"] = "replace";
                    }
                    else
                    {
                        updateData["@microsoft.graph.conflictBehavior"] = "fail";
                    }

                    // If moving to a different parent folder, include parentReference
                    if (!string.IsNullOrEmpty(parentPath))
                    {
                        // Get the parent folder's ID
                        var parentUrl = $"{GraphUrl}/drives/{_driveId}/root:/{parentPath}";
                        var parentResponse = await client.GetAsync(parentUrl);
                        
                        if (!parentResponse.IsSuccessStatusCode)
                        {
                            var parentError = await parentResponse.Content.ReadAsStringAsync();
                            throw new DirectoryNotFoundException($"Destination parent folder '{parentPath}' not found: {parentResponse.StatusCode} - {parentError}");
                        }

                        var parentJson = JObject.Parse(await parentResponse.Content.ReadAsStringAsync());
                        var parentId = parentJson["id"].Value<string>();

                        updateData["parentReference"] = new JObject
                        {
                            ["id"] = parentId
                        };
                    }
                    else
                    {
                        // Moving to root - set parent to the drive root
                        updateData["parentReference"] = new JObject
                        {
                            ["id"] = _driveId,
                            ["path"] = $"/drives/{_driveId}/root"
                        };
                    }

                    var content = new StringContent(updateData.ToString(), Encoding.UTF8, "application/json");
                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
                    {
                        Content = content
                    };

                    var response = await client.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        throw new Exception($"Failed to rename/move folder from '{sourcePath}' to '{destinationPath}': {response.StatusCode} - {error}");
                    }
                }
            }, $"Rename/move folder from '{sourcePath}' to '{destinationPath}'");
        }

        /// <summary>
        /// Rename or move a file to a new path
        /// </summary>
        /// <param name="sourcePath">Source file path</param>
        /// <param name="destinationPath">Destination file path</param>
        /// <param name="overwriteIfExists">If true, overwrites existing file at destination. If false, throws error if file exists.</param>
        public async Task RenameFileAsync(string sourcePath, string destinationPath, bool overwriteIfExists = false)
        {
            await InitializeAsync();

            await _retryPolicy.ExecuteAsync(async () =>
            {
                var token = await _authManager.GetAccessTokenAsync();
                using (var client = CreateHttpClient(token))
                {
                    // Parse destination path into parent folder and new name
                    var destParts = destinationPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    var newName = destParts[destParts.Length - 1];
                    var parentPath = destParts.Length > 1 
                        ? string.Join("/", destParts.Take(destParts.Length - 1))
                        : "";

                    // Build the PATCH request
                    var url = $"{GraphUrl}/drives/{_driveId}/root:/{sourcePath}";
                    
                    var updateData = new JObject
                    {
                        ["name"] = newName
                    };

                    // Set conflict behavior (mimics System.IO.File.Move behavior)
                    if (overwriteIfExists)
                    {
                        updateData["@microsoft.graph.conflictBehavior"] = "replace";
                    }
                    else
                    {
                        updateData["@microsoft.graph.conflictBehavior"] = "fail";
                    }

                    // If moving to a different parent folder, include parentReference
                    if (!string.IsNullOrEmpty(parentPath))
                    {
                        // Get the parent folder's ID
                        var parentUrl = $"{GraphUrl}/drives/{_driveId}/root:/{parentPath}";
                        var parentResponse = await client.GetAsync(parentUrl);
                        
                        if (!parentResponse.IsSuccessStatusCode)
                        {
                            var parentError = await parentResponse.Content.ReadAsStringAsync();
                            throw new DirectoryNotFoundException($"Destination parent folder '{parentPath}' not found: {parentResponse.StatusCode} - {parentError}");
                        }

                        var parentJson = JObject.Parse(await parentResponse.Content.ReadAsStringAsync());
                        var parentId = parentJson["id"].Value<string>();

                        updateData["parentReference"] = new JObject
                        {
                            ["id"] = parentId
                        };
                    }
                    else
                    {
                        // Moving to root - set parent to the drive root
                        updateData["parentReference"] = new JObject
                        {
                            ["id"] = _driveId,
                            ["path"] = $"/drives/{_driveId}/root"
                        };
                    }

                    var content = new StringContent(updateData.ToString(), Encoding.UTF8, "application/json");
                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
                    {
                        Content = content
                    };

                    var response = await client.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        throw new Exception($"Failed to rename/move file from '{sourcePath}' to '{destinationPath}': {response.StatusCode} - {error}");
                    }
                }
            }, $"Rename/move file from '{sourcePath}' to '{destinationPath}'");
        }

        private HttpClient CreateHttpClient(string accessToken)
        {
            // Ensure TLS 1.2 for corporate environments
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;

            var handler = new HttpClientHandler
            {
                // Follow redirects and decompress automatically
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                UseProxy = true
            };

            // Honor system proxy with default credentials when present
            var systemProxy = System.Net.WebRequest.DefaultWebProxy;
            if (systemProxy != null)
            {
                systemProxy.Credentials = System.Net.CredentialCache.DefaultNetworkCredentials;
                handler.Proxy = systemProxy;
            }

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(_config.TimeoutSeconds, 120))
            };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return client;
        }
    }
}

