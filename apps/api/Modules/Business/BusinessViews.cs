using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Infrastructure.Persistence;

namespace Yanyun.Api.Modules.Business;

public sealed class BusinessViews(YanyunDbContext database)
{
    private Dictionary<Guid, Yanyun.Api.Modules.Users.User>? _users;
    private ILookup<Guid, GameProfile>? _profiles;
    private Dictionary<Guid, Guild>? _guilds;
    private Dictionary<Guid, int>? _memberCounts;

    public async Task LoadUsersAsync(IEnumerable<Guid> userIds, CancellationToken token)
    {
        var ids = userIds.Distinct().ToArray();
        _users = await database.Users.AsNoTracking().Where(user => ids.Contains(user.Id)).ToDictionaryAsync(user => user.Id, token);
        _profiles = (await database.Profiles.AsNoTracking().Where(profile => ids.Contains(profile.UserId) && profile.DeletedAt == null)
            .OrderBy(profile => profile.CreatedAt).ThenBy(profile => profile.Id).ToArrayAsync(token)).ToLookup(profile => profile.UserId);
    }
    public async Task LoadGuildsAsync(IEnumerable<Guid> guildIds, CancellationToken token)
    {
        var ids = guildIds.Distinct().ToArray();
        _guilds = await database.Guilds.AsNoTracking().Where(guild => ids.Contains(guild.Id)).ToDictionaryAsync(guild => guild.Id, token);
        _memberCounts = await database.Memberships.Where(member => ids.Contains(member.GuildId)).GroupBy(member => member.GuildId)
            .Select(group => new { Id = group.Key, Count = group.Count() }).ToDictionaryAsync(group => group.Id, group => group.Count, token);
    }
    public async Task<GameProfile?> DefaultProfileAsync(Guid userId, CancellationToken token)
    {
        if (_users is not null && _profiles is not null && _users.TryGetValue(userId, out var loaded))
            return _profiles[userId].OrderByDescending(profile => profile.Id == loaded.DefaultProfileId).FirstOrDefault();
        var chosen = await database.Users.Where(user => user.Id == userId).Select(user => user.DefaultProfileId).SingleAsync(token);
        return await database.Profiles.Where(profile => profile.UserId == userId && profile.DeletedAt == null)
            .OrderByDescending(profile => profile.Id == chosen).ThenBy(profile => profile.CreatedAt).ThenBy(profile => profile.Id).FirstOrDefaultAsync(token);
    }
    public static ProfileResponse Profile(GameProfile profile, Guid? defaultId) => new(
        profile.Id, profile.UserId, profile.Name, profile.Server, profile.Power, profile.Styles,
        System.Text.Json.JsonSerializer.Deserialize<OnlineSlot[]>(profile.OnlineSlotsJson, BusinessJson.Options)!, profile.Bio,
        profile.Id == defaultId, profile.CreatedAt, profile.UpdatedAt);
    public static ProfileSummaryResponse Summary(GameProfile profile) => new(
        profile.Id, profile.UserId, profile.Name, profile.Server, profile.Power, profile.Styles);
    public async Task<MeResponse> MeAsync(Guid userId, CancellationToken token)
    {
        var user = await database.Users.SingleOrDefaultAsync(user => user.Id == userId, token) ?? throw BusinessOperations.Missing();
        var profile = await DefaultProfileAsync(userId, token);
        var membership = await database.Memberships.SingleOrDefaultAsync(member => member.UserId == userId, token);
        return new(user.Id, user.Nickname, user.Email, user.EmailVerifiedAt, user.EmailVerifiedAt is null,
            profile is null ? null : Profile(profile, profile.Id), membership is null ? null : await MembershipAsync(membership, token));
    }
    public async Task<MembershipResponse> MembershipAsync(Membership member, CancellationToken token)
    {
        var guild = await database.Guilds.SingleAsync(guild => guild.Id == member.GuildId, token);
        return new(member.Id, member.GuildId, guild.Name, member.Role, member.JoinedAt, guild.Version);
    }
    public async Task<GuildResponse> GuildAsync(Guild guild, CancellationToken token) => new(
        guild.Id, guild.Name, guild.Server, guild.Intro, guild.RecruitRequirements, guild.Recruiting,
        _memberCounts is not null ? _memberCounts.GetValueOrDefault(guild.Id) : await database.Memberships.CountAsync(member => member.GuildId == guild.Id, token), guild.Version);
    public async Task<MemberResponse> MemberAsync(Membership member, CancellationToken token)
    {
        var user = _users is not null && _users.TryGetValue(member.UserId, out var loaded) ? loaded : await database.Users.SingleAsync(user => user.Id == member.UserId, token);
        var profiles = _profiles is not null ? _profiles[user.Id].ToList() : await database.Profiles.Where(profile => profile.UserId == user.Id && profile.DeletedAt == null)
            .OrderBy(profile => profile.CreatedAt).ThenBy(profile => profile.Id).ToListAsync(token);
        var profile = await DefaultProfileAsync(user.Id, token);
        return new(member.Id, member.UserId, user.Nickname, member.Role, member.JoinedAt,
            profile is null ? null : Summary(profile), profiles.Select(Summary).ToArray());
    }
    public async Task<ApplicationResponse> ApplicationAsync(GuildApplication application, bool admin, CancellationToken token)
    {
        var guild = _guilds is not null && _guilds.TryGetValue(application.GuildId, out var loaded) ? loaded : await database.Guilds.SingleAsync(guild => guild.Id == application.GuildId, token);
        var profile = admin ? await DefaultProfileAsync(application.UserId, token) : null;
        return new(application.Id, application.GuildId, guild.Name, application.UserId,
            application.Message, application.Status, application.CreatedAt, application.HandledAt,
            profile is null ? null : Summary(profile));
    }
    public async Task<InvitationResponse> InvitationAsync(Invitation invitation, CancellationToken token)
    {
        var guild = _guilds is not null && _guilds.TryGetValue(invitation.GuildId, out var loaded) ? loaded : await database.Guilds.SingleAsync(guild => guild.Id == invitation.GuildId, token);
        return new(invitation.Id, invitation.GuildId, guild.Name, invitation.Type,
            invitation.TargetUserId, invitation.ExpiresAt, invitation.MaxUses, invitation.UsedCount,
            invitation.Status == "pending" && invitation.ExpiresAt <= DateTimeOffset.UtcNow ? "expired" : invitation.Status,
            invitation.CreatedAt);
    }
    public static TemplateResponse Template(DungeonTemplate template) => new(template.Id, template.GuildId, template.Name,
        template.NeedTank, template.NeedHealer, template.NeedDps, template.DefaultDurationMin, template.Version, template.ArchivedAt);

