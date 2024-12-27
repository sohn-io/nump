using Microsoft.EntityFrameworkCore;
using nump.Components.Classes;
namespace nump.Components.Database;

public partial class NumpContext : DbContext
{
        public NumpContext()
        {
        }

        public NumpContext(DbContextOptions<NumpContext> options) : base(options)
        {
            
        }

        partial void OnModelBuilding(ModelBuilder builder);

        protected override void OnModelCreating(ModelBuilder builder)
        {
            this.OnModelBuilding(builder);
        }

        public DbSet<NumpInstructionSet> Tasks {get; set;}
        public DbSet<NotificationData> Notifications {get; set;}
        public DbSet<IngestData> IngestData {get; set;}
        public DbSet<LocationMap> LocationMaps {get; set;}
        public DbSet<UserCreationLog> UserCreationLogs {get; set;}
        public DbSet<UserUpdateLog> UserUpdateLogs {get; set;}
        public DbSet<TaskLog> TaskLogs {get; set;}
        public DbSet<NotificationLog> NotificationLogs {get; set;}
        public DbSet<Setting> Settings {get; set;}
}