using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using ETCStorageHelper.Configuration;
using Newtonsoft.Json.Linq;

namespace ETCStorageHelper.Authentication
{
    /// <summary>
    /// Manages authentication tokens for SharePoint access
    /// </summary>
    internal class AuthenticationManager
    {
        private readonly ETCStorageConfig _config;
        private string _cachedToken;
        private DateTime _tokenExpiry;
        private readonly object _lockObject = new object();

        public AuthenticationManager(ETCStorageConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Gets a valid access token, refreshing if necessary
        /// </summary>
        public async Task<string> GetAccessTokenAsync()
        {
            lock (_lockObject)
            {
                // Return cached token if still valid (with 5 minute buffer)
                if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry.AddMinutes(-5))
                {
                    return _cachedToken;
                }
            }

            // Token expired or doesn't exist, get a new one
            var token = await AcquireTokenAsync();
            
            lock (_lockObject)
            {
                _cachedToken = token.AccessToken;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(token.ExpiresIn);
            }

            return token.AccessToken;
        }

        private async Task<TokenResponse> AcquireTokenAsync()
        {
            var tokenEndpoint = $"https://login.microsoftonline.com/{_config.TenantId}/oauth2/v2.0/token";
            
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);

                var content = new StringContent(
                    $"client_id={Uri.EscapeDataString(_config.ClientId)}" +
                    $"&scope=https://graph.microsoft.com/.default" +
                    $"&client_secret={Uri.EscapeDataString(_config.ClientSecret)}" +
                    $"&grant_type=client_credentials",
                    Encoding.UTF8,
                    "application/x-www-form-urlencoded"
                );

                var response = await client.PostAsync(tokenEndpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to acquire access token: {response.StatusCode} - {responseContent}");
                }

                var json = JObject.Parse(responseContent);
                return new TokenResponse
                {
                    AccessToken = json["access_token"].Value<string>(),
                    ExpiresIn = json["expires_in"].Value<int>()
                };
            }
        }

        private class TokenResponse
        {
            public string AccessToken { get; set; }
            public int ExpiresIn { get; set; }
        }
    }
}

