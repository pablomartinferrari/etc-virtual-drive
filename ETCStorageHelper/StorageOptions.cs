using System;
using ETCStorageHelper.Caching;
using ETCStorageHelper.Logging;

namespace ETCStorageHelper
{
    /// <summary>
    /// Represents a SharePoint site configuration.
    /// Consumer applications create instances for each SharePoint site they need to access.
    /// </summary>
    public class SharePointSite
    {
        /// <summary>
        /// Unique identifier for this site configuration (e.g., "Commercial", "GCCHigh", "ClientA")
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// User ID for audit logging (e.g., "pferrari009", "DOMAIN\\username", employee ID)
        /// Set this to identify who is performing operations
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// User display name for audit logging (e.g., "Pablo Ferrari")
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Application name for audit logging (e.g., "Environmental Desktop App v2.1")
        /// </summary>
        public string ApplicationName { get; set; }

        /// <summary>
        /// Azure AD Tenant ID
        /// </summary>
        public string TenantId { get; set; }

        /// <summary>
        /// Azure AD Client ID (Application ID)
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// Azure AD Client Secret
        /// </summary>
        public string ClientSecret { get; set; }

        /// <summary>
        /// SharePoint site URL (e.g., "https://tenant.sharepoint.com/sites/etc-projects")
        /// </summary>
        public string SiteUrl { get; set; }

        /// <summary>
        /// Document library name (e.g., "Client Projects")
        /// </summary>
        public string LibraryName { get; set; }

        /// <summary>
        /// Connection timeout in seconds (default: 30)
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Number of retry attempts for failed operations (default: 3)
        /// </summary>
        public int RetryAttempts { get; set; } = 3;

        /// <summary>
        /// Caching configuration (default: enabled with 1GB cache, 24-hour expiration)
        /// </summary>
        public CacheConfig CacheConfig { get; set; } = new CacheConfig();

        /// <summary>
        /// Auto-async threshold in MB (default: 50 MB)
        /// Files larger than this are automatically uploaded/downloaded asynchronously
        /// </summary>
        public int AutoAsyncThresholdMB { get; set; } = 50;

        /// <summary>
        /// Logger for audit trail (MANDATORY - cannot be disabled)
        /// Default: SharePoint list logger for centralized audit trail
        /// IMPORTANT: Logging is required for compliance and cannot be turned off
        /// </summary>
        internal IETCLogger Logger { get; set; }

        /// <summary>
        /// Creates a SharePoint site configuration
        /// </summary>
        public SharePointSite(string name, string tenantId, string clientId, string clientSecret, 
                             string siteUrl, string libraryName)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
            ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
            ClientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
            SiteUrl = siteUrl ?? throw new ArgumentNullException(nameof(siteUrl));
            LibraryName = libraryName ?? throw new ArgumentNullException(nameof(libraryName));
        }

        /// <summary>
        /// Creates a site configuration from app.config keys with specified prefix
        /// </summary>
        /// <param name="name">Site name</param>
        /// <param name="configPrefix">Configuration key prefix (e.g., "ETCStorage.Commercial")</param>
        /// <param name="userId">User ID for audit logging (REQUIRED)</param>
        /// <param name="userName">User display name for audit logging (REQUIRED)</param>
        /// <param name="applicationName">Application name for audit logging (optional)</param>
        public static SharePointSite FromConfig(
            string name, 
            string configPrefix,
            string userId,
            string userName,
            string applicationName = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID is required for audit logging", nameof(userId));
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("User name is required for audit logging", nameof(userName));

            var site = new SharePointSite(
                name,
                System.Configuration.ConfigurationManager.AppSettings[$"{configPrefix}.TenantId"],
                System.Configuration.ConfigurationManager.AppSettings[$"{configPrefix}.ClientId"],
                System.Configuration.ConfigurationManager.AppSettings[$"{configPrefix}.ClientSecret"],
                System.Configuration.ConfigurationManager.AppSettings[$"{configPrefix}.SiteUrl"],
                System.Configuration.ConfigurationManager.AppSettings[$"{configPrefix}.LibraryName"]
            );

            // Set user/app info for logging (REQUIRED)
            site.UserId = userId;
            site.UserName = userName;
            site.ApplicationName = applicationName ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";

            // MANDATORY: Set up centralized SharePoint logging (cannot be disabled)
            // Note: Log list name is hardcoded in the library - consumers don't configure this
            site.Logger = Logging.SharePointListLogger.FromSite(site, "ETC Storage Logs");

            return site;
        }

        /// <summary>
        /// Validates the configuration
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                throw new ArgumentException("Site name is required", nameof(Name));
            if (string.IsNullOrWhiteSpace(TenantId))
                throw new ArgumentException("TenantId is required", nameof(TenantId));
            if (string.IsNullOrWhiteSpace(ClientId))
                throw new ArgumentException("ClientId is required", nameof(ClientId));
            if (string.IsNullOrWhiteSpace(ClientSecret))
                throw new ArgumentException("ClientSecret is required", nameof(ClientSecret));
            if (string.IsNullOrWhiteSpace(SiteUrl))
                throw new ArgumentException("SiteUrl is required", nameof(SiteUrl));
            if (string.IsNullOrWhiteSpace(LibraryName))
                throw new ArgumentException("LibraryName is required", nameof(LibraryName));
            
            // MANDATORY: User information required for audit logging
            if (string.IsNullOrWhiteSpace(UserId))
                throw new ArgumentException("User ID is required for audit logging", nameof(UserId));
            if (string.IsNullOrWhiteSpace(UserName))
                throw new ArgumentException("User name is required for audit logging", nameof(UserName));
            
            // MANDATORY: Logger must be configured
            if (Logger == null)
                throw new InvalidOperationException("Logger is required for compliance. Use SharePointSite.FromConfig() to ensure proper initialization.");
        }
    }
}

