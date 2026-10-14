using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ZeroTrustClient
{
    public partial class LoginPage : ContentPage
    {
        private readonly IHttpClientFactory _clientFactory;
        public LoginPage(IHttpClientFactory clientFactory)
        {
            InitializeComponent();
            _clientFactory = clientFactory;
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            var sw = Stopwatch.StartNew();

            using var client = _clientFactory.CreateClient("ZeroTrustAPI");

            var request = new
            {
                Username = UsernameEntry.Text,
                Password = PasswordEntry.Text,
                // Identify if this is the mobile app
                IsMobile = DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS
            };

            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("api/zerotrust/login", content);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<LoginResponse>(
                    body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result == null || string.IsNullOrEmpty(result.AccessToken))
                {
                    await DisplayAlertAsync("Error", "Invalid server response", "OK");
                    return;
                }

                await SecureStorage.SetAsync("access_token", result.AccessToken);
                await SecureStorage.SetAsync("refresh_token", result.RefreshToken);

                MainPage.CurrentAuthState = MainPage.AuthState.Unverified;

                if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    sw.Stop();
                    Debug.WriteLine($"[PERF] Login took {sw.ElapsedMilliseconds} ms");

                    Application.Current.MainPage = new NavigationPage(new PhonePage(_clientFactory));
                }
                else
                {
                    sw.Stop();
                    Debug.WriteLine($"[PERF] Login took {sw.ElapsedMilliseconds} ms");

                    Application.Current.MainPage = new NavigationPage(new MainPage(_clientFactory));
                }
            }
            else
            {
                sw.Stop();
                Debug.WriteLine($"[PERF] Login took {sw.ElapsedMilliseconds} ms");

                await DisplayAlertAsync("Login Failed", "Invalid credentials", "OK");
            }
        }

        private async void OnGoBackClicked(object sender, EventArgs e)
        {
            Application.Current.MainPage = new NavigationPage(new StartPage(_clientFactory));
        }

        public class LoginResponse
        {
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
            public int UserId { get; set; }
        }
    }
}
