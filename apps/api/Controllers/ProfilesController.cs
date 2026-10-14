using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Authentication;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Auth;
using Yanyun.Api.Modules.Business;

namespace Yanyun.Api.Controllers;

[ApiController, Route("api/v1")]
public sealed class ProfilesController(YanyunDbContext database, BusinessOperations operations, BusinessViews views,
    CleanupService cleanup, AuthRedisStore sessions) : ControllerBase
{
    [HttpGet("me"), Authorize]
    public async Task<ApiResponse<MeResponse>> Me(CancellationToken token) => ApiResponse.Success(await views.MeAsync(operations.UserId, token));

    [HttpPatch("me")]
    public async Task<ApiResponse<MeResponse>> UpdateMe(JsonElement body, CancellationToken token)
    {
        CheckFields(body, "nickname", "default_profile_id");
        var request = body.Deserialize<MeWrite>(BusinessJson.Options) ?? throw BusinessOperations.Invalid("需要资料字段");
        return ApiResponse.Success(await operations.WriteAsync("me.patch", body, null, null, false, async () =>
        {
            var user = await database.Users.SingleAsync(user => user.Id == operations.UserId, token);
            if (body.TryGetProperty("nickname", out _)) user.Nickname = BusinessOperations.Text(request.Nickname, "昵称", 32, true);
            if (body.TryGetProperty("default_profile_id", out _))
            {
                if (request.DefaultProfileId is not null && !await database.Profiles.AnyAsync(profile => profile.Id == request.DefaultProfileId &&
                    profile.UserId == user.Id && profile.DeletedAt == null, token)) throw BusinessOperations.Missing();
                user.DefaultProfileId = request.DefaultProfileId;
            }
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(token);
            return await views.MeAsync(user.Id, token);
        }, token));
    }

    [HttpGet("profiles")]
    public async Task<ApiResponse<ItemsResponse<ProfileResponse>>> List(CancellationToken token)
    {
        operations.RejectUnknownQuery();
        var profiles = await database.Profiles.Where(profile => profile.UserId == operations.UserId && profile.DeletedAt == null)
            .OrderBy(profile => profile.CreatedAt).ThenBy(profile => profile.Id).ToListAsync(token);
        var defaultProfile = await views.DefaultProfileAsync(operations.UserId, token);
        return ApiResponse.Success(new ItemsResponse<ProfileResponse>([.. profiles.Select(profile => BusinessViews.Profile(profile, defaultProfile?.Id))]));
    }
    [HttpGet("profiles/{profileId:guid}")]
    public async Task<ApiResponse<ProfileResponse>> Get(Guid profileId, CancellationToken token)
    {
        var profile = await Owned(profileId, token);
        return ApiResponse.Success(BusinessViews.Profile(profile, (await views.DefaultProfileAsync(operations.UserId, token))?.Id));
    }
    [HttpPost("profiles")]
    [ProducesResponseType<ApiResponse<ProfileResponse>>(201)]
    public async Task<ActionResult<ApiResponse<ProfileResponse>>> Create(ProfileWrite request, CancellationToken token)
    {
        var result = await operations.WriteAsync("profile.create", request, null, null, false, async () =>
        {
            if (await database.Profiles.CountAsync(profile => profile.UserId == operations.UserId && profile.DeletedAt == null, token) >= 10)
                throw BusinessOperations.Conflict("PROFILE_LIMIT_REACHED", "最多创建 10 个角色");
            var profile = new GameProfile { UserId = operations.UserId };
            await Apply(profile, request, true, token);
            database.Profiles.Add(profile);
            await database.SaveChangesAsync(token);
            return BusinessViews.Profile(profile, (await views.DefaultProfileAsync(operations.UserId, token))?.Id);
        }, token);
        return StatusCode(201, ApiResponse.Success(result));
    }
    [HttpPatch("profiles/{profileId:guid}")]
    public async Task<ApiResponse<ProfileResponse>> Update(Guid profileId, JsonElement body, CancellationToken token)
    {
        CheckFields(body, "name", "server", "power", "styles", "online_slots", "bio");
        var request = body.Deserialize<ProfileWrite>(BusinessJson.Options) ?? throw BusinessOperations.Invalid("需要角色字段");
        return ApiResponse.Success(await operations.WriteAsync($"profile.patch:{profileId}", body, null, null, false, async () =>
        {
            var profile = await Owned(profileId, token);
            foreach (var property in body.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.Null && property.Name != "bio") throw BusinessOperations.Invalid("字段不能为 null");
            await Apply(profile, request, false, token);
            if (body.TryGetProperty("bio", out var bio) && bio.ValueKind == JsonValueKind.Null) profile.Bio = "";
            return BusinessViews.Profile(profile, (await views.DefaultProfileAsync(operations.UserId, token))?.Id);
        }, token));
    }
    [HttpDelete("profiles/{profileId:guid}")]
    public async Task<ApiResponse<DeletedResponse>> Delete(Guid profileId, CancellationToken token) => ApiResponse.Success(
        await operations.WriteAsync($"profile.delete:{profileId}", new { profileId }, null, null, false, async () =>
        {
            var profile = await Owned(profileId, token);
            if (await (from assignment in database.Assignments join raid in database.Events on assignment.EventId equals raid.Id
                where assignment.ProfileId == profileId && raid.Status != "cancelled" && raid.StartsAt.AddMinutes(raid.DurationMin) > DateTimeOffset.UtcNow
                select assignment.Id).AnyAsync(token)) throw BusinessOperations.Conflict("PROFILE_IN_USE", "该角色仍有未结束的排班");
            profile.DeletedAt = DateTimeOffset.UtcNow;
            var user = await database.Users.SingleAsync(user => user.Id == operations.UserId, token);
            if (user.DefaultProfileId == profileId) user.DefaultProfileId = null;
            return new DeletedResponse(true);
        }, token));

    [HttpPost("me/account-deletion"), Authorize, ServiceFilter(typeof(CsrfGuard))]
    public async Task<ApiResponse<DeletedResponse>> DeleteAccount(AccountDeletionRequest request, CancellationToken token)
    {
        if (request.ConfirmUserId != operations.UserId || request.Confirmation != "DELETE") throw BusinessOperations.Invalid("注销确认不匹配");
        var result = await operations.WriteAsync("account.delete", request, null, null, false, async () =>
        {
            await cleanup.AccountAsync(operations.UserId, token);
            return new DeletedResponse(true);
        }, token);
        try { await sessions.RevokeUserSessionsAsync(operations.UserId); }
        catch (Exception exception) when (exception is StackExchange.Redis.RedisException or BusinessException) { }
        Response.Cookies.Delete("yanyun.refresh", new CookieOptions { Path = "/api/v1/auth", HttpOnly = true,
            Secure = Request.IsHttps, SameSite = SameSiteMode.Lax });
        return ApiResponse.Success(result);
    }

    internal static void CheckFields(JsonElement body, params string[] fields)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.EnumerateObject().Any() ||
            body.EnumerateObject().Any(property => !fields.Contains(property.Name))) throw BusinessOperations.Invalid("字段不符合要求");
    }
    private async Task<GameProfile> Owned(Guid id, CancellationToken token) => await database.Profiles
        .SingleOrDefaultAsync(profile => profile.Id == id && profile.UserId == operations.UserId && profile.DeletedAt == null, token)
        ?? throw BusinessOperations.Missing();
    private async Task Apply(GameProfile profile, ProfileWrite request, bool create, CancellationToken token)
    {
        if (create && (request.Name is null || request.Server is null || request.Power is null || request.Styles is null))
            throw BusinessOperations.Invalid("姓名、区服、鹅力和流派必填");
        if (request.Name is not null) profile.Name = BusinessOperations.Text(request.Name, "角色名", 32, true);
        if (request.Server is not null)
        {
            if (request.Server is not ("official" or "channel")) throw BusinessOperations.Invalid("区服不正确");
            var guildServer = await (from member in database.Memberships join guild in database.Guilds on member.GuildId equals guild.Id
                where member.UserId == profile.UserId select guild.Server).SingleOrDefaultAsync(token);
            if (guildServer is not null && guildServer != request.Server && await database.Assignments.AnyAsync(assignment => assignment.ProfileId == profile.Id, token))
                throw BusinessOperations.Conflict("PROFILE_IN_USE", "排班引用角色不能改为其他区服");
            profile.Server = request.Server;
        }
        if (request.Power is not null)
        {
            if (request.Power < 0) throw BusinessOperations.Invalid("鹅力不能为负数");
            profile.Power = request.Power.Value;
        }
        if (request.Styles is not null)
        {
            var enabled = await database.ProfileStyleOptions.Where(option => option.Enabled).Select(option => option.Code).ToListAsync(token);
            if (enabled.Count == 0) throw new BusinessException(503, "DICTIONARY_NOT_READY", "流派字典尚未初始化");
            if (request.Styles.Length is < 1 or > 20 || request.Styles.Distinct().Count() != request.Styles.Length || request.Styles.Except(enabled).Any())
                throw BusinessOperations.Invalid("流派选项不正确");
            profile.Styles = request.Styles;
        }
        if (request.OnlineSlots is not null)
        {
            if (request.OnlineSlots.Length > 28 || request.OnlineSlots.Distinct().Count() != request.OnlineSlots.Length ||
                request.OnlineSlots.Any(slot => slot is null || slot.Weekday is < 1 or > 7 || slot.StartMinute < 0 || slot.EndMinute > 1440 || slot.StartMinute >= slot.EndMinute))
                throw BusinessOperations.Invalid("在线时段不正确");
            profile.OnlineSlotsJson = BusinessJson.Serialize(request.OnlineSlots);
        }
        if (request.Bio is not null) profile.Bio = BusinessOperations.Text(request.Bio, "简介", 500);
        profile.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
