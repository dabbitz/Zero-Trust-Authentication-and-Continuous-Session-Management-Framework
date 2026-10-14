using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeroTrustAPI.Models;

namespace ZeroTrustAPI.Services
{
    public class SessionCleanupService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // This loop runs continuously in the background while the API is alive
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // Find all user IDs where the session has been inactive for 10 or more minutes
                var expiredUserIds = SessionStore.Sessions
                    .Where(kvp => (now - kvp.Value.LastActivity).TotalMinutes >= 10)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var userId in expiredUserIds)
                {
                    // Wipe the session from memory
                    SessionStore.Sessions.Remove(userId);
                    System.Diagnostics.Debug.WriteLine($"[CLEANUP] Removed expired session for User {userId}");
                }

                // Pause the worker for 1 minute before sweeping again to save CPU
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}