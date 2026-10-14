namespace ZeroTrustAPI.Models
{
    public class DeviceRequest
    {
        public int userId { get; set; }
        public string DeviceId { get; set; }
        public string Action { get; set; }
        public string Location { get; set; }
        public string RealIp { get; set; }
        public DateTime LocalHour { get; set; }
    }
}
