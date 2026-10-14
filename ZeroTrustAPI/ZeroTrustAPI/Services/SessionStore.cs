using ZeroTrustAPI.Models;

namespace ZeroTrustAPI.Services
{
    public static class SessionStore
    {
        public static Dictionary<int, SessionContext> Sessions = new();
    }
}