using System;
using System.Collections.Generic;
using System.Text;

namespace ZeroTrustClient.Services
{
    // InactivityService.cs
    internal class InactivityService
    {
        private static IDispatcherTimer _timer;
        private static IHttpClientFactory _clientFactory; // 1. Add this field
        private const int TimeoutSeconds = 45;

        // 2. Update Initialize to accept the factory
        public static void Initialize(IDispatcher dispatcher, IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory; // Save it for later use

            if (_timer != null) return;

            _timer = dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(TimeoutSeconds);
            _timer.Tick += OnTimerTick;
        }

        public static void Start() => _timer?.Start();
        public static void Stop() => _timer?.Stop();

        public static void Reset()
        {
            if (_timer != null && _timer.IsRunning)
            {
                _timer.Stop();
                _timer.Start();
            }
        }

        private static async void OnTimerTick(object sender, EventArgs e)
        {
            Stop();

            // 3. Use the factory for the timeout notification
            var api = new ApiService();
            await api.NotifyTimeoutAsync();

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Application.Current.MainPage.DisplayAlertAsync("Session Expired", "You have been inactive for 45 seconds.", "OK");

                // 4. Pass the factory to MainPage when redirecting
                Application.Current.MainPage = new NavigationPage(new MainPage(_clientFactory));
            });
        }
    }
}
