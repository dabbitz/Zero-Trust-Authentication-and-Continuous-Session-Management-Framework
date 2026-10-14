using System;
using ZeroTrustClient.Services;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using System.IdentityModel.Tokens.Jwt;

namespace ZeroTrustClient
{
    public partial class AppPage : ContentPage
    {
        private readonly IHttpClientFactory _clientFactory;
        public AppPage(IHttpClientFactory clientFactory)
        {
            InitializeComponent();
            _clientFactory = clientFactory;
            // Initialize with the app's dispatcher
            InactivityService.Initialize(Dispatcher, _clientFactory);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            InactivityService.Start(); // Start the 45s countdown
            await DisplayCurrentToken();

        }
        private async Task DisplayCurrentToken()
        {
            try
            {
                var token = await SecureStorage.GetAsync("access_token");
                if (!string.IsNullOrEmpty(token))
                {
                    TokenDisplay.Text = token;
                }
                else
                {
                    TokenDisplay.Text = "No active session token found.";
                }
            }
            catch (Exception ex)
            {
                TokenDisplay.Text = $"Error loading token: {ex.Message}";
            }
        }

        private void OnPageInteracted(object sender, EventArgs e)
        {
            InactivityService.Reset();
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            bool answer = await DisplayAlertAsync("Logout", "Are you sure?", "Yes", "No");
            if (!answer) return;

            // 1. STOP the "Security Guard" timer immediately
            InactivityService.Stop();

            try
            {
                var api = new ApiService();

                // 2. Tell the server to flag this session as "Requires OTP"
                // This uses the modified Logout endpoint
                await api.LogoutServerSideAsync();
            }
            catch
            {
                // If the server is down, we still want to log out locally
            }

            // 3. Clear local tokens
            SecureStorage.Default.Remove("access_token");
            SecureStorage.Default.Remove("refresh_token");

            // 4. Redirect to login
            Application.Current.MainPage = new NavigationPage(new LoginPage(_clientFactory));
        }

        private async void OnCriticalActionClicked(object sender, EventArgs e)
        {
            try
            {
                using var client = _clientFactory.CreateClient("ZeroTrustAPI");
                string realIp = await DeviceInfoHelper.GetPublicIpAsync();

                // Critical request
                var request = new
                {
                    DeviceId = DeviceInfo.Model,
                    Action = "critical_action",
                    Location = await DeviceInfoHelper.GetLocationAsync(),
                    RealIp = realIp
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await client.PostAsync("api/zerotrust/secure", content);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // Parse response
                    var result = JsonSerializer.Deserialize<SecureResponse>(
                        responseBody,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (result?.OtpRequired == true)
                    {
                        await DisplayAlertAsync("Security Check", "OTP required for this action!", "OK");
                        MainPage.PendingNavigationIntent = "ResumeCriticalAction";

                        // IMPORTANT: force reset navigation stack cleanly
                        Application.Current.MainPage = new NavigationPage(new MainPage(_clientFactory));
                    }
                    else
                    {
                        await DisplayAlertAsync("Success", "Critical action executed!", "OK");
                    }
                }
                else
                {
                    await DisplayAlertAsync("Error", $"Server error: {response.StatusCode}", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }

        public class SecureResponse
        {
            public bool OtpRequired { get; set; }
            public string Message { get; set; }
        }
    }
}
