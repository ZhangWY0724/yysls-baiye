using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Modules.Business;
using Yanyun.Api.Modules.Users;

namespace Yanyun.Api.Infrastructure.Persistence;

public static class BusinessModel
{
    public static void Configure(ModelBuilder model)
    {
        var tables = new Dictionary<Type, string>
        {
            [typeof(GameProfile)] = "game_profiles", [typeof(Guild)] = "guilds",
            [typeof(Membership)] = "memberships", [typeof(GuildApplication)] = "applications",
            [typeof(Invitation)] = "invitations", [typeof(InvitationAcceptance)] = "invitation_acceptances",
            [typeof(DungeonTemplate)] = "dungeon_templates", [typeof(RaidEvent)] = "raid_events",
            [typeof(Assignment)] = "assignments", [typeof(Notification)] = "notifications",
            [typeof(OutboxEvent)] = "outbox_events", [typeof(IdempotencyRecord)] = "idempotency_records",
            [typeof(AuditLog)] = "audit_logs"
        };
        foreach (var (type, table) in tables)
        {
            var entity = model.Entity(type);
            entity.ToTable(table);
            entity.HasKey("Id");
            entity.Property("Id").ValueGeneratedNever();
            foreach (var property in entity.Metadata.GetProperties())
            {
                entity.Property(property.Name).HasColumnName(JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
                if (property.Name.EndsWith("Json", StringComparison.Ordinal))
                    entity.Property(property.Name).HasColumnType("jsonb");
            }
        }
        model.Entity<GameProfile>().HasAlternateKey(profile => new { profile.Id, profile.UserId });
        model.Entity<GameProfile>().HasOne<User>().WithMany().HasForeignKey(profile => profile.UserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<GameProfile>().ToTable("game_profiles", table =>
        {
            table.HasCheckConstraint("ck_profiles_server", "server IN ('official','channel')");
            table.HasCheckConstraint("ck_profiles_power", "power >= 0");
            table.HasCheckConstraint("ck_profiles_styles", "cardinality(styles) BETWEEN 1 AND 20");
        });
        model.Entity<Guild>().Property(guild => guild.Version).IsConcurrencyToken();
        model.Entity<Guild>().HasOne<User>().WithMany().HasForeignKey(guild => guild.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Guild>().ToTable("guilds", table =>
        {
            table.HasCheckConstraint("ck_guild_server", "server IN ('official','channel')");
            table.HasCheckConstraint("ck_guild_reminder", "remind_minutes BETWEEN 1 AND 1440");
        });
        model.Entity<Membership>().HasOne<Guild>().WithMany().HasForeignKey(member => member.GuildId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Membership>().HasOne<User>().WithMany().HasForeignKey(member => member.UserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Membership>().HasIndex(member => member.UserId).IsUnique();
        model.Entity<Membership>().HasIndex(member => member.GuildId).IsUnique().HasFilter("role = 'owner'");
        model.Entity<Membership>().ToTable("memberships", table => table.HasCheckConstraint("ck_members_role", "role IN ('owner','admin','member')"));
        model.Entity<GuildApplication>().HasOne<Guild>().WithMany().HasForeignKey(application => application.GuildId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<GuildApplication>().HasOne<User>().WithMany().HasForeignKey(application => application.UserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<GuildApplication>().HasIndex(application => new { application.GuildId, application.UserId }).IsUnique().HasFilter("status = 'pending'");
        model.Entity<Invitation>().HasOne<Guild>().WithMany().HasForeignKey(invitation => invitation.GuildId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Invitation>().HasOne<User>().WithMany().HasForeignKey(invitation => invitation.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Invitation>().HasOne<User>().WithMany().HasForeignKey(invitation => invitation.TargetUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Invitation>().HasIndex(invitation => invitation.CodeHash).IsUnique().HasFilter("code_hash IS NOT NULL");
        model.Entity<Invitation>().ToTable("invitations", table => table.HasCheckConstraint("ck_invitation_uses", "used_count >= 0 AND max_uses > 0 AND used_count <= max_uses"));
        model.Entity<InvitationAcceptance>().HasOne<Invitation>().WithMany().HasForeignKey(acceptance => acceptance.InvitationId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<InvitationAcceptance>().HasOne<Membership>().WithMany().HasForeignKey(acceptance => acceptance.MembershipId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<InvitationAcceptance>().HasIndex(acceptance => new { acceptance.InvitationId, acceptance.MembershipId }).IsUnique();
        model.Entity<DungeonTemplate>().HasOne<Guild>().WithMany().HasForeignKey(template => template.GuildId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<DungeonTemplate>().HasOne<User>().WithMany().HasForeignKey(template => template.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        model.Entity<DungeonTemplate>().Property(template => template.Version).IsConcurrencyToken();
        model.Entity<DungeonTemplate>().ToTable("dungeon_templates", table => table.HasCheckConstraint("ck_template_capacity",
            "need_tank BETWEEN 0 AND 10 AND need_healer BETWEEN 0 AND 10 AND need_dps BETWEEN 0 AND 10 AND (need_tank+need_healer+need_dps) BETWEEN 1 AND 10 AND default_duration_min BETWEEN 1 AND 1440"));
        model.Entity<RaidEvent>().HasOne<Guild>().WithMany().HasForeignKey(raid => raid.GuildId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<RaidEvent>().HasOne<DungeonTemplate>().WithMany().HasForeignKey(raid => raid.TemplateId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<RaidEvent>().HasOne<User>().WithMany().HasForeignKey(raid => raid.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        model.Entity<RaidEvent>().Property(raid => raid.Version).IsConcurrencyToken();
        model.Entity<RaidEvent>().HasIndex(raid => new { raid.GuildId, raid.StartsAt });
        model.Entity<RaidEvent>().ToTable("raid_events", table => table.HasCheckConstraint("ck_event_capacity",
            "need_tank BETWEEN 0 AND 10 AND need_healer BETWEEN 0 AND 10 AND need_dps BETWEEN 0 AND 10 AND (need_tank+need_healer+need_dps) BETWEEN 1 AND 10 AND duration_min BETWEEN 1 AND 1440 AND remind_minutes BETWEEN 1 AND 1440"));
        model.Entity<Assignment>().HasOne<RaidEvent>().WithMany().HasForeignKey(assignment => assignment.EventId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Assignment>().HasOne<Membership>().WithMany().HasForeignKey(assignment => assignment.MembershipId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Assignment>().HasOne<GameProfile>().WithMany().HasForeignKey(assignment => new { assignment.ProfileId, assignment.UserId })
            .HasPrincipalKey(profile => new { profile.Id, profile.UserId }).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Assignment>().HasIndex(assignment => new { assignment.EventId, assignment.UserId }).IsUnique();
        model.Entity<Assignment>().HasIndex(assignment => new { assignment.EventId, assignment.Status, assignment.SlotRole, assignment.CreatedAt, assignment.Id });
        model.Entity<Assignment>().ToTable("assignments", table =>
        {
            table.HasCheckConstraint("ck_assignment_role", "slot_role IN ('tank','healer','dps')");
            table.HasCheckConstraint("ck_assignment_status", "status IN ('confirmed','waitlist')");
        });
        model.Entity<Notification>().HasOne<User>().WithMany().HasForeignKey(notification => notification.UserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Notification>().HasIndex(notification => notification.DedupeKey).IsUnique();
        model.Entity<Notification>().HasIndex(notification => new { notification.UserId, notification.CreatedAt, notification.Id });
        model.Entity<Notification>().HasIndex(notification => new { notification.UserId, notification.CreatedAt }).HasFilter("read_at IS NULL");
        model.Entity<OutboxEvent>().HasIndex(outbox => outbox.NextAttemptAt).HasFilter("processed_at IS NULL");
        model.Entity<IdempotencyRecord>().HasIndex(record => new { record.UserId, record.Operation, record.Key }).IsUnique();
        foreach (var type in new[] { typeof(Notification), typeof(OutboxEvent), typeof(IdempotencyRecord), typeof(AuditLog) })
        {
            model.Entity(type).HasIndex("GuildId");
            model.Entity(type).HasIndex("EventId");
            model.Entity(type).HasIndex("RelatedUserIds").HasMethod("gin");
        }
    }
}
