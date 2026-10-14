using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Auth;
using Yanyun.Api.Modules.Business;

namespace Yanyun.Api.Controllers;

[ApiController, Route("api/v1")]
public sealed class CollaborationController(YanyunDbContext database, BusinessOperations operations, BusinessViews views) : ControllerBase
{
    [HttpPost("guilds/{guildId:guid}/applications")]
    [ProducesResponseType<ApiResponse<ApplicationResponse>>(201)]
    public async Task<ActionResult<ApiResponse<ApplicationResponse>>> Apply(Guid guildId, ApplicationWrite request, CancellationToken token)
    {
        var result = await operations.WriteAsync($"application.create:{guildId}", request, guildId, null, false, async () =>
        {
            var guild = await operations.GuildAsync(guildId, token);
            if (!guild.Recruiting) throw BusinessOperations.Conflict("RECRUITMENT_CLOSED", "百业已暂停招募");
            await CanJoin(guild, operations.UserId, token);
            if (await database.Applications.AnyAsync(application => application.GuildId == guildId && application.UserId == operations.UserId && application.Status == "pending", token))
                throw BusinessOperations.Conflict("ALREADY_APPLIED", "已提交申请");
            var application = new GuildApplication { GuildId = guildId, UserId = operations.UserId, Message = BusinessOperations.Text(request.Message, "申请说明", 500) };
            database.Applications.Add(application); await database.SaveChangesAsync(token);
            var managers = await database.Memberships.Where(member => member.GuildId == guildId && member.Role != "member").Select(member => member.UserId).ToArrayAsync(token);
            foreach (var user in managers) operations.Notify(user, guildId, null, "application.received", "新的入会申请", "有玩家申请加入百业",
                new { Kind = "application", GuildId = guildId, ApplicationId = application.Id }, operations.UserId);
            return await views.ApplicationAsync(application, false, token);
        }, token);
        return StatusCode(201, ApiResponse.Success(result));
    }
    [HttpGet("guilds/{guildId:guid}/applications")]
    public async Task<ApiResponse<PagedResponse<ApplicationResponse>>> GuildApplications(Guid guildId, CancellationToken token, int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20, string? status = null)
    { await operations.MemberAsync(guildId, true, false, token); return await Applications(guildId, true, page, pageSize, status, token); }
    [HttpGet("me/applications")]
    public Task<ApiResponse<PagedResponse<ApplicationResponse>>> MyApplications(CancellationToken token, int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, string? status = null)
        => Applications(null, false, page, pageSize, status, token);
    private async Task<ApiResponse<PagedResponse<ApplicationResponse>>> Applications(Guid? guildId, bool admin, int page, int size, string? status, CancellationToken token)
    {
        operations.RejectUnknownQuery("page", "page_size", "status"); BusinessOperations.Pagination(page, size);
        if (status is not null and not ("pending" or "approved" or "rejected" or "withdrawn")) throw BusinessOperations.Invalid("申请状态不正确");
        var query = database.Applications.Where(application => (guildId == null ? application.UserId == operations.UserId : application.GuildId == guildId) &&
            (status == null || application.Status == status));
        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(application => application.CreatedAt).ThenBy(application => application.Id).Skip((page - 1) * size).Take(size).ToListAsync(token);
        await views.LoadGuildsAsync(rows.Select(application => application.GuildId), token);
        if (admin) await views.LoadUsersAsync(rows.Select(application => application.UserId), token);
        var items = new List<ApplicationResponse>(); foreach (var application in rows) items.Add(await views.ApplicationAsync(application, admin, token));
        return ApiResponse.Success(new PagedResponse<ApplicationResponse>([.. items], page, size, total));
    }
    [HttpPatch("applications/{applicationId:guid}")]
    public async Task<ApiResponse<ApplicationDecisionResponse>> Decide(Guid applicationId, ApplicationDecision request, CancellationToken token)
    {
        var snapshot = await database.Applications.AsNoTracking().SingleOrDefaultAsync(application => application.Id == applicationId, token) ?? throw BusinessOperations.Missing();
        return ApiResponse.Success(await operations.WriteAsync($"application.decide:{applicationId}", request, snapshot.GuildId, null,
            request.Decision == "approved", async () =>
        {
            var application = await database.Applications.SingleOrDefaultAsync(application => application.Id == applicationId, token) ?? throw BusinessOperations.Missing();
            await operations.MemberAsync(application.GuildId, true, false, token);
            if (application.Status != "pending") throw BusinessOperations.Conflict("APPLICATION_ALREADY_HANDLED", "申请已处理，请刷新");
            Membership? member = null;
            if (request.Decision == "approved")
            {
                var guild = await operations.GuildAsync(application.GuildId, token); await CanJoin(guild, application.UserId, token);
                member = new Membership { GuildId = guild.Id, UserId = application.UserId };
                database.Memberships.Add(member); guild.Version++;
            }
            application.Status = request.Decision; application.HandledAt = DateTimeOffset.UtcNow; application.HandledBy = operations.UserId;
            operations.Notify(application.UserId, application.GuildId, null, request.Decision == "approved" ? "application.approved" : "application.rejected", "入会申请已处理",
                request.Decision == "approved" ? "你的申请已通过" : "你的申请未通过", new { Kind = "application", application.GuildId, ApplicationId = application.Id }, operations.UserId);
            await database.SaveChangesAsync(token);
            var view = await views.ApplicationAsync(application, true, token);
            return new ApplicationDecisionResponse(view.Id, view.GuildId, view.GuildName, view.UserId, view.Message, view.Status,
                view.CreatedAt, view.HandledAt, view.Applicant, member?.Id);
        }, token));
    }
    [HttpPost("applications/{applicationId:guid}/withdraw")]
    public async Task<ApiResponse<ApplicationResponse>> Withdraw(Guid applicationId, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"application.withdraw:{applicationId}", new { applicationId }, null, null, false, async () =>
        {
            var application = await database.Applications.SingleOrDefaultAsync(application => application.Id == applicationId && application.UserId == operations.UserId, token) ?? throw BusinessOperations.Missing();
            if (application.Status == "withdrawn") return await views.ApplicationAsync(application, false, token);
            if (application.Status != "pending") throw BusinessOperations.Conflict("APPLICATION_ALREADY_HANDLED", "申请已处理，无法撤回");
            application.Status = "withdrawn"; application.HandledAt = DateTimeOffset.UtcNow;
            return await views.ApplicationAsync(application, false, token);
        }, token));
    [HttpPost("guilds/{guildId:guid}/invitations")]
    [ProducesResponseType<ApiResponse<InvitationCreatedResponse>>(201)]
    public async Task<ActionResult<ApiResponse<InvitationCreatedResponse>>> Invite(Guid guildId, InvitationWrite request, CancellationToken token)
    {
        var result = await operations.WriteAsync($"invitation.create:{guildId}", request, guildId, null, false, async () =>
        {
            await operations.MemberAsync(guildId, true, false, token); var guild = await operations.GuildAsync(guildId, token);
            if (request.Type == "direct" && (request.TargetUserId is null || request.MaxUses is not null) ||
                request.Type == "code" && request.TargetUserId is not null) throw BusinessOperations.Invalid("邀请类型与字段不匹配");
            if (request.TargetUserId is not null) await CanJoin(guild, request.TargetUserId.Value, token);
            var expires = request.ExpiresAt ?? DateTimeOffset.UtcNow.AddDays(7);
            if (expires <= DateTimeOffset.UtcNow || expires > DateTimeOffset.UtcNow.AddDays(30)) throw BusinessOperations.Invalid("有效期须在未来 30 天内");
            var code = request.Type == "code" ? AuthRedisStore.RandomToken() : null;
            var invitation = new Invitation { GuildId = guildId, CreatedBy = operations.UserId, Type = request.Type,
                TargetUserId = request.TargetUserId, CodeHash = code is null ? null : AuthRedisStore.Hash(code), ExpiresAt = expires.ToUniversalTime(),
                MaxUses = request.Type == "direct" ? 1 : request.MaxUses ?? 20 };
            database.Invitations.Add(invitation);
            if (invitation.TargetUserId is not null) operations.Notify(invitation.TargetUserId.Value, guildId, null,
                "invitation.received", "收到百业邀请", $"「{guild.Name}」邀请你加入", new { Kind = "invitation", GuildId = guildId, InvitationId = invitation.Id }, invitation.CreatedBy);
            await database.SaveChangesAsync(token);
            var view = await views.InvitationAsync(invitation, token);
            return new InvitationCreatedResponse(view.Id, view.GuildId, view.GuildName, view.Type, view.TargetUserId, view.ExpiresAt,
                view.MaxUses, view.UsedCount, view.Status, view.CreatedAt, code, code is null ? null : $"/invitations/code/{code}");
        }, token);
        return StatusCode(201, ApiResponse.Success(result));
    }
    [HttpGet("guilds/{guildId:guid}/invitations")]
    public async Task<ApiResponse<PagedResponse<InvitationResponse>>> GuildInvitations(Guid guildId, CancellationToken token, int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20, string? type = null, string? status = null)
    { await operations.MemberAsync(guildId, true, false, token); return await Invitations(guildId, page, pageSize, type, status, token); }
    [HttpGet("me/invitations")]
    public Task<ApiResponse<PagedResponse<InvitationResponse>>> MyInvitations(CancellationToken token, int page = 1, [FromQuery(Name = "page_size")] int pageSize = 20, string? status = null)
        => Invitations(null, page, pageSize, null, status, token);
    private async Task<ApiResponse<PagedResponse<InvitationResponse>>> Invitations(Guid? guildId, int page, int size, string? type, string? status, CancellationToken token)
    {
        operations.RejectUnknownQuery(guildId is null ? ["page", "page_size", "status"] : ["page", "page_size", "type", "status"]);
        BusinessOperations.Pagination(page, size);
        if (type is not null and not ("code" or "direct") || status is not null and not ("pending" or "accepted" or "declined" or "revoked" or "expired"))
            throw BusinessOperations.Invalid("邀请筛选不正确");
        var now = DateTimeOffset.UtcNow;
        var query = database.Invitations.Where(invitation => (guildId == null ? invitation.TargetUserId == operations.UserId : invitation.GuildId == guildId) &&
            (type == null || invitation.Type == type) && (status == null || (status == "expired" ? invitation.Status == "pending" && invitation.ExpiresAt <= now :
                invitation.Status == status && (status != "pending" || invitation.ExpiresAt > now))));
        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(invitation => invitation.CreatedAt).ThenBy(invitation => invitation.Id).Skip((page - 1) * size).Take(size).ToListAsync(token);
        await views.LoadGuildsAsync(rows.Select(invitation => invitation.GuildId), token);
        var items = new List<InvitationResponse>(); foreach (var invitation in rows) items.Add(await views.InvitationAsync(invitation, token));
        return ApiResponse.Success(new PagedResponse<InvitationResponse>([.. items], page, size, total));
    }
    [HttpGet("invitations/code/{code}")]
    public async Task<ApiResponse<InvitationPreviewResponse>> PreviewCode(string code, CancellationToken token) => ApiResponse.Success(await Preview(await FindCode(code, token), token));
    [HttpGet("invitations/{invitationId:guid}")]
    public async Task<ApiResponse<InvitationPreviewResponse>> PreviewDirect(Guid invitationId, CancellationToken token) => ApiResponse.Success(await Preview(await FindDirect(invitationId, token), token));
    private async Task<InvitationPreviewResponse> Preview(Invitation invitation, CancellationToken token)
    {
        var guild = await operations.GuildAsync(invitation.GuildId, token);
        var inGuild = await database.Memberships.AnyAsync(member => member.UserId == operations.UserId, token);
        var validProfile = await database.Profiles.AnyAsync(profile => profile.UserId == operations.UserId && profile.DeletedAt == null && profile.Server == guild.Server, token);
        var reason = InvitationError(invitation) ?? (inGuild ? "ALREADY_IN_GUILD" : !validProfile ? "PROFILE_REQUIRED" : null);
        var inviter = await database.Users.Where(user => user.Id == invitation.CreatedBy).Select(user => user.Nickname).SingleOrDefaultAsync(token);
        return new InvitationPreviewResponse(await views.InvitationAsync(invitation, token), await views.GuildAsync(guild, token),
            inviter, reason is null, invitation.Type == "direct" && invitation.Status == "pending" && invitation.ExpiresAt > DateTimeOffset.UtcNow, reason);
    }
    [HttpPost("invitations/code/{code}/accept")]
    public async Task<ApiResponse<MembershipResponse>> AcceptCode(string code, CancellationToken token)
    { var invitation = await FindCode(code, token); return await Accept(invitation.Id, token); }
    [HttpPost("invitations/{invitationId:guid}/accept")]
    public async Task<ApiResponse<MembershipResponse>> AcceptDirect(Guid invitationId, CancellationToken token)
    { await FindDirect(invitationId, token); return await Accept(invitationId, token); }
    private async Task<ApiResponse<MembershipResponse>> Accept(Guid invitationId, CancellationToken token)
    {
        var snapshot = await database.Invitations.AsNoTracking().SingleOrDefaultAsync(invitation => invitation.Id == invitationId, token) ?? throw BusinessOperations.Missing();
        return ApiResponse.Success(await operations.WriteAsync($"invitation.accept:{invitationId}", new { invitationId }, snapshot.GuildId, null, true, async () =>
        {
            var invitation = await database.Invitations.SingleOrDefaultAsync(invitation => invitation.Id == invitationId, token) ?? throw BusinessOperations.Missing();
            if (invitation.Type == "direct" && invitation.TargetUserId != operations.UserId) throw BusinessOperations.Missing();
            var error = InvitationError(invitation); if (error is not null) throw BusinessOperations.Conflict(error, "邀请已失效或已处理");
            var guild = await operations.GuildAsync(invitation.GuildId, token); await CanJoin(guild, operations.UserId, token);
            var member = new Membership { GuildId = guild.Id, UserId = operations.UserId }; database.Memberships.Add(member);
            invitation.UsedCount++; if (invitation.Type == "direct") invitation.Status = "accepted"; guild.Version++;
            database.Set<InvitationAcceptance>().Add(new() { InvitationId = invitationId, MembershipId = member.Id, UserId = operations.UserId });
            await database.SaveChangesAsync(token);
            operations.Notify(invitation.CreatedBy, guild.Id, null, "invitation.accepted", "邀请已接受", "玩家已接受邀请加入百业",
                new { Kind = "guild", GuildId = guild.Id }, operations.UserId);
            return await views.MembershipAsync(member, token);
        }, token));
    }
    [HttpPost("invitations/{invitationId:guid}/decline")]
    public Task<ApiResponse<InvitationResponse>> Decline(Guid invitationId, CancellationToken token) => ChangeInvitation(invitationId, false, token);
    [HttpPost("invitations/{invitationId:guid}/revoke")]
    public Task<ApiResponse<InvitationResponse>> Revoke(Guid invitationId, CancellationToken token) => ChangeInvitation(invitationId, true, token);
    private async Task<ApiResponse<InvitationResponse>> ChangeInvitation(Guid id, bool revoke, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"invitation.{(revoke ? "revoke" : "decline")}:{id}", new { id }, null, null, false, async () =>
        {
            var invitation = await database.Invitations.SingleOrDefaultAsync(invitation => invitation.Id == id, token) ?? throw BusinessOperations.Missing();
            if (revoke) await operations.MemberAsync(invitation.GuildId, true, false, token);
            else if (invitation.Type != "direct" || invitation.TargetUserId != operations.UserId) throw BusinessOperations.Missing();
            var next = revoke ? "revoked" : "declined";
            if (invitation.Status == next) return await views.InvitationAsync(invitation, token);
            if (invitation.Status != "pending") throw BusinessOperations.Conflict("INVITATION_ALREADY_HANDLED", "邀请已处理");
            invitation.Status = next;
            if (!revoke) operations.Notify(invitation.CreatedBy, invitation.GuildId, null, "invitation.declined", "邀请已拒绝",
                "玩家已拒绝加入百业", new { Kind = "guild", invitation.GuildId }, invitation.TargetUserId!.Value);
            return await views.InvitationAsync(invitation, token);
        }, token));
    private async Task CanJoin(Guild guild, Guid userId, CancellationToken token)
    {
        if (!await database.Users.AnyAsync(user => user.Id == userId && user.Status == "active" && user.EmailVerifiedAt != null, token)) throw BusinessOperations.Missing();
        if (await database.Memberships.AnyAsync(member => member.UserId == userId, token)) throw BusinessOperations.Conflict("ALREADY_IN_GUILD", "该账号已加入百业");
        if (!await database.Profiles.AnyAsync(profile => profile.UserId == userId && profile.DeletedAt == null && profile.Server == guild.Server, token))
            throw BusinessOperations.Invalid("需要对应区服的角色");
    }
    private Task<Invitation> FindCode(string code, CancellationToken token)
    {
        if (code.Length != 64 || !code.All(Uri.IsHexDigit)) throw BusinessOperations.Missing();
        var hash = AuthRedisStore.Hash(code);
        return Find(database.Invitations.Where(invitation => invitation.Type == "code" && invitation.CodeHash == hash), token);
    }
    private Task<Invitation> FindDirect(Guid id, CancellationToken token) => Find(database.Invitations.Where(invitation => invitation.Id == id && invitation.Type == "direct" && invitation.TargetUserId == operations.UserId), token);
    private static async Task<Invitation> Find(IQueryable<Invitation> query, CancellationToken token) => await query.SingleOrDefaultAsync(token) ?? throw BusinessOperations.Missing();
    private static string? InvitationError(Invitation invitation) => invitation.Status switch
    {
        "revoked" => "INVITATION_REVOKED", "accepted" or "declined" => "INVITATION_ALREADY_HANDLED",
        _ => invitation.ExpiresAt <= DateTimeOffset.UtcNow ? "INVITATION_EXPIRED" : invitation.UsedCount >= invitation.MaxUses ? "INVITATION_EXHAUSTED" : null
    };
}
