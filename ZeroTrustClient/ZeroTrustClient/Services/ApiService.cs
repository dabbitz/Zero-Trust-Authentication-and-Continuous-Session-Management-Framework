using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static ZeroTrustClient.MainPage;
using static ZeroTrustClient.PhonePage;

namespace ZeroTrustClient.Services
{
    internal class ApiService
    {
        private readonly HttpClient _httpClient;

        public ApiService()
        {
            string baseUrl;

            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                // 10.0.2.2 is the host loopback. 
                // HTTP (port 5297) to bypass SSL certificate checks.
                baseUrl = "http://10.0.2.2:5193/";
            }
            else
            {
                baseUrl = "https://localhost:7092/";
            }

            _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        }

        public async Task<string> GetTestAsync()
        {
            await AddJwtAsync();

            var request = new
            {
                DeviceId = DeviceInfo.Model
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("api/zerotrust/secure", content);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var refreshed = await RefreshTokenAsync();

                if (refreshed)
                {
                    await AddJwtAsync();

                    var retryContent = new StringContent(json, Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync("api/zerotrust/secure", retryContent);
                }
            }

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<SecureResponse>(responseBody, options);

                if (result != null && result.OtpRequired)
                {
                    return "OTP_REQUIRED"; // special signal
                }

                return result?.Message ?? "Unknown response";
            }

            return $"Error: {response.StatusCode}";
        }

        public class SecureResponse
        {
            public bool OtpRequired { get; set; }
            public string Message { get; set; }
        }

        private async Task AddJwtAsync()
        {
            var token = await SecureStorage.GetAsync("access_token");

            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }

        private async Task<bool> RefreshTokenAsync()
        {
            var refreshToken = await SecureStorage.GetAsync("refresh_token");
            
            if (string.IsNullOrEmpty(refreshToken))
                return false;

            var request = new { RefreshToken = refreshToken, DeviceId = DeviceInfoHelper.GetDeviceModel() };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("api/zerotrust/refresh", content);

            if (!response.IsSuccessStatusCode)
                return false;

            var responseBody = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var result = JsonSerializer.Deserialize<TokenResponse>(responseBody, options);

            if (result == null || string.IsNullOrEmpty(result.AccessToken))
                return false;

            await SecureStorage.SetAsync("access_token", result.AccessToken);
            await SecureStorage.SetAsync("refresh_token", result.RefreshToken);

            return true;
        }

        public async Task NotifyTimeoutAsync()
        {
            var token = await SecureStorage.GetAsync("access_token");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.PostAsync("api/zerotrust/timeout", null);
        }

        public async Task<bool> LogoutServerSideAsync()
        {
            try
            {
                await AddJwtAsync(); // This automatically adds the Bearer token
                var response = await _httpClient.PostAsync("api/zerotrust/logout", null);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }
}
