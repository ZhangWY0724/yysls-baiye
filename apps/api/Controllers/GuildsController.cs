using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Business;

namespace Yanyun.Api.Controllers;

[ApiController, Route("api/v1/guilds")]
public sealed class GuildsController(YanyunDbContext database, BusinessOperations operations,
    BusinessViews views, CleanupService cleanup) : ControllerBase
{
    [HttpGet]
    public async Task<ApiResponse<PagedResponse<GuildResponse>>> List(CancellationToken token, int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20,
        string? q = null, string? server = null, bool? recruiting = null)
    {
        operations.RejectUnknownQuery("page", "page_size", "q", "server", "recruiting");
        BusinessOperations.Pagination(page, pageSize);
        q = BusinessOperations.Text(q, "搜索词", 100);
        if (server is not null and not ("official" or "channel")) throw BusinessOperations.Invalid("区服不正确");
        var query = database.Guilds.Where(guild => (q == "" || guild.Name.Contains(q)) &&
            (server == null || guild.Server == server) && (recruiting == null || guild.Recruiting == recruiting));
        var total = await query.CountAsync(token);
        var rows = await query.OrderBy(guild => guild.Name).ThenBy(guild => guild.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
        await views.LoadGuildsAsync(rows.Select(guild => guild.Id), token);
        var items = new List<GuildResponse>();
        foreach (var guild in rows) items.Add(await views.GuildAsync(guild, token));
        return ApiResponse.Success(new PagedResponse<GuildResponse>([.. items], page, pageSize, total));
    }
    [HttpPost]
    [ProducesResponseType<ApiResponse<GuildCreatedResponse>>(201)]
    public async Task<ActionResult<ApiResponse<GuildCreatedResponse>>> Create(GuildWrite request, CancellationToken token)
    {
        Guid guildId = Guid.CreateVersion7();
        var result = await operations.WriteAsync("guild.create", request, guildId, null, true, async () =>
        {
            if (request.Server is not ("official" or "channel")) throw BusinessOperations.Invalid("区服必填");
            if (await database.Memberships.AnyAsync(member => member.UserId == operations.UserId, token))
                throw BusinessOperations.Conflict("ALREADY_IN_GUILD", "你已加入百业");
            if (!await database.Profiles.AnyAsync(profile => profile.UserId == operations.UserId && profile.DeletedAt == null && profile.Server == request.Server, token))
                throw BusinessOperations.Invalid("请先创建对应区服的游戏角色");
            var guild = new Guild { Id = guildId, OwnerUserId = operations.UserId, Server = request.Server,
                Name = BusinessOperations.Text(request.Name, "百业名称", 40, true), Intro = BusinessOperations.Text(request.Intro, "简介", 2000),
                RecruitRequirements = BusinessOperations.Text(request.RecruitRequirements, "招募要求", 1000), Recruiting = request.Recruiting ?? true };
            var member = new Membership { UserId = operations.UserId, GuildId = guildId, Role = "owner" };
            database.Guilds.Add(guild); database.Memberships.Add(member);
            await database.SaveChangesAsync(token);
            operations.Audit("guild.created", guildId, guildId, null);
            var summary = await views.GuildAsync(guild, token);
            return new GuildCreatedResponse(summary.Id, summary.Name, summary.Server, summary.Intro, summary.RecruitRequirements,
                summary.Recruiting, summary.MemberCount, summary.Version, await views.MembershipAsync(member, token));
        }, token);
        return StatusCode(201, ApiResponse.Success(result));
    }
    [HttpGet("{guildId:guid}")]
    public async Task<ApiResponse<GuildDetailResponse>> Get(Guid guildId, CancellationToken token)
    {
        var guild = await operations.GuildAsync(guildId, token);
        // 成员名单与近期副本只对本百业成员可见，其他人只看到简介和招募信息。
        var isMember = await database.Memberships.AnyAsync(member => member.GuildId == guildId && member.UserId == operations.UserId, token);
        var members = isMember ? await database.Memberships.Where(member => member.GuildId == guildId).OrderBy(member => member.JoinedAt).Take(20).ToListAsync(token) : [];
        var memberIds = members.Select(member => member.UserId).ToArray();
        var nicknames = await database.Users.Where(user => memberIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.Nickname, token);
        var publicMembers = members.Select(member => new PublicMemberResponse(member.UserId, nicknames.GetValueOrDefault(member.UserId, ""), member.Role, member.JoinedAt)).ToArray();
        var summary = await views.GuildAsync(guild, token);
        var recentEvents = isMember ? await database.Events.Where(raid => raid.GuildId == guildId && raid.Status == "published")
            .OrderByDescending(raid => raid.StartsAt).Take(10)
            .Select(raid => new RecentEventResponse(raid.Id, raid.Title, raid.StartsAt, raid.StartsAt.AddMinutes(raid.DurationMin))).ToArrayAsync(token) : [];
        var application = await database.Applications.SingleOrDefaultAsync(application => application.GuildId == guildId &&
            application.UserId == operations.UserId && application.Status == "pending", token);
        return ApiResponse.Success(new GuildDetailResponse(summary.Id, summary.Name, summary.Server, summary.Intro,
            summary.RecruitRequirements, summary.Recruiting, summary.MemberCount, summary.Version, publicMembers, recentEvents,
            application is null ? null : await views.ApplicationAsync(application, false, token)));
    }
    [HttpPatch("{guildId:guid}")]
    public async Task<ApiResponse<GuildResponse>> Update(Guid guildId, JsonElement body, CancellationToken token)
    {
        ProfilesController.CheckFields(body, "name", "intro", "recruit_requirements", "recruiting", "expected_version");
        if (body.EnumerateObject().Count() <= 1) throw BusinessOperations.Invalid("至少修改一个字段");
        if (body.EnumerateObject().Any(property => property.Value.ValueKind == JsonValueKind.Null))
            throw BusinessOperations.Invalid("百业字段不能为空");
        var request = body.Deserialize<GuildWrite>(BusinessJson.Options)!;
        return ApiResponse.Success(await operations.WriteAsync($"guild.patch:{guildId}", body, guildId, null, false, async () =>
        {
            await operations.MemberAsync(guildId, true, false, token);
            var guild = await operations.GuildAsync(guildId, token); BusinessOperations.Version(guild.Version, request.ExpectedVersion);
            if (body.TryGetProperty("name", out _)) guild.Name = BusinessOperations.Text(request.Name, "名称", 40, true);
            if (body.TryGetProperty("intro", out _)) guild.Intro = BusinessOperations.Text(request.Intro, "简介", 2000);
            if (body.TryGetProperty("recruit_requirements", out _)) guild.RecruitRequirements = BusinessOperations.Text(request.RecruitRequirements, "招募要求", 1000);
            if (body.TryGetProperty("recruiting", out _)) guild.Recruiting = request.Recruiting ?? throw BusinessOperations.Invalid("招募状态不能为空");
            guild.Version++; guild.UpdatedAt = DateTimeOffset.UtcNow;
            return await views.GuildAsync(guild, token);
        }, token));
    }
    [HttpDelete("{guildId:guid}")]
    public async Task<ApiResponse<DeletedResponse>> Delete(Guid guildId, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"guild.delete:{guildId}", new { guildId }, guildId, null, false, async () =>
        {
            await operations.MemberAsync(guildId, false, true, token);
            var guild = await operations.GuildAsync(guildId, token);
            BusinessOperations.Version(guild.Version, operations.HeaderVersion()); operations.ConfirmHeader("X-Confirm-Guild-Id", guildId);
            await cleanup.GuildAsync(guildId, operations.UserId, token); return new DeletedResponse(true);
        }, token));
    [HttpGet("{guildId:guid}/members")]
    public async Task<ApiResponse<PagedResponse<MemberResponse>>> Members(Guid guildId, CancellationToken token, int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20, string? q = null, string? role = null)
    {
        await operations.MemberAsync(guildId, false, false, token); BusinessOperations.Pagination(page, pageSize);
        operations.RejectUnknownQuery("page", "page_size", "q", "role"); q = BusinessOperations.Text(q, "搜索词", 100);
        if (role is not null and not ("owner" or "admin" or "member")) throw BusinessOperations.Invalid("成员身份不正确");
        var query = from member in database.Memberships join user in database.Users on member.UserId equals user.Id
            where member.GuildId == guildId && (role == null || member.Role == role) && (q == "" || user.Nickname.Contains(q)) select member;
        var total = await query.CountAsync(token);
        var members = await query.OrderBy(member => member.JoinedAt).ThenBy(member => member.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
        await views.LoadUsersAsync(members.Select(member => member.UserId), token);
        var items = new List<MemberResponse>(); foreach (var member in members) items.Add(await views.MemberAsync(member, token));
        return ApiResponse.Success(new PagedResponse<MemberResponse>([.. items], page, pageSize, total));
    }
    [HttpPatch("{guildId:guid}/members/{membershipId:guid}/role")]
    public async Task<ApiResponse<RoleChangedResponse>> Role(Guid guildId, Guid membershipId, MemberRoleWrite request, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"member.role:{membershipId}", request, guildId, null, false, async () =>
        {
            await operations.MemberAsync(guildId, false, true, token);
            var guild = await operations.GuildAsync(guildId, token); BusinessOperations.Version(guild.Version, request.ExpectedVersion);
            var member = await database.Memberships.SingleOrDefaultAsync(member => member.GuildId == guildId && member.Id == membershipId, token) ?? throw BusinessOperations.Missing();
            if (member.Role == "owner") throw BusinessOperations.Forbidden();
            member.Role = request.Role; guild.Version++;
            operations.Notify(member.UserId, guildId, null, "member.role_changed", "成员身份变更", $"你的百业身份已变更为 {(request.Role == "admin" ? "管理员" : "成员")}", new { Kind = "guild", GuildId = guildId });
            return new RoleChangedResponse(await views.MemberAsync(member, token), guild.Version);
        }, token));
    [HttpDelete("{guildId:guid}/members/{membershipId:guid}")]
    public async Task<ApiResponse<MemberDeletedResponse>> Remove(Guid guildId, Guid membershipId, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"member.remove:{membershipId}", new { membershipId }, guildId, null, false, async () =>
        {
            var actor = await operations.MemberAsync(guildId, true, false, token);
            var guild = await operations.GuildAsync(guildId, token); BusinessOperations.Version(guild.Version, operations.HeaderVersion());
            operations.ConfirmHeader("X-Confirm-Membership-Id", membershipId);
            var member = await database.Memberships.SingleOrDefaultAsync(member => member.GuildId == guildId && member.Id == membershipId, token) ?? throw BusinessOperations.Missing();
            if (member.Role == "owner" || member.UserId == operations.UserId || actor.Role == "admin" && member.Role != "member") throw BusinessOperations.Forbidden();
            await cleanup.MemberAsync(member, token);
            guild = await operations.GuildAsync(guildId, token); guild.Version++;
            cleanup.NotifyRemoved(member.UserId, guild.Name);
            return new MemberDeletedResponse(true, guild.Version);
        }, token));
    [HttpPost("{guildId:guid}/leave")]
    public async Task<ApiResponse<LeaveResponse>> Leave(Guid guildId, LeaveWrite request, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"guild.leave:{guildId}", request, guildId, null, false, async () =>
        {
            var member = await operations.MemberAsync(guildId, false, false, token);
            var guild = await operations.GuildAsync(guildId, token); BusinessOperations.Version(guild.Version, request.ExpectedVersion);
            if (request.ConfirmGuildId != guildId || member.Role == "owner" && request.ConfirmDissolve != true ||
                member.Role != "owner" && request.ConfirmDissolve == true) throw BusinessOperations.Invalid("退会确认不匹配；社长退会须确认解散");
            var dissolved = member.Role == "owner";
            if (dissolved) await cleanup.GuildAsync(guildId, operations.UserId, token);
            else { await cleanup.MemberAsync(member, token); guild = await operations.GuildAsync(guildId, token); guild.Version++; }
            return new LeaveResponse(true, dissolved);
        }, token));
    [HttpPost("{guildId:guid}/owner-transfer")]
    public async Task<ApiResponse<OwnerTransferResponse>> Transfer(Guid guildId, OwnerTransferWrite request, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"guild.transfer:{guildId}", request, guildId, null, false, async () =>
        {
            var actor = await operations.MemberAsync(guildId, false, true, token);
            var guild = await operations.GuildAsync(guildId, token); BusinessOperations.Version(guild.Version, request.ExpectedVersion);
            var target = await database.Memberships.SingleOrDefaultAsync(member => member.GuildId == guildId && member.Id == request.TargetMembershipId, token) ?? throw BusinessOperations.Missing();
            if (target.Id == actor.Id) throw BusinessOperations.Invalid("不能转让给本人");
            actor.Role = request.PreviousOwnerRole; await database.SaveChangesAsync(token);
            target.Role = "owner"; guild.OwnerUserId = target.UserId; guild.Version++;
            operations.Notify(target.UserId, guildId, null, "guild.owner_transferred", "社长已转让", "你已成为百业社长", new { Kind = "guild", GuildId = guildId });
            operations.Notify(actor.UserId, guildId, null, "guild.owner_transferred", "社长已转让", "你的社长转让已完成", new { Kind = "guild", GuildId = guildId }, target.UserId);
            return new OwnerTransferResponse(target.UserId, actor.Role, guild.Version,
                [new(actor.Id, actor.Role), new(target.Id, target.Role)]);
        }, token));
    [HttpGet("{guildId:guid}/reminder-settings")]
    public async Task<ApiResponse<ReminderResponse>> Reminders(Guid guildId, CancellationToken token)
    { await operations.MemberAsync(guildId, false, false, token); var guild = await operations.GuildAsync(guildId, token); return ApiResponse.Success(new ReminderResponse(guild.ReminderEnabled, guild.RemindMinutes, guild.Version)); }
    [HttpPut("{guildId:guid}/reminder-settings")]
    public async Task<ApiResponse<ReminderResponse>> SetReminders(Guid guildId, ReminderWrite request, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"guild.reminders:{guildId}", request, guildId, null, false, async () =>
        {
            await operations.MemberAsync(guildId, true, false, token); var guild = await operations.GuildAsync(guildId, token);
            BusinessOperations.Version(guild.Version, request.ExpectedVersion); guild.ReminderEnabled = request.ReminderEnabled;
            guild.RemindMinutes = request.RemindMinutes; guild.Version++; return new ReminderResponse(guild.ReminderEnabled, guild.RemindMinutes, guild.Version);
        }, token));
}
