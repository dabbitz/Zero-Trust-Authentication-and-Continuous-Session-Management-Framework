using CommunityToolkit.Mvvm.Messaging;
using System.Buffers.Text;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ZeroTrustClient.Services;
using static ZeroTrustClient.Services.ApiService;

namespace ZeroTrustClient
{
    public partial class MainPage : ContentPage
    {
        private readonly IHttpClientFactory _clientFactory;
        public static string PendingNavigationIntent = "";
        private static bool _isNavigating = false;

        private OtpTimerDrawable _timerDrawable;
        private double _secondsRemaining = 10;
        private const double MaxTime = 10;

        private IDispatcherTimer _countdownTimer;
        public static bool WasLoggedOut = false;

        public enum AuthState
        {
            FreshLogin,     // just logged in, OTP required
            Unverified,      // token exists but not trusted
            Verified         // fully in app
        }

        public static AuthState CurrentAuthState = AuthState.Unverified;

        public MainPage(IHttpClientFactory clientFactory)
        {
            InitializeComponent();

            _timerDrawable = new OtpTimerDrawable();
            OtpTimerView.Drawable = _timerDrawable;
            _clientFactory = clientFactory;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            InactivityService.Stop();

            var token = await SecureStorage.GetAsync("access_token");
            Debug.WriteLine(token);

            // Critical Action Routing
            if (!string.IsNullOrEmpty(PendingNavigationIntent))
            {
                StatusLabel.Text = "Status: Critical action, authentication required";
                StatusLabel.TextColor = Colors.Orange;
                StartPolling();
                StartCountdownTimer();
                return;
            }

            if (CurrentAuthState == AuthState.FreshLogin)
            {
                StatusLabel.Text = "Status: OTP Required to enter app";
                StatusLabel.TextColor = Colors.Orange;

                StartPolling();
                StartCountdownTimer();
                await GenerateOtpSilently();

                // Important: stop here
                return;
            }

            if (token == null) // debugging
            {
                Debug.WriteLine("Token is null.");
            }
            else
            {
                Debug.WriteLine(token);
            }

            // Has Token (Verify Posture against Server)
            StatusLabel.Text = "Status: Verifying Session Context...";
            StatusLabel.TextColor = Colors.Orange;

            var (sessionValid, sessionMessage) = await VerifySessionPostureAsync(token);

            if (sessionValid)
            {
                StatusLabel.Text = "Status: Authenticated Successfully!";
                StatusLabel.TextColor = Colors.Green;
            }
            else
            {
                StatusLabel.Text = "Status: " + sessionMessage;
                StatusLabel.TextColor = Colors.Red;
                StartPolling();
                StartCountdownTimer();
                await GenerateOtpSilently();
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopEverything(); // Centralized cleanup
        }

        private void StopEverything()
        {
            // 1. Stop the visual/OTP timer
            OtpState.StopRequested = true;
            _countdownTimer?.Stop();
            _countdownTimer = null;
            OtpState.IsRunning = false; // RESET THE GUARD

            // 2. Stop the polling timer
            _timer?.Stop();
            _timer = null;
            _isChecking = false;

            _isGeneratingOtp = false;
            System.Diagnostics.Debug.WriteLine("MainPage: Timers and Polling stopped.");
        }

        private void StartCountdownTimer()
        {
            // Stop any existing timer first to prevent duplicates
            _countdownTimer?.Stop();

            OtpState.IsRunning = true;
            OtpState.StopRequested = false;

            _secondsRemaining = MaxTime; // Reset time to 10s

            _countdownTimer = Dispatcher.CreateTimer();
            _countdownTimer.Interval = TimeSpan.FromMilliseconds(500);

            _countdownTimer.Tick += async (s, e) =>
            {
                if (OtpState.StopRequested)
                {
                    _countdownTimer.Stop();
                    OtpState.IsRunning = false;
                    return;
                }

                _secondsRemaining -= 0.5;

                if (_secondsRemaining <= 0)
                {
                    _secondsRemaining = MaxTime;
                    await GenerateOtpSilently();
                }

                _timerDrawable.Progress = _secondsRemaining / MaxTime;
                OtpTimerView.Invalidate();
            };

            _countdownTimer.Start();
            // Trigger the first OTP immediately so we don't wait 10s
            _ = GenerateOtpSilently();
        }

        private bool IsTokenValid(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();

                if (!handler.CanReadToken(token))
                    return false;

                var jwt = handler.ReadJwtToken(token);

                return jwt.ValidTo > DateTime.UtcNow;
            }
            catch
            {
                return false;
            }
        }

