using System;
using System.Configuration;

namespace ETCStorageHelper.Configuration
{
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
        /// Load configuration from app.config/web.config
        /// </summary>
        public static ETCStorageConfig LoadFromAppSettings()
        {
            return new ETCStorageConfig
            {
                TenantId = ConfigurationManager.AppSettings["ETCStorage.TenantId"],
                ClientId = ConfigurationManager.AppSettings["ETCStorage.ClientId"],
                ClientSecret = ConfigurationManager.AppSettings["ETCStorage.ClientSecret"],
                SiteUrl = ConfigurationManager.AppSettings["ETCStorage.SiteUrl"],
                LibraryName = ConfigurationManager.AppSettings["ETCStorage.LibraryName"] ?? "Client Projects",
                TimeoutSeconds = int.TryParse(ConfigurationManager.AppSettings["ETCStorage.TimeoutSeconds"], out var timeout) ? timeout : 30,
                RetryAttempts = int.TryParse(ConfigurationManager.AppSettings["ETCStorage.RetryAttempts"], out var retries) ? retries : 3
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

