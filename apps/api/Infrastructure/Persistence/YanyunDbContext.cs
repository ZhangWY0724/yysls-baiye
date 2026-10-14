using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Modules.Profiles;
using Yanyun.Api.Modules.Users;
using Yanyun.Api.Modules.Business;

namespace Yanyun.Api.Infrastructure.Persistence;

public sealed class YanyunDbContext(DbContextOptions<YanyunDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ProfileStyleOption> ProfileStyleOptions => Set<ProfileStyleOption>();
    public DbSet<GameProfile> Profiles => Set<GameProfile>();
    public DbSet<Guild> Guilds => Set<Guild>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<GuildApplication> Applications => Set<GuildApplication>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<DungeonTemplate> Templates => Set<DungeonTemplate>();
    public DbSet<RaidEvent> Events => Set<RaidEvent>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();
    public DbSet<IdempotencyRecord> Idempotency => Set<IdempotencyRecord>();
    public DbSet<AuditLog> Audit => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(YanyunDbContext).Assembly);
        BusinessModel.Configure(modelBuilder);
    }
}