        private async Task<(bool IsValid, string Message)> VerifySessionPostureAsync(string token)
        {
            try
            {
                using var client = _clientFactory.CreateClient("ZeroTrustAPI");

                // Fetch dynamic data
                string realIp = await DeviceInfoHelper.GetPublicIpAsync();
                string realLocation = await DeviceInfoHelper.GetLocationAsync();
                string realDevice = DeviceInfoHelper.GetDeviceModel();

                // Send current context data to the SecureEndpoint
                var request = new
                {
                    DeviceId = realDevice,
                    Action = "none",
                    Location = realLocation,
                    RealIp = realIp
                };

                var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

                var sw = Stopwatch.StartNew();
                var response = await client.PostAsync("api/zerotrust/secure", content);

                sw.Stop();
                Debug.WriteLine($"[PERF] Session verification took {sw.ElapsedMilliseconds} ms");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<ApiService.SecureResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (result?.OtpRequired == true)
                    {
                        return (false, result.Message);
                    }
                    return (true, "Valid");
                }
                return (false, "Session verification failed.");
            }
            catch (Exception)
            {
                return (false, "Network error during posture check.");
            }
        }

        private async void OnCallApiClicked(object sender, EventArgs e)
        {
            var api = new ApiService();
            var result = await api.GetTestAsync();

            if (result == "OTP_REQUIRED")
            {
                StatusLabel.Text = "Status: Re-authentication required";
                StatusLabel.TextColor = Colors.Orange;

                await DisplayAlertAsync("Session Expired", "Please login again.", "OK");
            }
            else
            {
                await DisplayAlertAsync("API Response", result, "OK");
            }
        }

        private IDispatcherTimer _timer;
        private bool _isChecking = false;

        private void StartPolling()
        {
            // If a timer exists, stop it before making a new one
            _timer?.Stop();

            _timer = Application.Current.Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(3);

            _timer.Tick += async (s, e) =>
            {
                await CheckAuthStatus();
            };

            _timer.Start();
        }

