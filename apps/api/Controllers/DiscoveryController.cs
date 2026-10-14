using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Business;

namespace Yanyun.Api.Controllers;

[ApiController, Route("api/v1")]
public sealed class DiscoveryController(YanyunDbContext database, BusinessOperations operations, BusinessViews views) : ControllerBase
{
    [HttpGet("players")]
    [ProducesResponseType<ApiResponse<PagedResponse<PlayerSummaryResponse>>>(200)]
    public async Task<object> Players(CancellationToken token, int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20,
        string? server = null, string? style = null, int? weekday = null,
        [FromQuery(Name = "start_minute")] int? startMinute = null, [FromQuery(Name = "end_minute")] int? endMinute = null)
    {
        operations.RejectUnknownQuery("page", "page_size", "server", "style", "weekday", "start_minute", "end_minute");
        BusinessOperations.Pagination(page, pageSize);
        if (server is not null and not ("official" or "channel")) throw BusinessOperations.Invalid("区服不正确");
        if (weekday is not null || startMinute is not null || endMinute is not null)
            if (weekday is null or < 1 or > 7 || startMinute is null or < 0 or > 1439 || endMinute is null or < 1 or > 1440 || startMinute >= endMinute)
                throw BusinessOperations.Invalid("在线时间筛选需要完整有效时段");
        var profiles = database.Profiles.Where(profile => profile.DeletedAt == null && (server == null || profile.Server == server) &&
            (style == null || profile.Styles.Contains(style)));
        if (weekday is not null)
        {
            // 时间边界在 PostgreSQL JSONB 中匹配，不先拉全量数据到内存。
            profiles = database.Profiles.FromSqlInterpolated($"""
                SELECT * FROM game_profiles p WHERE deleted_at IS NULL AND EXISTS (
                  SELECT 1 FROM jsonb_array_elements(p.online_slots_json) s
                  WHERE (s->>'weekday')::int = {weekday.Value} AND (s->>'start_minute')::int < {endMinute!.Value}
                    AND (s->>'end_minute')::int > {startMinute!.Value})
                """).Where(profile => (server == null || profile.Server == server) && (style == null || profile.Styles.Contains(style)));
        }
        var matchingUsers = profiles.Select(profile => profile.UserId);
        var users = database.Users.Where(user => user.Status == "active" && user.EmailVerifiedAt != null &&
            !database.Memberships.Any(member => member.UserId == user.Id) && matchingUsers.Contains(user.Id));
        var total = await users.CountAsync(token);
        var rows = await users.OrderBy(user => user.Nickname).ThenBy(user => user.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        var items = new List<object>();
        var ids = rows.Select(user => user.Id).ToArray();
        var matchesByUser = (await profiles.Where(profile => ids.Contains(profile.UserId)).ToArrayAsync(token)).ToLookup(profile => profile.UserId);
        await views.LoadUsersAsync(ids, token);
        foreach (var user in rows)
        {
            var matches = matchesByUser[user.Id];
            var profile = await views.DefaultProfileAsync(user.Id, token);
            items.Add(new { UserId = user.Id, user.Nickname, DefaultProfile = profile is null ? null : BusinessViews.Summary(profile), MatchingProfiles = matches.Select(BusinessViews.Summary) });
        }
        return ApiResponse.Success(new { Items = items, Page = page, PageSize = pageSize, Total = total });
    }
    [HttpGet("players/{userId:guid}")]
    [ProducesResponseType<ApiResponse<PlayerResponse>>(200)]
    public async Task<object> Player(Guid userId, CancellationToken token)
    {
        var user = await database.Users.SingleOrDefaultAsync(user => user.Id == userId && user.Status == "active" && user.EmailVerifiedAt != null &&
            !database.Memberships.Any(member => member.UserId == user.Id), token) ?? throw BusinessOperations.Missing();
        var profiles = await database.Profiles.Where(profile => profile.UserId == userId && profile.DeletedAt == null).ToArrayAsync(token);
        return ApiResponse.Success(new { UserId = user.Id, user.Nickname, Profiles = profiles.Select(profile => new
        { ProfileId = profile.Id, profile.UserId, profile.Name, profile.Server, profile.Power, profile.Styles, OnlineSlots = BusinessJson.Parse(profile.OnlineSlotsJson), profile.Bio }) });
    }
    [HttpGet("guilds/{guildId:guid}/participation")]
    [ProducesResponseType<ApiResponse<ParticipationResponse>>(200)]
    public async Task<object> Participation(Guid guildId, CancellationToken token, int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20, [FromQuery(Name = "unassigned_only")] bool unassignedOnly = false)
    {
        operations.RejectUnknownQuery("page", "page_size", "unassigned_only"); await operations.MemberAsync(guildId, true, false, token);
        BusinessOperations.Pagination(page, pageSize); var range = BusinessOperations.Week();
        var counts = from member in database.Memberships join user in database.Users on member.UserId equals user.Id
            where member.GuildId == guildId
            select new
            {
                MembershipId = member.Id, member.UserId, user.Nickname,
                WeeklyCount = (from assignment in database.Assignments join raid in database.Events on assignment.EventId equals raid.Id
                    where assignment.MembershipId == member.Id && assignment.Status == "confirmed" && raid.Status != "cancelled" &&
                        raid.StartsAt >= range.Start && raid.StartsAt < range.End select assignment.Id).Count(),
                CumulativeCount = (from assignment in database.Assignments join raid in database.Events on assignment.EventId equals raid.Id
                    where assignment.MembershipId == member.Id && assignment.Status == "confirmed" && raid.Status != "cancelled" &&
                        raid.StartsAt >= member.JoinedAt && raid.StartsAt < range.End select assignment.Id).Count()
            };
        var filtered = counts.Where(member => !unassignedOnly || member.WeeklyCount == 0);
        var total = await filtered.CountAsync(token);
        var rows = await filtered.OrderByDescending(member => member.CumulativeCount).ThenBy(member => member.UserId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        var items = new List<object>();
        var ranks = await counts.GroupBy(member => member.CumulativeCount)
            .Select(group => new { Count = group.Key, Members = group.Count() }).ToArrayAsync(token);
        foreach (var row in rows) items.Add(new { row.MembershipId, row.UserId, row.Nickname, row.WeeklyCount, row.CumulativeCount,
            Rank = 1 + ranks.Where(rank => rank.Count > row.CumulativeCount).Sum(rank => rank.Members) });
        return ApiResponse.Success(new { Basis = "confirmed_assignments", WeekKey = range.Key, WeekStart = range.Start, WeekEnd = range.End,
            Timezone = "Asia/Shanghai", ServerTime = DateTimeOffset.UtcNow, CumulativeCutoff = range.End, IncludesFutureInCurrentWeek = true,
            Items = items, Page = page, PageSize = pageSize, Total = total, UnassignedUserIds = rows.Where(row => row.WeeklyCount == 0).Select(row => row.UserId) });
    }
}
