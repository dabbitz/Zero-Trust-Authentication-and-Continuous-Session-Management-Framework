using System;
using ZeroTrustClient.Services;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Storage;

namespace ZeroTrustClient
{
    public partial class PhonePage : ContentPage
    {
        private readonly IHttpClientFactory _clientFactory;
        public PhonePage(IHttpClientFactory clientFactory)
        {
            InitializeComponent();
            _clientFactory = clientFactory;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            InactivityService.Stop(); // Stop the 45s countdown, no countdown on the phone page
        }

        public class VerifyRequest
        {
            public string Username { get; set; }
            public string Code { get; set; }
        }

        public class TokenResponse
        {
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
        }

        public class VerifyResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
        }

        private async void OnVerifyClicked(object sender, EventArgs e)
        {
            var code = OtpEntry.Text;

            System.Diagnostics.Debug.WriteLine(
            $"[PHONE INPUT OTP] {code}"
            );

            if (string.IsNullOrWhiteSpace(code))
            {
                await DisplayAlertAsync("Error", "Please enter the OTP", "OK");
                return;
            }

            try
            {
                using var client = _clientFactory.CreateClient("ZeroTrustAPI");

                var requestBody = new VerifyRequest
                {
                    Code = code
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync("api/zerotrust/verify", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var result = JsonSerializer.Deserialize<VerifyResponse>(responseBody, options);

                    // Check for success
                    if (result != null && result.Success)
                    {
                        // The token isn't saved here because the phone is just a verifier.
                        // The PC will retrieve the token via its polling service.
                        await DisplayAlertAsync("Success", result.Message, "OK");
                        await Navigation.PopAsync();
                    }
                    else
                    {
                        await DisplayAlertAsync("Error", "Verification failed on server.", "OK");
                    }
                }
                else
                {
                    var errorDetails = await response.Content.ReadAsStringAsync();
                    await DisplayAlertAsync("Error", $"Invalid OTP or Server Error: {errorDetails}", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Network Error", $"Details: {ex.Message}", "OK");
            }
        }


    }
}