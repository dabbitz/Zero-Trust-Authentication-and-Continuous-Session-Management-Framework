using Microsoft.EntityFrameworkCore;
using ZeroTrustAPI.Models;

namespace ZeroTrustAPI
{
    public class AppDbContext: DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        { }



        public DbSet<User> Users { get; set; }
        public DbSet<DeviceAccess> DeviceAccesses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DeviceAccess>()
                .HasOne(d => d.User)
                .WithMany(u => u.DeviceAccesses)
                .HasForeignKey(d => d.UserId);
        }
    }
}
