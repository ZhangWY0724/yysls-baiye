using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Infrastructure.Persistence;

namespace Yanyun.Api.Modules.Business;

public sealed class CleanupService(YanyunDbContext database, NotificationPublisher publisher)
{
    // 解散百业：硬删除全部关联数据后，给除操作人以外的成员留一条不挂靠百业的通知，百业删除后仍可见。
    public async Task GuildAsync(Guid guildId, Guid initiatedBy, CancellationToken token)
    {
        var name = await database.Guilds.Where(guild => guild.Id == guildId).Select(guild => guild.Name).SingleAsync(token);
        var recipients = await database.Memberships.Where(member => member.GuildId == guildId).Select(member => member.UserId).ToArrayAsync(token);
        foreach (var user in recipients) publisher.Queue(user, "deleted");
        var events = await database.Events.Where(raid => raid.GuildId == guildId).Select(raid => raid.Id).ToArrayAsync(token);
        await EventsAsync(events, token);
        var invitations = await database.Invitations.Where(invitation => invitation.GuildId == guildId).Select(invitation => invitation.Id).ToArrayAsync(token);
        await database.Set<InvitationAcceptance>().Where(acceptance => invitations.Contains(acceptance.InvitationId)).ExecuteDeleteAsync(token);
        await database.Invitations.Where(invitation => invitation.GuildId == guildId).ExecuteDeleteAsync(token);
        await database.Applications.Where(application => application.GuildId == guildId).ExecuteDeleteAsync(token);
        await database.Templates.Where(template => template.GuildId == guildId).ExecuteDeleteAsync(token);
        await ScopeAsync(guildId, null, token);
        await database.Memberships.Where(member => member.GuildId == guildId).ExecuteDeleteAsync(token);
        await database.Guilds.Where(guild => guild.Id == guildId).ExecuteDeleteAsync(token);
        database.ChangeTracker.Clear();
        foreach (var user in recipients.Where(user => user != initiatedBy))
        {
            database.Notifications.Add(NotificationFactory.Create(user, null, null, "guild.dissolved", "百业已解散",
                $"「{name}」已解散，你在该百业的排班和记录已删除", new { Kind = "guild" }));
            publisher.Queue(user, "created");
        }
        // 立即保存：注销等后续清理会清空变更跟踪，未保存的通知会丢失。
        await database.SaveChangesAsync(token);
    }
    // 被移出的成员单独留一条本人通知，同样不挂靠百业，避免随百业后续清理被删除。
    public void NotifyRemoved(Guid userId, string guildName)
    {
        database.Notifications.Add(NotificationFactory.Create(userId, null, null, "member.removed", "你已被移出百业",
            $"你已被移出「{guildName}」，你在该百业的排班和记录已删除", new { Kind = "guild" }));
        publisher.Queue(userId, "created");
    }
    public async Task MemberAsync(Membership member, CancellationToken token)
    {
        var userId = member.UserId;
        var guildId = member.GuildId;
        var affected = await database.Assignments.Where(assignment => assignment.MembershipId == member.Id || assignment.AssignedBy == userId)
            .Select(assignment => assignment.EventId).Distinct().ToArrayAsync(token);
        var templates = await database.Templates.Where(template => template.GuildId == guildId && template.CreatedBy == userId).Select(template => template.Id).ToArrayAsync(token);
        var events = await database.Events.Where(raid => raid.GuildId == guildId && (raid.CreatedBy == userId || templates.Contains(raid.TemplateId))).Select(raid => raid.Id).ToArrayAsync(token);
        await EventsAsync(events, token);
        await database.Assignments.Where(assignment => assignment.MembershipId == member.Id || assignment.AssignedBy == userId).ExecuteDeleteAsync(token);
        var invitations = await database.Invitations.Where(invitation => invitation.GuildId == guildId &&
            (invitation.CreatedBy == userId || invitation.TargetUserId == userId)).Select(invitation => invitation.Id).ToArrayAsync(token);
        await database.Set<InvitationAcceptance>().Where(acceptance => invitations.Contains(acceptance.InvitationId) || acceptance.MembershipId == member.Id).ExecuteDeleteAsync(token);
        await database.Invitations.Where(invitation => invitations.Contains(invitation.Id)).ExecuteDeleteAsync(token);
        await database.Applications.Where(application => application.GuildId == guildId && (application.UserId == userId || application.HandledBy == userId)).ExecuteDeleteAsync(token);
        await database.Templates.Where(template => templates.Contains(template.Id)).ExecuteDeleteAsync(token);
        await ScopeAsync(guildId, userId, token);
        await database.Memberships.Where(row => row.Id == member.Id).ExecuteDeleteAsync(token);
        database.ChangeTracker.Clear();
        foreach (var eventId in affected.Except(events)) await PromoteAsync(eventId, token);
        publisher.Queue(userId, "deleted");
        var others = await database.Memberships.Where(row => row.GuildId == guildId).Select(row => row.UserId).ToArrayAsync(token);
        foreach (var user in others) publisher.Queue(user, "deleted");
    }
    public async Task AccountAsync(Guid userId, CancellationToken token)
    {
        var owned = await database.Guilds.Where(guild => guild.OwnerUserId == userId).Select(guild => guild.Id).ToArrayAsync(token);
        foreach (var guildId in owned) await GuildAsync(guildId, userId, token);
        var member = await database.Memberships.SingleOrDefaultAsync(member => member.UserId == userId, token);
        if (member is not null) await MemberAsync(member, token);
        var invitations = await database.Invitations.Where(invitation => invitation.CreatedBy == userId || invitation.TargetUserId == userId).Select(invitation => invitation.Id).ToArrayAsync(token);
        await database.Set<InvitationAcceptance>().Where(acceptance => acceptance.UserId == userId || invitations.Contains(acceptance.InvitationId)).ExecuteDeleteAsync(token);
        await database.Invitations.Where(invitation => invitations.Contains(invitation.Id)).ExecuteDeleteAsync(token);
        await database.Applications.Where(application => application.UserId == userId || application.HandledBy == userId).ExecuteDeleteAsync(token);
        await ScopeAsync(null, userId, token);
        await database.Users.Where(user => user.Id == userId).ExecuteUpdateAsync(update => update.SetProperty(user => user.DefaultProfileId, (Guid?)null), token);
        await database.Profiles.Where(profile => profile.UserId == userId).ExecuteDeleteAsync(token);
        await database.Users.Where(user => user.Id == userId).ExecuteDeleteAsync(token);
        database.ChangeTracker.Clear();
    }
    private async Task EventsAsync(Guid[] eventIds, CancellationToken token)
    {
        await database.Assignments.Where(assignment => eventIds.Contains(assignment.EventId)).ExecuteDeleteAsync(token);
        await database.Notifications.Where(row => row.EventId != null && eventIds.Contains(row.EventId.Value)).ExecuteDeleteAsync(token);
        await database.Idempotency.Where(row => row.EventId != null && eventIds.Contains(row.EventId.Value)).ExecuteDeleteAsync(token);
        await database.Audit.Where(row => row.EventId != null && eventIds.Contains(row.EventId.Value)).ExecuteDeleteAsync(token);
        await database.Events.Where(raid => eventIds.Contains(raid.Id)).ExecuteDeleteAsync(token);
    }
    private async Task ScopeAsync(Guid? guildId, Guid? userId, CancellationToken token)
    {
        var recipients = await database.Notifications.Where(row =>
            (guildId == null || row.GuildId == guildId) && (userId == null || row.UserId == userId || row.RelatedUserIds.Contains(userId.Value)))
            .Select(row => row.UserId).Distinct().ToArrayAsync(token);
        foreach (var user in recipients) publisher.Queue(user, "deleted");
        await database.Notifications.Where(row => (guildId == null || row.GuildId == guildId) &&
            (userId == null || row.UserId == userId || row.RelatedUserIds.Contains(userId.Value))).ExecuteDeleteAsync(token);
        await database.Idempotency.Where(row => (guildId == null || row.GuildId == guildId) &&
            (userId == null || row.UserId == userId || row.RelatedUserIds.Contains(userId.Value))).ExecuteDeleteAsync(token);
        await database.Audit.Where(row => (guildId == null || row.GuildId == guildId) &&
            (userId == null || row.ActorId == userId || row.RelatedUserIds.Contains(userId.Value))).ExecuteDeleteAsync(token);
    }
    public async Task PromoteAsync(Guid eventId, CancellationToken token)
    {
        var raid = await database.Events.SingleOrDefaultAsync(raid => raid.Id == eventId, token);
        if (raid is null || raid.Status != "published" || raid.StartsAt <= DateTimeOffset.UtcNow) return;
        foreach (var (role, capacity) in new[] { ("tank", raid.NeedTank), ("healer", raid.NeedHealer), ("dps", raid.NeedDps) })
        {
            var count = await database.Assignments.CountAsync(assignment => assignment.EventId == eventId && assignment.SlotRole == role && assignment.Status == "confirmed", token);
            var waiting = await database.Assignments.Where(assignment => assignment.EventId == eventId && assignment.SlotRole == role && assignment.Status == "waitlist")
                .OrderBy(assignment => assignment.CreatedAt).ThenBy(assignment => assignment.Id).ToListAsync(token);
            foreach (var assignment in waiting)
            {
                if (count >= capacity) break;
                if (!await database.Users.AnyAsync(user => user.Id == assignment.UserId && user.Status == "active" && user.EmailVerifiedAt != null, token)) continue;
                if (await HasConflictAsync(assignment.UserId, raid, token)) continue;
                assignment.Status = "confirmed";
                count++;
                database.Notifications.Add(NotificationFactory.Create(assignment.UserId, raid.GuildId, raid.Id, "assignment.promoted",
                    "候补已递补", $"你已进入「{raid.Title}」正式队伍", new { Kind = "raid_event", raid.GuildId, EventId = raid.Id }));
                publisher.Queue(assignment.UserId, "created");
            }
        }
        raid.Version++;
        await database.SaveChangesAsync(token);
    }
    public Task<bool> HasConflictAsync(Guid userId, RaidEvent raid, CancellationToken token) =>
        (from assignment in database.Assignments join other in database.Events on assignment.EventId equals other.Id
         where assignment.UserId == userId && assignment.Status == "confirmed" && other.Id != raid.Id && other.Status == "published" &&
             other.StartsAt < raid.StartsAt.AddMinutes(raid.DurationMin) && other.StartsAt.AddMinutes(other.DurationMin) > raid.StartsAt
         select assignment.Id).AnyAsync(token);
}
