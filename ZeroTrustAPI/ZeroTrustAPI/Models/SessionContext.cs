namespace ZeroTrustAPI.Models
{
    public class SessionContext
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string IpAddress { get; set; }
        public string Location { get; set; }
        public string DeviceId { get; set; }
        public bool OtpRequired { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsAfkExpired { get; set; }
        public bool IsCriticalPending { get; set; }
        public bool IpChanged { get; set; }
    }
}