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
    public async Task<ApiResponse<PagedResponse<PlayerSummaryResponse>>> Players(CancellationToken token, int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20,
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
        var items = new List<PlayerSummaryResponse>();
        var ids = rows.Select(user => user.Id).ToArray();
        var matchesByUser = (await profiles.Where(profile => ids.Contains(profile.UserId)).ToArrayAsync(token)).ToLookup(profile => profile.UserId);
        await views.LoadUsersAsync(ids, token);
        foreach (var user in rows)
        {
            var matches = matchesByUser[user.Id];
            var profile = await views.DefaultProfileAsync(user.Id, token);
            items.Add(new(user.Id, user.Nickname, profile is null ? null : BusinessViews.Summary(profile), [.. matches.Select(BusinessViews.Summary)]));
        }
        return ApiResponse.Success(new PagedResponse<PlayerSummaryResponse>([.. items], page, pageSize, total));
    }
    [HttpGet("players/{userId:guid}")]
    public async Task<ApiResponse<PlayerResponse>> Player(Guid userId, CancellationToken token)
    {
        var user = await database.Users.SingleOrDefaultAsync(user => user.Id == userId && user.Status == "active" && user.EmailVerifiedAt != null &&
            !database.Memberships.Any(member => member.UserId == user.Id), token) ?? throw BusinessOperations.Missing();
        var profiles = await database.Profiles.Where(profile => profile.UserId == userId && profile.DeletedAt == null).ToArrayAsync(token);
        return ApiResponse.Success(new PlayerResponse(user.Id, user.Nickname, [.. profiles.Select(profile => new PublicProfileResponse(
            profile.Id, profile.UserId, profile.Name, profile.Server, profile.Power, profile.Styles,
            System.Text.Json.JsonSerializer.Deserialize<OnlineSlot[]>(profile.OnlineSlotsJson, BusinessJson.Options)!, profile.Bio))]));
    }
    [HttpGet("guilds/{guildId:guid}/participation")]
    public async Task<ApiResponse<ParticipationResponse>> Participation(Guid guildId, CancellationToken token, int page = 1,
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
        var ranks = await counts.GroupBy(member => member.CumulativeCount)
            .Select(group => new { Count = group.Key, Members = group.Count() }).ToArrayAsync(token);
        var items = rows.Select(row => new ParticipationItemResponse(row.MembershipId, row.UserId, row.Nickname, row.WeeklyCount,
            row.CumulativeCount, 1 + ranks.Where(rank => rank.Count > row.CumulativeCount).Sum(rank => rank.Members))).ToArray();
        return ApiResponse.Success(new ParticipationResponse("confirmed_assignments", range.Key, range.Start, range.End,
            "Asia/Shanghai", DateTimeOffset.UtcNow, range.End, true, items, page, pageSize, total,
            [.. rows.Where(row => row.WeeklyCount == 0).Select(row => row.UserId)]));
    }
}
