using System;
using System.Configuration;

namespace ETCStorageHelper.Configuration
{
    /// <summary>
    /// Supported cloud environments
    /// </summary>
    public enum CloudEnvironment
    {
        /// <summary>Commercial/Global Azure (default)</summary>
        Commercial,
        /// <summary>US Government GCC High</summary>
        GCCHigh,
        /// <summary>US Government DoD</summary>
        DoD
    }

    /// <summary>
    /// Configuration for ETC Storage Helper
    /// </summary>
    public class ETCStorageConfig
    {
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string SiteUrl { get; set; }
        public string LibraryName { get; set; }
        public int TimeoutSeconds { get; set; }
        public int RetryAttempts { get; set; }
        
        /// <summary>
        /// The cloud environment (Commercial, GCCHigh, or DoD). Defaults to Commercial.
        /// </summary>
        public CloudEnvironment Environment { get; set; } = CloudEnvironment.Commercial;

        /// <summary>
        /// Gets the Microsoft Graph API base URL for the configured environment
        /// </summary>
        public string GraphBaseUrl
        {
            get
            {
                switch (Environment)
                {
                    case CloudEnvironment.GCCHigh:
                        return "https://graph.microsoft.us";
                    case CloudEnvironment.DoD:
                        return "https://dod-graph.microsoft.us";
                    default:
                        return "https://graph.microsoft.com";
                }
            }
        }

        /// <summary>
        /// Gets the Azure AD login endpoint for the configured environment
        /// </summary>
        public string LoginBaseUrl
        {
            get
            {
                switch (Environment)
                {
                    case CloudEnvironment.GCCHigh:
                    case CloudEnvironment.DoD:
                        return "https://login.microsoftonline.us";
                    default:
                        return "https://login.microsoftonline.com";
                }
            }
        }

        /// <summary>
        /// Gets the Graph scope for token acquisition
        /// </summary>
        public string GraphScope => $"{GraphBaseUrl}/.default";

        /// <summary>
        /// Load configuration from app.config/web.config
        /// </summary>
        public static ETCStorageConfig LoadFromAppSettings()
        {
            // Parse cloud environment (default to Commercial)
            var envString = ConfigurationManager.AppSettings["ETCStorage.Environment"] ?? "Commercial";
            if (!Enum.TryParse<CloudEnvironment>(envString, ignoreCase: true, out var environment))
            {
                environment = CloudEnvironment.Commercial;
            }

            return new ETCStorageConfig
            {
                TenantId = ConfigurationManager.AppSettings["ETCStorage.TenantId"],
                ClientId = ConfigurationManager.AppSettings["ETCStorage.ClientId"],
                ClientSecret = ConfigurationManager.AppSettings["ETCStorage.ClientSecret"],
                SiteUrl = ConfigurationManager.AppSettings["ETCStorage.SiteUrl"],
                LibraryName = ConfigurationManager.AppSettings["ETCStorage.LibraryName"] ?? "Client Projects",
                TimeoutSeconds = int.TryParse(ConfigurationManager.AppSettings["ETCStorage.TimeoutSeconds"], out var timeout) ? timeout : 30,
                RetryAttempts = int.TryParse(ConfigurationManager.AppSettings["ETCStorage.RetryAttempts"], out var retries) ? retries : 3,
                Environment = environment
            };
        }

        /// <summary>
        /// Validates the configuration
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(TenantId))
                throw new ConfigurationErrorsException("ETCStorage.TenantId is required in app.config");
            
            if (string.IsNullOrWhiteSpace(ClientId))
                throw new ConfigurationErrorsException("ETCStorage.ClientId is required in app.config");
            
            if (string.IsNullOrWhiteSpace(ClientSecret))
                throw new ConfigurationErrorsException("ETCStorage.ClientSecret is required in app.config");
            
            if (string.IsNullOrWhiteSpace(SiteUrl))
                throw new ConfigurationErrorsException("ETCStorage.SiteUrl is required in app.config");
            
            if (string.IsNullOrWhiteSpace(LibraryName))
                throw new ConfigurationErrorsException("ETCStorage.LibraryName is required in app.config");
        }
    }
}

