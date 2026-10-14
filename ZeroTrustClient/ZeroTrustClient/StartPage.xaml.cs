using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ZeroTrustClient.Services;
using static ZeroTrustClient.MainPage;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls;

namespace ZeroTrustClient
{
    public partial class StartPage : ContentPage
    {
        private readonly IHttpClientFactory _clientFactory;
        private bool _checked = false;

        public StartPage(IHttpClientFactory clientFactory)
        {
            InitializeComponent();
            _clientFactory = clientFactory;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_checked) return;
            _checked = true;

            if (DeviceInfo.Platform == DevicePlatform.WinUI || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
            {
                bool hasActiveSession = await TryAutoLogin();

                if (hasActiveSession)
                {
                    // MainPage will naturally take over and trigger the OTP timer if the context changed.
                    Application.Current.MainPage = new NavigationPage(new MainPage(_clientFactory));
                    return;
                }
            }

            StartPageLayout.IsVisible = true;
        }

        private async Task<bool> TryAutoLogin()
        {
            try
            {
                using var client = _clientFactory.CreateClient("ZeroTrustAPI");

                string realIp = await DeviceInfoHelper.GetPublicIpAsync();
                string realLocation = await DeviceInfoHelper.GetLocationAsync();
                string realDevice = DeviceInfoHelper.GetDeviceModel();

                var request = new
                {
                    DeviceId = realDevice,
                    Action = "none",
                    Location = realLocation,
                    RealIp = realIp
                };

                var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

                // Check posture against the zero-trust backend
                var response = await client.PostAsync("api/zerotrust/secure", content);

                if (response.IsSuccessStatusCode)
                {
                    // If we get a 200 OK, the JWT is still valid and the server recognizes the user.
                    // We return true to route to MainPage. 
                    // Note: If result.OtpRequired is true, MainPage's OnAppearing will handle showing the OTP UI.
                    return true;
                }

                // If response is 401 Unauthorized or another error, the session is dead.
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Auto-login check failed: {ex.Message}");
                return false;
            }
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            //await Navigation.PushAsync(new LoginPage());

            Application.Current.MainPage = new NavigationPage(new LoginPage(_clientFactory));
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            //await Navigation.PushAsync(new RegisterPage());
            Application.Current.MainPage = new NavigationPage(new RegisterPage(_clientFactory));
        }
    }
}
