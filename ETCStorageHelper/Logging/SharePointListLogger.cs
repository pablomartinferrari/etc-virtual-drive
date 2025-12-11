using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ETCStorageHelper.Configuration;

namespace ETCStorageHelper.Logging
{
    /// <summary>
    /// Logs operations to a SharePoint list for centralized audit trail
    /// All users' logs go to the same SharePoint list - easy to query and analyze
    /// </summary>
    public class SharePointListLogger : IETCLogger
    {
        private readonly string _siteUrl;
        private readonly string _listName;
        private readonly Authentication.AuthenticationManager _authManager;

        /// <summary>
        /// Creates a SharePoint list logger
        /// </summary>
        /// <param name="siteUrl">SharePoint site URL (e.g., "https://tenant.sharepoint.com/sites/etc-projects")</param>
        /// <param name="listName">SharePoint list name (e.g., "ETC Storage Logs")</param>
        /// <param name="tenantId">Azure AD Tenant ID</param>
        /// <param name="clientId">Azure AD Client ID</param>
        /// <param name="clientSecret">Azure AD Client Secret</param>
        public SharePointListLogger(string siteUrl, string listName, string tenantId, string clientId, string clientSecret)
        {
            _siteUrl = siteUrl ?? throw new ArgumentNullException(nameof(siteUrl));
            _listName = listName ?? throw new ArgumentNullException(nameof(listName));

            // Create config for authentication
            // NOTE: TimeoutSeconds MUST be > 0 or HttpClient will throw when setting Timeout.
            // We use a sensible default here that is independent of any app.config settings.
            var config = new ETCStorageConfig
            {
                TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId)),
                ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId)),
                ClientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret)),
                TimeoutSeconds = 60,
                RetryAttempts = 3
            };

            _authManager = new Authentication.AuthenticationManager(config);
        }

        /// <summary>
        /// Creates logger from existing site configuration
        /// </summary>
        public static SharePointListLogger FromSite(SharePointSite site, string listName = "ETC Storage Logs")
        {
            return new SharePointListLogger(
                site.SiteUrl,
                listName,
                site.TenantId,
                site.ClientId,
                site.ClientSecret
            );
        }

        public void Log(LogEntry entry)
        {
            if (entry == null)
                return;

            Console.WriteLine($"[SharePointListLogger] Log() called - Operation: {entry.Operation}, Path: {entry.Path}");

            // Log asynchronously to avoid blocking operations
            Task.Run(async () =>
            {
                try
                {
                    await LogToSharePointAsync(entry);
                    System.Diagnostics.Debug.WriteLine(
                        $"[SharePointListLogger] ✓ Logged to SharePoint list: {entry.Operation} by {entry.UserName}");
                }
                catch (Exception ex)
                {
                    // Don't throw - logging failures shouldn't break the application
                    var errorMsg = $"[SharePointListLogger] ✗ Failed to write to SharePoint list: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine(errorMsg);
                    Console.WriteLine(errorMsg); // Also write to console
                    System.Diagnostics.Debug.WriteLine($"[SharePointListLogger] Stack: {ex.StackTrace}");
                    
                    // Fall back to local file logging
                    try
                    {
                        var logDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "ETCStorageHelper", "Logs");
                        Directory.CreateDirectory(logDir);
                        var logFile = Path.Combine(logDir, $"sharepoint-errors-{DateTime.Now:yyyy-MM-dd}.log");
                        var logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | ERROR | SharePoint logging failed: {ex.Message} | Entry: {entry}\n";
                        File.AppendAllText(logFile, logLine);
                        Console.WriteLine($"[SharePointListLogger] Logged error to: {logFile}");
                    }
                    catch
                    {
                        // If even file logging fails, just give up
                    }
                }
            });
        }

        private async Task LogToSharePointAsync(LogEntry entry)
        {
            var token = await _authManager.GetAccessTokenAsync();

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                // Get site ID
                var siteId = await GetSiteIdAsync(client);
                
                // Get list ID
                var listId = await GetOrCreateListAsync(client, siteId);

                // Add item to list
                await AddListItemAsync(client, siteId, listId, entry);
            }
        }

        private async Task<string> GetSiteIdAsync(HttpClient client)
        {
            var hostname = new Uri(_siteUrl).Host;
            var sitePath = new Uri(_siteUrl).AbsolutePath;
            
            var url = $"https://graph.microsoft.com/v1.0/sites/{hostname}:{sitePath}";
            
            System.Diagnostics.Debug.WriteLine($"[SharePointListLogger] Getting site ID for: {_siteUrl}");
            System.Diagnostics.Debug.WriteLine($"[SharePointListLogger] Graph API URL: {url}");
            Console.WriteLine($"[SharePointListLogger] Getting site ID for: {_siteUrl}");
            
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to get site ID for '{_siteUrl}': {response.StatusCode} - {error}");
            }

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            var siteId = json["id"].Value<string>();
            var siteName = json["displayName"]?.Value<string>() ?? "Unknown";
            var siteWebUrl = json["webUrl"]?.Value<string>() ?? "Unknown";
            
            System.Diagnostics.Debug.WriteLine($"[SharePointListLogger] ✓ Got site: {siteName} ({siteWebUrl})");
            System.Diagnostics.Debug.WriteLine($"[SharePointListLogger]   Site ID: {siteId}");
            Console.WriteLine($"[SharePointListLogger] ✓ Got site: {siteName}");
            Console.WriteLine($"[SharePointListLogger]   Site ID: {siteId}");
            
            return siteId;
        }

        private async Task<string> GetOrCreateListAsync(HttpClient client, string siteId)
        {
            // Try to get existing list (get all lists and filter in code - $filter doesn't work reliably)
            var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists";
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                var lists = json["value"] as JArray;
                
                if (lists != null)
                {
                    foreach (var list in lists)
                    {
                        var displayName = list["displayName"]?.Value<string>();
                        if (displayName != null && displayName.Equals(_listName, StringComparison.OrdinalIgnoreCase))
                        {
                            var existingListId = list["id"].Value<string>();
                            System.Diagnostics.Debug.WriteLine($"[SharePointListLogger] Found existing list '{_listName}' with ID: {existingListId}");
                            return existingListId;
                        }
                    }
                }
            }

            // List doesn't exist - create it
            return await CreateListAsync(client, siteId);
        }

        private async Task<string> CreateListAsync(HttpClient client, string siteId)
        {
            var msg = $"[SharePointListLogger] Creating SharePoint list '{_listName}'...";
            System.Diagnostics.Debug.WriteLine(msg);
            Console.WriteLine(msg); // Also write to console

            var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists";

            var listDefinition = new JObject
            {
                ["displayName"] = _listName,
                ["columns"] = new JArray
                {
                    new JObject { ["name"] = "Level", ["text"] = new JObject() },
                    new JObject { ["name"] = "UserId", ["text"] = new JObject() },
                    new JObject { ["name"] = "UserName", ["text"] = new JObject() },
                    new JObject { ["name"] = "Operation", ["text"] = new JObject() },
                    new JObject { ["name"] = "SiteName", ["text"] = new JObject() },
                    new JObject { ["name"] = "Path", ["text"] = new JObject() },
                    new JObject { ["name"] = "DestinationPath", ["text"] = new JObject() },
                    new JObject { ["name"] = "FileSizeMB", ["number"] = new JObject() },
                    new JObject { ["name"] = "DurationMs", ["number"] = new JObject() },
                    new JObject { ["name"] = "Success", ["boolean"] = new JObject() },
                    new JObject { ["name"] = "ErrorMessage", ["text"] = new JObject() },
                    new JObject { ["name"] = "MachineName", ["text"] = new JObject() },
                    new JObject { ["name"] = "ApplicationName", ["text"] = new JObject() }
                }
            };

            var content = new StringContent(listDefinition.ToString(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to create list: {response.StatusCode} - {error}");
            }

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            var listId = json["id"].Value<string>();

            var successMsg = $"[SharePointListLogger] ✓ SharePoint list '{_listName}' created successfully!";
            System.Diagnostics.Debug.WriteLine(successMsg);
            Console.WriteLine(successMsg);
            Console.WriteLine($"[SharePointListLogger] Access at: {_siteUrl}/Lists/{_listName.Replace(" ", "%20")}");

            return listId;
        }

        private async Task AddListItemAsync(HttpClient client, string siteId, string listId, LogEntry entry)
        {
            var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/items";

            // DEBUG: Log all entry values
            Console.WriteLine($"[DEBUG] Entry values:");
            Console.WriteLine($"  Level: {entry.Level} (type: {entry.Level.GetType().Name})");
            Console.WriteLine($"  FileSizeMB: {entry.FileSizeMB} (type: {entry.FileSizeMB.GetType().Name})");
            Console.WriteLine($"  DurationMs: {entry.DurationMs} (type: {entry.DurationMs.GetType().Name})");
            Console.WriteLine($"  Success: {entry.Success} (type: {entry.Success.GetType().Name})");
            Console.WriteLine($"  FileSizeMB is NaN: {double.IsNaN(entry.FileSizeMB)}");
            Console.WriteLine($"  FileSizeMB is Infinity: {double.IsInfinity(entry.FileSizeMB)}");

            JObject item;
            try
            {
                item = new JObject
                {
                    ["fields"] = new JObject
                    {
                        ["Title"] = $"{entry.Operation} by {entry.UserName}",
                        ["Level"] = entry.Level.ToString(),
                        ["UserId"] = entry.UserId ?? "",
                        ["UserName"] = entry.UserName ?? "",
                        ["Operation"] = entry.Operation ?? "",
                        ["SiteName"] = entry.SiteName ?? "",
                        ["Path"] = entry.Path ?? "",
                        ["DestinationPath"] = entry.DestinationPath ?? "",
                        ["FileSizeMB"] = entry.FileSizeMB,
                        ["DurationMs"] = entry.DurationMs,
                        ["Success"] = entry.Success,
                        ["ErrorMessage"] = entry.ErrorMessage ?? "",
                        ["MachineName"] = entry.MachineName ?? "",
                        ["ApplicationName"] = entry.ApplicationName ?? ""
                    }
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Error creating JSON object: {ex.Message}. Entry details - Operation: {entry.Operation}, Level: {entry.Level}, FileSizeMB: {entry.FileSizeMB}, DurationMs: {entry.DurationMs}", ex);
            }

            var content = new StringContent(item.ToString(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to add list item: {response.StatusCode} - {error}");
            }

            System.Diagnostics.Debug.WriteLine(
                $"[SharePointListLogger] Logged to SharePoint: {entry.Operation} by {entry.UserName}");
        }
    }
}

