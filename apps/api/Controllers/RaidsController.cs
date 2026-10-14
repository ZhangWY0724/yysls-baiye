using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Business;

namespace Yanyun.Api.Controllers;

[ApiController, Route("api/v1")]
public sealed class RaidsController(YanyunDbContext database, BusinessOperations operations, BusinessViews views,
    CleanupService cleanup) : ControllerBase
{
    [HttpGet("dungeon-templates")]
    public Task<ApiResponse<PagedResponse<TemplateResponse>>> SystemTemplates(CancellationToken token, int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, string? q = null)
        => Templates(null, page, pageSize, q, token);
    [HttpGet("guilds/{guildId:guid}/dungeon-templates")]
    public async Task<ApiResponse<PagedResponse<TemplateResponse>>> GuildTemplates(Guid guildId, CancellationToken token, int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, string? q = null)
    { await operations.MemberAsync(guildId, false, false, token); return await Templates(guildId, page, pageSize, q, token); }
    private async Task<ApiResponse<PagedResponse<TemplateResponse>>> Templates(Guid? guildId, int page, int size, string? q, CancellationToken token)
    {
        operations.RejectUnknownQuery("page", "page_size", "q"); BusinessOperations.Pagination(page, size); q = BusinessOperations.Text(q, "搜索词", 100);
        var query = database.Templates.Where(template => template.ArchivedAt == null && (template.GuildId == null || template.GuildId == guildId) && (q == "" || template.Name.Contains(q)));
        var total = await query.CountAsync(token);
        var rows = await query.OrderBy(template => template.Name).ThenBy(template => template.Id).Skip((page - 1) * size).Take(size).ToArrayAsync(token);
        return ApiResponse.Success(new PagedResponse<TemplateResponse>([.. rows.Select(BusinessViews.Template)], page, size, total));
    }
    [HttpPost("guilds/{guildId:guid}/dungeon-templates")]
    [ProducesResponseType<ApiResponse<TemplateResponse>>(201)]
    public async Task<ActionResult<ApiResponse<TemplateResponse>>> CreateTemplate(Guid guildId, TemplateWrite request, CancellationToken token)
    {
        var result = await operations.WriteAsync($"template.create:{guildId}", request, guildId, null, false, async () =>
        {
            await operations.MemberAsync(guildId, true, false, token);
            if (request.NeedTank is null || request.NeedHealer is null || request.NeedDps is null || request.DefaultDurationMin is null)
                throw BusinessOperations.Invalid("模板容量和时长必填");
            BusinessOperations.Capacity(request.NeedTank.Value, request.NeedHealer.Value, request.NeedDps.Value);
            var template = new DungeonTemplate { GuildId = guildId, CreatedBy = operations.UserId,
                Name = BusinessOperations.Text(request.Name, "模板名", 80, true), NeedTank = request.NeedTank.Value,
                NeedHealer = request.NeedHealer.Value, NeedDps = request.NeedDps.Value, DefaultDurationMin = request.DefaultDurationMin.Value };
            database.Templates.Add(template); return BusinessViews.Template(template);
        }, token);
        return StatusCode(201, ApiResponse.Success(result));
    }
    [HttpPatch("guilds/{guildId:guid}/dungeon-templates/{templateId:guid}")]
    public async Task<ApiResponse<TemplateResponse>> UpdateTemplate(Guid guildId, Guid templateId, JsonElement body, CancellationToken token)
    {
        ProfilesController.CheckFields(body, "name", "need_tank", "need_healer", "need_dps", "default_duration_min", "expected_version");
        if (body.EnumerateObject().Count() < 2 || body.EnumerateObject().Any(property => property.Value.ValueKind == JsonValueKind.Null)) throw BusinessOperations.Invalid("模板字段不能为空");
        var request = body.Deserialize<TemplateWrite>(BusinessJson.Options)!;
        return ApiResponse.Success(await operations.WriteAsync($"template.patch:{templateId}", body, guildId, null, false, async () =>
        {
            await operations.MemberAsync(guildId, true, false, token);
            var template = await database.Templates.SingleOrDefaultAsync(template => template.GuildId == guildId && template.Id == templateId && template.ArchivedAt == null, token) ?? throw BusinessOperations.Missing();
            BusinessOperations.Version(template.Version, request.ExpectedVersion);
            if (request.Name is not null) template.Name = BusinessOperations.Text(request.Name, "模板名", 80, true);
            template.NeedTank = request.NeedTank ?? template.NeedTank; template.NeedHealer = request.NeedHealer ?? template.NeedHealer;
            template.NeedDps = request.NeedDps ?? template.NeedDps; template.DefaultDurationMin = request.DefaultDurationMin ?? template.DefaultDurationMin;
            BusinessOperations.Capacity(template.NeedTank, template.NeedHealer, template.NeedDps);
            if (template.DefaultDurationMin is < 1 or > 1440) throw BusinessOperations.Invalid("时长不正确");
            template.Version++; return BusinessViews.Template(template);
        }, token));
    }
    [HttpDelete("guilds/{guildId:guid}/dungeon-templates/{templateId:guid}")]
    public async Task<ApiResponse<ArchivedResponse>> DeleteTemplate(Guid guildId, Guid templateId, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"template.archive:{templateId}", new { templateId }, guildId, null, false, async () =>
        {
            await operations.MemberAsync(guildId, true, false, token);
            var template = await database.Templates.SingleOrDefaultAsync(template => template.Id == templateId && template.GuildId == guildId, token) ?? throw BusinessOperations.Missing();
            BusinessOperations.Version(template.Version, operations.HeaderVersion()); template.ArchivedAt = DateTimeOffset.UtcNow; template.Version++;
            return new ArchivedResponse(true, template.Version);
        }, token));
    [HttpGet("guilds/{guildId:guid}/events")]
    public async Task<ApiResponse<EventsWeekResponse>> Events(Guid guildId, CancellationToken token, string? week = null)
    {
        operations.RejectUnknownQuery("week"); await operations.MemberAsync(guildId, false, false, token);
        var range = BusinessOperations.Week(week);
        var rows = await database.Events.Where(raid => raid.GuildId == guildId && raid.StartsAt >= range.Start && raid.StartsAt < range.End)
            .OrderBy(raid => raid.StartsAt).ThenBy(raid => raid.Id).ToListAsync(token);
        var events = await views.EventsAsync(rows, operations.UserId, token);
        return ApiResponse.Success(new EventsWeekResponse(range.Key, range.Start, range.End, "Asia/Shanghai", DateTimeOffset.UtcNow, events));
    }
    [HttpGet("guilds/{guildId:guid}/events/{eventId:guid}")]
    public async Task<ApiResponse<EventResponse>> Event(Guid guildId, Guid eventId, CancellationToken token)
    {
        await operations.MemberAsync(guildId, false, false, token);
        var raid = await database.Events.SingleOrDefaultAsync(raid => raid.Id == eventId && raid.GuildId == guildId, token) ?? throw BusinessOperations.Missing();
        return ApiResponse.Success(await views.EventAsync(raid, operations.UserId, token));
    }
    [HttpPost("guilds/{guildId:guid}/events")]
    [ProducesResponseType<ApiResponse<EventResponse>>(201)]
    public async Task<ActionResult<ApiResponse<EventResponse>>> CreateEvent(Guid guildId, EventWrite request, CancellationToken token)
    {
        var eventId = Guid.CreateVersion7();
        var result = await operations.WriteAsync($"event.create:{guildId}", request, guildId, eventId, true, async () =>
        {
            await operations.MemberAsync(guildId, true, false, token);
            if (request.TemplateId is null || request.StartsAt is null || request.SignupMode is null) throw BusinessOperations.Invalid("模板、开始时间和报名模式必填");
            var template = await database.Templates.SingleOrDefaultAsync(template => template.Id == request.TemplateId && template.ArchivedAt == null &&
                (template.GuildId == null || template.GuildId == guildId), token) ?? throw BusinessOperations.Missing();
            var guild = await operations.GuildAsync(guildId, token);
            var raid = new RaidEvent { Id = eventId, GuildId = guildId, CreatedBy = operations.UserId, TemplateId = template.Id,
                TemplateName = template.Name, Title = template.Name, DurationMin = template.DefaultDurationMin, NeedTank = template.NeedTank,
                NeedHealer = template.NeedHealer, NeedDps = template.NeedDps, ReminderEnabled = guild.ReminderEnabled, RemindMinutes = guild.RemindMinutes };
            ApplyEvent(raid, request); database.Events.Add(raid); await database.SaveChangesAsync(token);
            operations.Audit("event.created", eventId, guildId, eventId);
            return await views.EventAsync(raid, operations.UserId, token);
        }, token);
        return StatusCode(201, ApiResponse.Success(result));
    }
    [HttpPatch("guilds/{guildId:guid}/events/{eventId:guid}")]
    public async Task<ApiResponse<EventResponse>> UpdateEvent(Guid guildId, Guid eventId, JsonElement body, CancellationToken token)
    {
        ProfilesController.CheckFields(body, "title", "starts_at", "duration_min", "signup_mode", "status", "need_tank", "need_healer", "need_dps", "note", "reminder_enabled", "remind_minutes", "expected_version");
        if (body.EnumerateObject().Count() < 2 || body.EnumerateObject().Any(property => property.Value.ValueKind == JsonValueKind.Null && property.Name != "note"))
            throw BusinessOperations.Invalid("至少修改一个字段，仅备注允许 null");
        var request = body.Deserialize<EventWrite>(BusinessJson.Options)!;
        return ApiResponse.Success(await operations.WriteAsync($"event.patch:{eventId}", body, guildId, eventId, false, async () =>
        {
            await operations.MemberAsync(guildId, true, false, token);
            var raid = await database.Events.SingleOrDefaultAsync(raid => raid.Id == eventId && raid.GuildId == guildId, token) ?? throw BusinessOperations.Missing();
            BusinessOperations.Version(raid.Version, request.ExpectedVersion); Open(raid);
            if (request.Status is not null && (raid.Status != "draft" || request.Status != "published")) throw BusinessOperations.Invalid("仅允许草稿发布");
            ApplyEvent(raid, request);
            if (body.TryGetProperty("note", out var note) && note.ValueKind == JsonValueKind.Null) raid.Note = "";
            foreach (var (role, capacity) in new[] { ("tank", raid.NeedTank), ("healer", raid.NeedHealer), ("dps", raid.NeedDps) })
                if (await database.Assignments.CountAsync(assignment => assignment.EventId == eventId && assignment.Status == "confirmed" && assignment.SlotRole == role, token) > capacity)
                    throw BusinessOperations.Conflict("CAPACITY_CONFLICT", "新容量小于已报名人数");
            var users = await database.Assignments.Where(assignment => assignment.EventId == eventId).Select(assignment => new { assignment.UserId, assignment.Status }).ToArrayAsync(token);
            foreach (var user in users)
            {
                if (user.Status == "confirmed" && await cleanup.HasConflictAsync(user.UserId, raid, token)) throw BusinessOperations.Conflict("ACCOUNT_TIME_CONFLICT", "改时后与现有排班冲突");
                operations.Notify(user.UserId, guildId, eventId, "raid.changed", "场次安排已变更", $"「{raid.Title}」安排已变更",
                    new { Kind = "raid_event", GuildId = guildId, EventId = eventId });
            }
            raid.Version++; raid.ReminderVersion++; await database.SaveChangesAsync(token);
            await cleanup.PromoteAsync(eventId, token);
            return await views.EventAsync(raid, operations.UserId, token);
        }, token));
    }
    [HttpDelete("guilds/{guildId:guid}/events/{eventId:guid}")]
    public async Task<ApiResponse<CancelledResponse>> CancelEvent(Guid guildId, Guid eventId, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"event.cancel:{eventId}", new { eventId }, guildId, eventId, false, async () =>
        {
            await operations.MemberAsync(guildId, true, false, token);
            var raid = await database.Events.SingleOrDefaultAsync(raid => raid.Id == eventId && raid.GuildId == guildId, token) ?? throw BusinessOperations.Missing();
            BusinessOperations.Version(raid.Version, operations.HeaderVersion()); Open(raid);
            raid.Status = "cancelled"; raid.Version++; raid.ReminderVersion++;
            var users = await database.Assignments.Where(assignment => assignment.EventId == eventId).Select(assignment => assignment.UserId).ToArrayAsync(token);
            foreach (var user in users) operations.Notify(user, guildId, eventId, "raid.cancelled", "场次已取消", $"「{raid.Title}」已取消",
                new { Kind = "raid_event", GuildId = guildId, EventId = eventId });
            return new CancelledResponse(true, raid.Version);
        }, token));
    [HttpPost("events/{eventId:guid}/assignments")]
    public async Task<ApiResponse<AssignmentCreatedResponse>> Signup(Guid eventId, AssignmentWrite request, CancellationToken token)
    {
        var snapshot = await database.Events.AsNoTracking().SingleOrDefaultAsync(raid => raid.Id == eventId, token) ?? throw BusinessOperations.Missing();
        return ApiResponse.Success(await operations.WriteAsync($"assignment.create:{eventId}", request, snapshot.GuildId, eventId, true, async () =>
        {
            var raid = await database.Events.SingleOrDefaultAsync(raid => raid.Id == eventId, token) ?? throw BusinessOperations.Missing(); Open(raid);
            if (raid.Status != "published") throw BusinessOperations.Invalid("草稿不能报名");
            await operations.MemberAsync(raid.GuildId, request.Action == "assign", false, token);
            var profile = await database.Profiles.SingleOrDefaultAsync(profile => profile.Id == request.ProfileId && profile.DeletedAt == null, token) ?? throw BusinessOperations.Missing();
            if (request.Action == "signup" && (profile.UserId != operations.UserId || raid.SignupMode != "open")) throw BusinessOperations.Forbidden();
            var member = await database.Memberships.SingleOrDefaultAsync(member => member.UserId == profile.UserId && member.GuildId == raid.GuildId, token) ?? throw BusinessOperations.Missing();
            if (!await database.Users.AnyAsync(user => user.Id == profile.UserId && user.Status == "active" && user.EmailVerifiedAt != null, token))
                throw BusinessOperations.Missing();
            var guild = await operations.GuildAsync(raid.GuildId, token);
            if (profile.Server != guild.Server) throw BusinessOperations.Invalid("角色区服不匹配");
            if (await database.Assignments.AnyAsync(assignment => assignment.EventId == eventId && assignment.UserId == profile.UserId, token))
                throw BusinessOperations.Conflict("ALREADY_ASSIGNED", "该账号已报名");
            var capacity = request.SlotRole switch { "tank" => raid.NeedTank, "healer" => raid.NeedHealer, "dps" => raid.NeedDps, _ => throw BusinessOperations.Invalid("定位不正确") };
            var full = await database.Assignments.CountAsync(assignment => assignment.EventId == eventId && assignment.SlotRole == request.SlotRole && assignment.Status == "confirmed", token) >= capacity;
            if (full && request.Action == "assign") throw BusinessOperations.Conflict("CAPACITY_CONFLICT", "该定位已满，指派不能转候补");
            if (!full && await cleanup.HasConflictAsync(profile.UserId, raid, token))
                throw BusinessOperations.Conflict("ACCOUNT_TIME_CONFLICT", "该账号已有重叠正式排班");
            if (full && await database.Assignments.CountAsync(assignment => assignment.EventId == eventId && assignment.Status == "waitlist", token) >= 100)
                throw BusinessOperations.Conflict("WAITLIST_LIMIT_REACHED", "候补队列已满");
            var assignment = new Assignment { EventId = eventId, UserId = profile.UserId, ProfileId = profile.Id, MembershipId = member.Id,
                AssignedBy = operations.UserId, SlotRole = request.SlotRole, Status = full ? "waitlist" : "confirmed" };
            database.Assignments.Add(assignment); raid.Version++;
            operations.Notify(profile.UserId, raid.GuildId, eventId, full ? "assignment.waitlisted" : "assignment.confirmed", full ? "已加入候补" : "排班已确认",
                $"「{raid.Title}」{(full ? "候补" : "报名")}成功", new { Kind = "raid_event", raid.GuildId, EventId = eventId }, operations.UserId);
            operations.Audit("assignment.created", assignment.Id, raid.GuildId, eventId, profile.UserId);
            await database.SaveChangesAsync(token);
            return new AssignmentCreatedResponse(assignment.Id, assignment.Status, full ? await database.Assignments.CountAsync(
                row => row.EventId == eventId && row.SlotRole == request.SlotRole && row.Status == "waitlist", token) : null, raid.Version);
        }, token));
    }
    [HttpDelete("events/{eventId:guid}/assignments/{assignmentId:guid}")]
    public async Task<ApiResponse<AssignmentDeletedResponse>> CancelAssignment(Guid eventId, Guid assignmentId, CancellationToken token)
    {
        var snapshot = await database.Events.AsNoTracking().SingleOrDefaultAsync(raid => raid.Id == eventId, token) ?? throw BusinessOperations.Missing();
        return ApiResponse.Success(await operations.WriteAsync($"assignment.cancel:{assignmentId}", new { assignmentId }, snapshot.GuildId, eventId, false, async () =>
        {
            var raid = await database.Events.SingleOrDefaultAsync(raid => raid.Id == eventId, token) ?? throw BusinessOperations.Missing(); Open(raid);
            var member = await operations.MemberAsync(raid.GuildId, false, false, token);
            var assignment = await database.Assignments.SingleOrDefaultAsync(assignment => assignment.Id == assignmentId && assignment.EventId == eventId, token);
            if (assignment is null) return new AssignmentDeletedResponse(true, raid.Version);
            if (assignment.UserId != operations.UserId && member.Role == "member") throw BusinessOperations.Forbidden();
            database.Assignments.Remove(assignment); raid.Version++;
            operations.Notify(assignment.UserId, raid.GuildId, eventId, "assignment.removed", "排班已取消", $"「{raid.Title}」排班已取消",
                new { Kind = "raid_event", raid.GuildId, EventId = eventId }, operations.UserId);
            await database.SaveChangesAsync(token); await cleanup.PromoteAsync(eventId, token);
            return new AssignmentDeletedResponse(true, raid.Version);
        }, token));
    }
    [HttpGet("me/week")]
    public async Task<ApiResponse<MyWeekResponse>> MyWeek(CancellationToken token)
    {
        operations.RejectUnknownQuery(); var range = BusinessOperations.Week();
        var assignments = await (from assignment in database.Assignments join raid in database.Events on assignment.EventId equals raid.Id
            join profile in database.Profiles on assignment.ProfileId equals profile.Id
            where assignment.UserId == operations.UserId && raid.StartsAt >= range.Start && raid.StartsAt < range.End orderby raid.StartsAt, raid.Id select new { Assignment = assignment, Raid = raid, Profile = profile }).ToListAsync(token);
        var now = DateTimeOffset.UtcNow;
        var items = assignments.Select(row =>
        {
            var (assignment, raid) = (row.Assignment, row.Raid);
            var endsAt = raid.StartsAt.AddMinutes(raid.DurationMin);
            return new MyWeekItemResponse(
                new MyEventResponse(raid.Id, raid.GuildId, raid.Title, raid.StartsAt, endsAt,
                    raid.Status == "published" && endsAt <= now ? "finished" : raid.Status, raid.Version,
                    raid.StartsAt > now && raid.Status == "published"),
                new MyAssignmentResponse(assignment.Id, assignment.Status, assignment.SlotRole, BusinessViews.Summary(row.Profile)));
        }).ToArray();
        return ApiResponse.Success(new MyWeekResponse(range.Key, range.Start, range.End, "Asia/Shanghai", now, items));
    }
    private static void Open(RaidEvent raid)
    {
        if (raid.StartsAt <= DateTimeOffset.UtcNow || raid.Status == "cancelled") throw BusinessOperations.Conflict("EVENT_STARTED", "场次已开始或已取消，不能修改");
    }
    private static void ApplyEvent(RaidEvent raid, EventWrite request)
    {
        if (request.StartsAt is not null)
        {
            if (request.StartsAt <= DateTimeOffset.UtcNow || request.StartsAt > DateTimeOffset.UtcNow.AddDays(366)) throw BusinessOperations.Invalid("开始时间须在未来一年内");
            raid.StartsAt = request.StartsAt.Value.ToUniversalTime();
        }
        if (request.Title is not null) raid.Title = BusinessOperations.Text(request.Title, "场次名", 80, true);
        raid.DurationMin = request.DurationMin ?? raid.DurationMin;
        if (raid.DurationMin is < 1 or > 1440) throw BusinessOperations.Invalid("时长不正确");
        if (request.SignupMode is not null)
        { if (request.SignupMode is not ("open" or "assign")) throw BusinessOperations.Invalid("报名模式不正确"); raid.SignupMode = request.SignupMode; }
        if (request.Status is not null)
        { if (request.Status is not ("draft" or "published")) throw BusinessOperations.Invalid("状态不正确"); raid.Status = request.Status; }
        if (request.NeedTank is not null || request.NeedHealer is not null || request.NeedDps is not null)
        {
            if (request.NeedTank is null || request.NeedHealer is null || request.NeedDps is null) throw BusinessOperations.Invalid("容量需要同时提交三项");
            raid.NeedTank = request.NeedTank.Value; raid.NeedHealer = request.NeedHealer.Value; raid.NeedDps = request.NeedDps.Value;
        }
        BusinessOperations.Capacity(raid.NeedTank, raid.NeedHealer, raid.NeedDps);
        if (request.Note is not null) raid.Note = BusinessOperations.Text(request.Note, "备注", 2000);
        raid.ReminderEnabled = request.ReminderEnabled ?? raid.ReminderEnabled;
        raid.RemindMinutes = request.RemindMinutes ?? raid.RemindMinutes;
        if (raid.RemindMinutes is < 1 or > 1440) throw BusinessOperations.Invalid("提醒分钟数不正确");
        raid.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
