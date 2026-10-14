using Microsoft.Maui.Devices.Sensors;
using System.Net.Http;
using System.Threading.Tasks;

namespace ZeroTrustClient.Services
{
    public static class DeviceInfoHelper
    {
        // Fetches the public IP address
        public static async Task<string> GetPublicIpAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                return await client.GetStringAsync("https://api.ipify.org");
            }
            catch
            {
                return "Unknown IP";
            }
        }

        // Fetches the city/province location

        public static async Task<string> GetLocationAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

                    if (status != PermissionStatus.Granted)
                        return "Location Denied";
                }

                var request = new GeolocationRequest(
                    GeolocationAccuracy.High,
                    TimeSpan.FromSeconds(10));

                var location = await Geolocation.Default.GetLocationAsync(request);

                if (location == null)
                    return "Location Null";

                Console.WriteLine($"Lat: {location.Latitude}");
                Console.WriteLine($"Lon: {location.Longitude}");

                // Return coordinates directly
                return $"{location.Latitude}, {location.Longitude}";
            }
            catch (FeatureNotEnabledException)
            {
                return "Location Disabled";
            }
            catch (PermissionException)
            {
                return "Permission Error";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public static string GetDeviceModel() => DeviceInfo.Model;

        public static DateTime GetLocalHour() => DateTime.Now;
    }
}