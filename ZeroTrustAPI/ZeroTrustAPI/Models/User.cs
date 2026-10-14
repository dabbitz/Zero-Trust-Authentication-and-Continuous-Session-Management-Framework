using Microsoft.Build.Framework;

namespace ZeroTrustAPI.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        public string Username { get; set; }

        [Required]
        public string PasswordHash { get; set; }
        public List<DeviceAccess> DeviceAccesses { get; set; }
    }
}