    public async Task<EventResponse> EventAsync(RaidEvent raid, Guid viewer, CancellationToken token)
        => (await EventsAsync([raid], viewer, token))[0];

    public async Task<EventResponse[]> EventsAsync(IReadOnlyCollection<RaidEvent> raids, Guid viewer, CancellationToken token)
    {
        var guildIds = raids.Select(raid => raid.GuildId).Distinct().ToArray();
        var eventIds = raids.Select(raid => raid.Id).ToArray();
        var members = await database.Memberships.Where(member => member.UserId == viewer && guildIds.Contains(member.GuildId)).ToDictionaryAsync(member => member.GuildId, token);
        var assignments = await database.Assignments.Where(assignment => eventIds.Contains(assignment.EventId))
            .OrderBy(assignment => assignment.CreatedAt).ThenBy(assignment => assignment.Id).ToArrayAsync(token);
        var profileIds = assignments.Select(assignment => assignment.ProfileId).Distinct().ToArray();
        var profiles = await database.Profiles.Where(profile => profileIds.Contains(profile.Id)).ToDictionaryAsync(profile => profile.Id, token);
        var grouped = assignments.ToLookup(assignment => assignment.EventId);
        var now = DateTimeOffset.UtcNow;
        return raids.Select(raid => BuildEvent(raid, viewer, members.GetValueOrDefault(raid.GuildId), grouped[raid.Id].ToArray(), profiles, now)).ToArray();
    }

    private static EventResponse BuildEvent(RaidEvent raid, Guid viewer, Membership? member, Assignment[] assignments,
        IReadOnlyDictionary<Guid, GameProfile> profiles, DateTimeOffset now)
    {
        var views = new List<(Assignment Entity, AssignmentResponse View)>();
        foreach (var assignment in assignments)
        {
            var profile = profiles[assignment.ProfileId];
            views.Add((assignment, new(assignment.Id, assignment.UserId, assignment.MembershipId,
                Summary(profile), assignment.SlotRole, assignment.Status, assignment.CreatedAt,
                assignment.Status == "waitlist" ? (int?)assignments.Count(other =>
                    other.Status == "waitlist" && other.SlotRole == assignment.SlotRole &&
                    (other.CreatedAt < assignment.CreatedAt || other.CreatedAt == assignment.CreatedAt && other.Id.CompareTo(assignment.Id) <= 0)) : null)));
        }
        var locked = raid.StartsAt <= now || raid.Status == "cancelled";
        var mine = views.FirstOrDefault(item => item.Entity.UserId == viewer);
        return new(raid.Id, raid.GuildId, raid.TemplateId, raid.TemplateName, raid.Title, raid.StartsAt,
            raid.StartsAt.AddMinutes(raid.DurationMin), raid.DurationMin, raid.SignupMode,
            raid.Status == "published" && raid.StartsAt.AddMinutes(raid.DurationMin) <= now ? "finished" : raid.Status,
            raid.Note, raid.Version, raid.ReminderEnabled, raid.RemindMinutes, locked,
            new(raid.NeedTank, raid.NeedHealer, raid.NeedDps),
            views.Where(item => item.Entity.Status == "confirmed").Select(item => item.View).ToArray(),
            views.Where(item => item.Entity.Status == "waitlist").Select(item => item.View).ToArray(), mine.View,
            !locked && member is not null && raid.Status == "published" && raid.SignupMode == "open" && mine.Entity is null,
            !locked && mine.Entity is not null, !locked && member is { Role: "owner" or "admin" },
            locked ? "场次已开始或已取消" : member is null ? "仅百业成员可操作" : null);
    }
}
