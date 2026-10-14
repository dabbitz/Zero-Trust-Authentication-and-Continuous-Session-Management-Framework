using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ZeroTrustClient
{
    public partial class RegisterPage : ContentPage
    {
        private readonly IHttpClientFactory _clientFactory;
        public RegisterPage(IHttpClientFactory clientFactory)
        {
            InitializeComponent();
            _clientFactory = clientFactory;
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(UsernameEntry.Text) ||
                string.IsNullOrWhiteSpace(PasswordEntry.Text))
            {
                await DisplayAlertAsync("Error", "Please enter username and password", "OK");
                return;
            }

            using var client = _clientFactory.CreateClient("ZeroTrustAPI");

            var request = new
            {
                Username = UsernameEntry.Text,
                Password = PasswordEntry.Text
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"
            );

            var response = await client.PostAsync("api/zerotrust/register", content);

            if (response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync("Success", "User registered successfully!", "OK");

                // Navigate back to login
                Application.Current.MainPage = new NavigationPage(new LoginPage(_clientFactory));
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                await DisplayAlertAsync("Error", error, "OK");
            }
        }

        private async void OnGoBackClicked(object sender, EventArgs e)
        {
            Application.Current.MainPage = new NavigationPage(new StartPage(_clientFactory));
        }
    }
}
