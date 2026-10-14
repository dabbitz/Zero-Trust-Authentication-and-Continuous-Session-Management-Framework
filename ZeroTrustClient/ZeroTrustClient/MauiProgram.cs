using Microsoft.Extensions.Logging;
using ZeroTrustClient.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroTrustClient
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddTransient<AuthInterceptor>();

            builder.Services.AddHttpClient("ZeroTrustAPI", client =>
            {
                string baseUrl = DeviceInfo.Platform == DevicePlatform.Android
                    ? "http://10.0.2.2:5193/"
                    : "https://localhost:7092/";

                client.BaseAddress = new Uri(baseUrl);
            })
            .AddHttpMessageHandler<AuthInterceptor>();

            builder.Services.AddTransient<StartPage>();
            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<RegisterPage>();
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<AppPage>();
            builder.Services.AddTransient<PhonePage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
