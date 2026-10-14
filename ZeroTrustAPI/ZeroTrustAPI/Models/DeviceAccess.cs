namespace ZeroTrustAPI.Models
{
    public class DeviceAccess
    {
        public int Id { get; set; }

        public int UserId { get; set; }           // FK

        public User User { get; set; }            // Navigation

        public string DeviceModel { get; set; }

        public string Location { get; set; }

        public string IpAddress { get; set; }

        public DateTime AccessTime { get; set; }
    }
}
