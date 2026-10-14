using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ZeroTrustClient.Services
{
    public class AuthInterceptor : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 1. Automatically attach token to every request
            var token = await SecureStorage.GetAsync("access_token");
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            // 2. Proceed with the request
            var response = await base.SendAsync(request, cancellationToken);

            // 3. Handle 401 Unauthorized (Expired Token)
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                bool refreshed = await TryRefreshTokensAsync();
                if (refreshed)
                {
                    // Update header with the NEW token and retry
                    var newToken = await SecureStorage.GetAsync("access_token");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                    return await base.SendAsync(request, cancellationToken);
                }
            }

            return response;
        }

        private async Task<bool> TryRefreshTokensAsync()
        {
            try
            {
                var refreshToken = await SecureStorage.GetAsync("refresh_token");
                if (string.IsNullOrEmpty(refreshToken)) return false;

                // Determine base URL dynamically
                string baseUrl = DeviceInfo.Platform == DevicePlatform.Android
                    ? "http://10.0.2.2:5193/"
                    : "https://localhost:7092/";

                // Use a standard HttpClient to avoid intercepting our own refresh request
                using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

                // Match the RefreshRequest model from the controller
                var requestBody = new { RefreshToken = refreshToken };
                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Hit the refresh endpoint
                var response = await client.PostAsync("api/zerotrust/refresh", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseBodyStr = await response.Content.ReadAsStringAsync();

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                    // Match the TokenResponse expected back from the controller
                    var result = JsonSerializer.Deserialize<TokenResponse>(responseBodyStr, options);

                    if (result != null && !string.IsNullOrEmpty(result.AccessToken))
                    {
                        // Save the newly cycled tokens
                        await SecureStorage.SetAsync("access_token", result.AccessToken);
                        await SecureStorage.SetAsync("refresh_token", result.RefreshToken);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Refresh Error] {ex.Message}");
            }

            return false;
        }

        // Internal class to map the JSON response from the server
        private class TokenResponse
        {
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
        }
    }
}