        private async Task CheckAuthStatus()
        {
            if (_isChecking) return;
            _isChecking = true;

            try
            {
                using var client = _clientFactory.CreateClient("ZeroTrustAPI");

                var sw = Stopwatch.StartNew();

                var response = await client.GetAsync($"api/zerotrust/status");
                sw.Stop();
                Debug.WriteLine($"[PERF] Polling request took {sw.ElapsedMilliseconds} ms");

                if (response != null && response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    if (!string.IsNullOrEmpty(content))
                    {
                        var result = JsonSerializer.Deserialize<AuthResponse>(
                            content,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (result?.Authenticated == true && !string.IsNullOrEmpty(result.AccessToken))
                        {
                            _timer?.Stop();

                            await SecureStorage.SetAsync("access_token", result.AccessToken);
                            await SecureStorage.SetAsync("refresh_token", result.RefreshToken);

                            // Prevent double navigation
                            if (_isNavigating) return;
                            _isNavigating = true;

                            await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                MainPage.WasLoggedOut = false;
                                StatusLabel.Text = "Status: Authenticated Successfully!";
                                StatusLabel.TextColor = Colors.Green;
                                await Task.Delay(1000);

                                // Read and clear the intent so it doesn't get stuck
                                string intent = PendingNavigationIntent;
                                PendingNavigationIntent = "";

                                if (intent == "ResumeCriticalAction")
                                {
                                    Application.Current.MainPage = new NavigationPage(new AccountDetailsPage(_clientFactory));
                                }
                                else
                                {
                                    // The second route: Standard login, just go to the default menu
                                    Application.Current.MainPage = new NavigationPage(new AppPage(_clientFactory));
                                }

                                _isNavigating = false;
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Polling Error: {ex.Message}");
            }
            finally
            {
                _isChecking = false;
            }
        }

        private async void OnGoToAppPageClicked(object sender, EventArgs e)
        {
            var token = await SecureStorage.GetAsync("access_token");

            if (string.IsNullOrEmpty(token) || !IsTokenValid(token))
            {
                await DisplayAlertAsync("Access Denied", "No valid token found.", "OK");
                return;
            }

            // Ask the server if the session is actually valid
            using var client = _clientFactory.CreateClient("ZeroTrustAPI");

            try
            {
                var response = await client.GetAsync($"api/zerotrust/session-status");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var status = JsonSerializer.Deserialize<SessionStatusResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (status != null && status.IsReady)
                    {
                        // ONLY NOW do we allow entry
                        Application.Current.MainPage = new NavigationPage(new AppPage(_clientFactory));
                    }
                    else
                    {
                        await DisplayAlertAsync("Verification Required", status?.Message ?? "Session not ready.", "OK");
                    }
                }
                else
                {
                    await DisplayAlertAsync("Error", "Could not verify session with server.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Connection Error", "Server is unreachable.", "OK");
            }
        }

        public class SessionStatusResponse { public bool IsReady { get; set; } public string Message { get; set; } }

        private bool _isGeneratingOtp = false;

        private async Task GenerateOtpSilently()
        {
            if (_isGeneratingOtp) return;
            _isGeneratingOtp = true;

            try
            {
                using var client = _clientFactory.CreateClient("ZeroTrustAPI");
                var token = await SecureStorage.GetAsync("access_token");

                Debug.WriteLine($"[OTP REQUEST] (GenerateOtpSilently) Sending request. Token exists: {!string.IsNullOrEmpty(token)}");
                

                // Fetch dynamic data
                string realIp = await DeviceInfoHelper.GetPublicIpAsync();
                string realLocation = await DeviceInfoHelper.GetLocationAsync();
                string realDevice = DeviceInfoHelper.GetDeviceModel();

                var request = new
                {
                    DeviceId = realDevice,
                    RealIp = realIp,
                    Location = realLocation
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json"
                );

                var sw = Stopwatch.StartNew();

                var response = await client.PostAsync("api/zerotrust/generate", content);

                Debug.WriteLine($"[OTP RESPONSE] (GenerateOtpSilently) Status: {response.StatusCode}");

                sw.Stop();
                Debug.WriteLine($"[PERF] OTP request round-trip took {sw.ElapsedMilliseconds} ms");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    var result = JsonSerializer.Deserialize<OtpResponse>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (result != null)
                    {
                        System.Diagnostics.Debug.WriteLine(
                        $"[CLIENT RECEIVED OTP] {result.Otp}, ExpiresAt: {result.ExpiresAt}, LocalTime: {DateTime.UtcNow}"
                        );

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            OtpLabel.Text = result.Otp;

                            // Calculate exactly how many seconds are left based on the server clock
                            var timeLeft = (result.ExpiresAt - DateTime.UtcNow).TotalSeconds;

                            // Snap the visual timer to exactly what the server says
                            if (timeLeft > 0 && timeLeft <= MaxTime)
                            {
                                _secondsRemaining = timeLeft;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OTP Auto Error: {ex.Message}");
            }
            finally
            {
                _isGeneratingOtp = false;
            }
        }

        public class OtpTimerDrawable : IDrawable
        {
            public double Progress { get; set; } = 1.0; // 1.0 is full, 0.0 is empty

            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                float strokeWidth = 8;
                float radius = (Math.Min(dirtyRect.Width, dirtyRect.Height) / 2) - strokeWidth;
                PointF center = new PointF(dirtyRect.Width / 2, dirtyRect.Height / 2);

                canvas.Antialias = true;

                // 1. Draw the background (faded circle)
                canvas.StrokeColor = Colors.Grey.WithAlpha(0.2f);
                canvas.StrokeSize = strokeWidth;
                canvas.DrawCircle(center, radius);

                // 2. Draw the active progress arc
                // Change the color to red when the time is low (less than 20%)
                canvas.StrokeColor = Progress < 0.2 ? Colors.Red : Colors.DeepSkyBlue;
                canvas.StrokeSize = strokeWidth;

                // Calculate end angle (starting from top/270 degrees)
                float endAngle = (float)(360 * Progress);
                canvas.DrawArc(strokeWidth, strokeWidth, dirtyRect.Width - (strokeWidth * 2),
                               dirtyRect.Height - (strokeWidth * 2), 90, 90 - endAngle, true, false);
            }
        }

        public class AuthResponse
        {
            public bool Authenticated { get; set; }
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
        }

        public class OtpResponse
        {
            public string Message { get; set; }
            public string Otp { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        public class ContextAuthRequest
        {
            public string Username { get; set; }
            public string DeviceId { get; set; }
            public string Location { get; set; }
            public string IpAddress { get; set; }
        }

        public class ContextAuthResponse
        {
            public bool RequiresOtp { get; set; }
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
            public string Message { get; set; }
        }

        public static class OtpState
        {
            public static bool IsRunning = false;
            public static bool StopRequested = false;
        }
    }
}