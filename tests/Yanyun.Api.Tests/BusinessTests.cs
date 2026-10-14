using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Business;
using Yanyun.Api.Modules.Auth;
using Yanyun.Api.Modules.Notifications;

namespace Yanyun.Api.Tests;

public sealed class BusinessTests : IAsyncLifetime
{
    private readonly BusinessApiFactory _factory = new();
    public Task InitializeAsync() => Environment.GetEnvironmentVariable("YANYUN_RUN_INFRA_TESTS") == "1"
        ? _factory.InitializeDatabaseAsync() : Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();
    private const string Password = "Test-password-2026";

    private async Task<(HttpClient Client, Guid UserId, string Email)> Account()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:3000");
        await Csrf(client);
        var email = $"{Guid.NewGuid():N}@example.test";
        await Data(client, "/auth/email-registration/code", "POST", new { email });
        var code = _factory.Emails.Sent[email];
        var result = await Data(client, "/auth/register", "POST", new { email, password = Password, nickname = "测试玩家",
            challenge_id = code.Challenge, verification_code = code.Code }, 201);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.GetProperty("access_token").GetString());
        await Csrf(client);
        return (client, result.GetProperty("user_id").GetGuid(), email);
    }
    private static async Task Csrf(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var csrf = await Data(client, "/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("request_token").GetString());
    }
    private static async Task<JsonElement> Data(HttpClient client, string path, string method = "GET", object? body = null, int expected = 200, bool idempotent = false)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1" + path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (idempotent) request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, $"{method} {path}: expected {expected}, actual {(int)response.StatusCode}; {SafeError(content)}");
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty(expected >= 400 ? "error" : "data").Clone();
    }
    private static string SafeError(string content)
    {
        using var json = JsonDocument.Parse(content);
        return json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object ? error.ToString() : "成功响应内容已隐藏";
    }
    private async Task<Guid> Profile(HttpClient client) => (await Data(client, "/profiles", "POST", new
    { name = "测试角色", server = "official", power = 5000, styles = new[] { "test_style" }, online_slots = new[] { new { weekday = 2, start_minute = 1200, end_minute = 1440 } } }, 201)).GetProperty("id").GetGuid();
    private static async Task<Guid> Guild(HttpClient client) => (await Data(client, "/guilds", "POST", new { name = "测试百业", server = "official" }, 201, true)).GetProperty("id").GetGuid();
    private static async Task Join(HttpClient owner, HttpClient member, Guid userId, Guid guildId)
    {
        var invitation = await Data(owner, $"/guilds/{guildId}/invitations", "POST", new { type = "direct", target_user_id = userId }, 201);
        await Data(member, $"/invitations/{invitation.GetProperty("id").GetGuid()}/accept", "POST", null, 200, true);
    }
    private static async Task<Guid> Event(HttpClient client, Guid guildId, DateTimeOffset? starts = null, int tank = 1, int healer = 0, int dps = 0)
    {
        var template = await Data(client, $"/guilds/{guildId}/dungeon-templates", "POST", new
        { name = "测试副本", need_tank = tank, need_healer = healer, need_dps = dps, default_duration_min = 60 }, 201);
        var result = await Data(client, $"/guilds/{guildId}/events", "POST", new
        { template_id = template.GetProperty("id").GetGuid(), starts_at = starts ?? DateTimeOffset.UtcNow.AddHours(3), signup_mode = "open" }, 201, true);
        return result.GetProperty("id").GetGuid();
    }
    [InfrastructureFact]
    public async Task RegistrationLoginRefreshAndRevocationUseRealStores()
    {
        var account = await Account(); using var client = account.Client;
        Assert.Equal(account.UserId, (await Data(client, "/me")).GetProperty("user_id").GetGuid());
        var wrong = await Data(client, "/auth/login", "POST", new { email = account.Email, password = "wrong" }, 401);
        Assert.Equal("INVALID_CREDENTIALS", wrong.GetProperty("code").GetString());
        await Data(client, "/auth/login", "POST", new { email = account.Email, password = Password });
        await Data(client, "/auth/refresh", "POST");
        await Data(client, "/auth/logout", "POST");
        var invalid = await Data(client, "/auth/refresh", "POST", expected: 401);
        Assert.Equal("REFRESH_TOKEN_INVALID", invalid.GetProperty("code").GetString());
        await Data(client, "/auth/register", "POST", new { email = account.Email, password = Password, challenge_id = _factory.Emails.Sent[account.Email].Challenge,
            verification_code = _factory.Emails.Sent[account.Email].Code }, 409);
    }

    [InfrastructureFact]
    public async Task FailedRegistrationSaveKeepsCodeForRetryAndDoesNotCreateAccount()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:3000");
        await Csrf(client);
        var email = $"{Guid.NewGuid():N}@example.test";
        await Data(client, "/auth/email-registration/code", "POST", new { email });
        var code = _factory.Emails.Sent[email];
        var body = new { email, password = Password, challenge_id = code.Challenge, verification_code = code.Code };
        _factory.SaveFailure.FailNext = true;
        Assert.Equal("DEPENDENCY_UNAVAILABLE", (await Data(client, "/auth/register", "POST", body, 503)).GetProperty("code").GetString());
        using (var scope = _factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<YanyunDbContext>().Users.AnyAsync(user => user.Email == email));
        await Data(client, "/auth/register", "POST", body, 201);
    }

    [InfrastructureFact]
    public async Task CancelledRefreshDoesNotRotateTheCurrentToken()
    {
        var account = await Account();
        using var client = account.Client;
        var store = _factory.Services.GetRequiredService<AuthRedisStore>();
        var session = await store.CreateSessionAsync(account.UserId);
        using var scope = _factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => auth.RefreshAsync(session.Token, cancellation.Token));
        Assert.Equal(account.UserId, await store.ValidateSessionAsync(session.Token));
        Assert.NotEmpty((await auth.RefreshAsync(session.Token, CancellationToken.None)).Auth.AccessToken);
    }

    [InfrastructureFact]
    public async Task OptionalCreationKeyReturnsOriginalProfileAndRejectsDifferentPayload()
    {
        var account = await Account();
        using var client = account.Client;
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var body = new { name = "幂等角色", server = "official", power = 1, styles = new[] { "test_style" } };
        var first = await Data(client, "/profiles", "POST", body, 201);
        var second = await Data(client, "/profiles", "POST", body, 201);
        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Single((await Data(client, "/profiles")).GetProperty("items").EnumerateArray());
        var error = await Data(client, "/profiles", "POST", new { name = "另一个角色", server = "official", power = 1, styles = new[] { "test_style" } }, 409);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", error.GetProperty("code").GetString());
    }

    [InfrastructureFact]
    public async Task PlayerPageUsesFixedBatchQueriesForMultipleUsers()
    {
        var account = await Account();
        using var client = account.Client;
        await Profile(client);
        _factory.Queries.Reset();
        Assert.Single((await Data(client, "/players")).GetProperty("items").EnumerateArray());
        var singleCount = _factory.Queries.Count;
        for (var index = 0; index < 3; index++)
        {
            var other = await Account();
            using var otherClient = other.Client;
            await Profile(otherClient);
        }
        _factory.Queries.Reset();
        Assert.Equal(4, (await Data(client, "/players")).GetProperty("items").GetArrayLength());
        Assert.Equal(singleCount, _factory.Queries.Count);
        Assert.InRange(singleCount, 1, 6);
    }
    [InfrastructureFact]
    public async Task AnonymousWritesRequireCsrfAndAllowedOrigin()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        var rejected = await Data(client, "/auth/login", "POST", new { email = "nobody@example.test", password = "password" }, 403);
        Assert.Equal("ORIGIN_REJECTED", rejected.GetProperty("code").GetString());
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "http://localhost:3000");
        var csrf = await Data(client, "/auth/login", "POST", new { email = "nobody@example.test", password = "password" }, 400);
        Assert.Equal("CSRF_INVALID", csrf.GetProperty("code").GetString());
    }
    [InfrastructureFact]
    public async Task ConcurrentSignupCannotOverbookAndAssignCannotWaitlist()
    {
        var owner = await Account(); var member = await Account();
        using var a = owner.Client; using var b = member.Client;
        var profileA = await Profile(a); var profileB = await Profile(b); var guild = await Guild(a); await Join(a, b, member.UserId, guild);
        var raid = await Event(a, guild);
        var results = await Task.WhenAll(Data(a, $"/events/{raid}/assignments", "POST", new { action = "signup", profile_id = profileA, slot_role = "tank" }, idempotent: true),
            Data(b, $"/events/{raid}/assignments", "POST", new { action = "signup", profile_id = profileB, slot_role = "tank" }, idempotent: true));
        Assert.Single(results, result => result.GetProperty("status").GetString() == "confirmed");
        Assert.Single(results, result => result.GetProperty("status").GetString() == "waitlist");
        var confirmed = results.Single(result => result.GetProperty("status").GetString() == "confirmed");
        await Data(a, $"/events/{raid}/assignments/{confirmed.GetProperty("assignment_id").GetGuid()}", "DELETE");
        var detail = await Data(a, $"/guilds/{guild}/events/{raid}");
        Assert.Equal(1, detail.GetProperty("confirmed_assignments").GetArrayLength()); Assert.Equal(0, detail.GetProperty("waitlist_assignments").GetArrayLength());
        var vacantProfile = results[0].GetProperty("status").GetString() == "confirmed" ? profileA : profileB;
        var full = await Data(a, $"/events/{raid}/assignments", "POST", new { action = "assign", profile_id = vacantProfile, slot_role = "tank" }, 409, true);
        Assert.Equal("CAPACITY_CONFLICT", full.GetProperty("code").GetString());
        await Data(b, $"/guilds/{guild}/members/{(await Data(a, "/me")).GetProperty("membership").GetProperty("membership_id").GetGuid()}/role", "PATCH", new { role = "admin", expected_version = 2 }, 403);
    }
    [InfrastructureFact]
    public async Task CurrentWeekStatisticsExcludeFutureWeekAndCleanupDeletesStartedEvents()
    {
        var owner = await Account(); var member = await Account(); using var a = owner.Client; using var b = member.Client;
        var pa = await Profile(a); var pb = await Profile(b); var guild = await Guild(a); await Join(a, b, member.UserId, guild);
        var current = await Event(a, guild, DateTimeOffset.UtcNow.AddHours(1));
        var next = await Event(a, guild, BusinessOperations.Week().End.AddHours(10));
        await Data(a, $"/events/{current}/assignments", "POST", new { action = "assign", profile_id = pb, slot_role = "tank" }, idempotent: true);
        await Data(a, $"/events/{next}/assignments", "POST", new { action = "signup", profile_id = pa, slot_role = "tank" }, idempotent: true);
        var stats = await Data(a, $"/guilds/{guild}/participation");
        var rows = stats.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(1, rows.Single(row => row.GetProperty("user_id").GetGuid() == member.UserId).GetProperty("cumulative_count").GetInt32());
        Assert.Equal(0, rows.Single(row => row.GetProperty("user_id").GetGuid() == owner.UserId).GetProperty("cumulative_count").GetInt32());
        using (var scope = _factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<YanyunDbContext>();
            await database.Events.Where(raid => raid.Id == current).ExecuteUpdateAsync(update => update.SetProperty(raid => raid.StartsAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        var context = await Data(a, "/me");
        var result = await Data(a, $"/guilds/{guild}/leave", "POST", new { expected_version = context.GetProperty("membership").GetProperty("guild_version").GetInt64(), confirm_guild_id = guild, confirm_dissolve = true });
        Assert.True(result.GetProperty("guild_dissolved").GetBoolean());
        await Data(a, $"/guilds/{guild}", expected: 404);
        Assert.Equal(JsonValueKind.Null, (await Data(b, "/me")).GetProperty("membership").ValueKind);
        Assert.Equal(1, (await Data(b, "/profiles")).GetProperty("items").GetArrayLength());
        using var verify = _factory.Services.CreateScope();var db = verify.ServiceProvider.GetRequiredService<YanyunDbContext>();
        Assert.False(await db.Events.AnyAsync(raid=>raid.GuildId==guild));Assert.False(await db.Assignments.AnyAsync());
    }
    [InfrastructureFact]
    public async Task ProfileOwnershipAndAccountHardDeletionAreEnforced()
    {
        var owner=await Account();var other=await Account();using var a=owner.Client;using var b=other.Client;
        var profile=await Profile(a);await Data(b,$"/profiles/{profile}",expected:404);
        await Data(a,"/me","PATCH",new{default_profile_id=profile});
        var invalid=await Data(a,"/profiles","POST",new{name="无效角色",server="official",power=1,styles=new[]{"unknown"}},400);
        Assert.Equal("VALIDATION_FAILED",invalid.GetProperty("code").GetString());
        await Data(a,"/me/account-deletion","POST",new{confirm_user_id=owner.UserId,confirmation="DELETE"});
        var deleted=await Data(a,"/me",expected:401);Assert.Equal("ACCOUNT_NOT_FOUND",deleted.GetProperty("code").GetString());
        using var scope=_factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<YanyunDbContext>();
        Assert.False(await db.Profiles.AnyAsync(row=>row.UserId==owner.UserId));Assert.True(await db.Users.AnyAsync(row=>row.Id==other.UserId));
    }

    [InfrastructureFact]
    public async Task RedisChallengesAreBoundedSingleUseAndRefreshReplayRevokesFamily()
    {
        var store = _factory.Services.GetRequiredService<AuthRedisStore>();
        var challenge = Guid.NewGuid(); var email = "codes@example.test";
        await store.ReserveCodeAsync(challenge, email, "123456");
        await Assert.ThrowsAsync<Yanyun.Api.Common.BusinessException>(()=>store.ConsumeCodeAsync(challenge,email,"123456"));
        await store.ActivateCodeAsync(challenge);
        await Assert.ThrowsAsync<Yanyun.Api.Common.BusinessException>(()=>store.ConsumeCodeAsync(challenge,"other@example.test","123456"));
        await store.ConsumeCodeAsync(challenge,email,"123456");
        await Assert.ThrowsAsync<Yanyun.Api.Common.BusinessException>(()=>store.ConsumeCodeAsync(challenge,email,"123456"));
        var exhausted = Guid.NewGuid(); await store.ReserveCodeAsync(exhausted,"attempts@example.test","123456"); await store.ActivateCodeAsync(exhausted);
        for(var i=0;i<5;i++)await Assert.ThrowsAsync<Yanyun.Api.Common.BusinessException>(()=>store.ConsumeCodeAsync(exhausted,"attempts@example.test","000000"));
        await Assert.ThrowsAsync<Yanyun.Api.Common.BusinessException>(()=>store.ConsumeCodeAsync(exhausted,"attempts@example.test","123456"));
        var user=Guid.NewGuid();var session=await store.CreateSessionAsync(user);var rotated=await store.RotateSessionAsync(session.Token);
        Assert.Equal(session.Expires.ToUnixTimeSeconds(),rotated.Expires.ToUnixTimeSeconds());
        Assert.Equal(rotated.Token, (await store.RotateSessionAsync(session.Token)).Token);
        Assert.Equal(user, await store.ValidateSessionAsync(session.Token));
        await Task.Delay(TimeSpan.FromSeconds(11));
        await Assert.ThrowsAsync<Yanyun.Api.Common.BusinessException>(()=>store.RotateSessionAsync(session.Token));
        await Assert.ThrowsAsync<Yanyun.Api.Common.BusinessException>(()=>store.RotateSessionAsync(rotated.Token));
        var one=await store.CreateSessionAsync(user);var two=await store.CreateSessionAsync(user);await store.RevokeUserSessionsAsync(user);
        await Assert.ThrowsAsync<Yanyun.Api.Common.BusinessException>(()=>store.RotateSessionAsync(one.Token));
        await Assert.ThrowsAsync<Yanyun.Api.Common.BusinessException>(()=>store.RotateSessionAsync(two.Token));
    }

    [InfrastructureFact]
    public async Task ApplicationsInvitationsIdempotencyAndMemberCleanupUseActualRules()
    {
        var owner=await Account();var member=await Account();using var a=owner.Client;using var b=member.Client;
        await Profile(a);var pb=await Profile(b);var guild=await Guild(a);
        var application=await Data(b,$"/guilds/{guild}/applications","POST",new{message="申请加入"},201);
        var appId=application.GetProperty("id").GetGuid();
        await Data(b,$"/applications/{appId}/withdraw","POST");
        application=await Data(b,$"/guilds/{guild}/applications","POST",new{message="重新申请"},201);appId=application.GetProperty("id").GetGuid();
        await Data(a,$"/applications/{appId}","PATCH",new{decision="approved",expected_status="pending"},idempotent:true);
        var membership=(await Data(b,"/me")).GetProperty("membership").GetProperty("membership_id").GetGuid();
        var version=(await Data(a,"/me")).GetProperty("membership").GetProperty("guild_version").GetInt64();
        await Data(a,$"/guilds/{guild}/members/{membership}/role","PATCH",new{role="admin",expected_version=version});
        var raid=await Event(b,guild);var key=Guid.NewGuid().ToString();
        async Task<JsonElement> Signup(string role,int expected=200)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/events/{raid}/assignments");
            request.Headers.Add("Idempotency-Key",key);request.Content=JsonContent.Create(new{action="signup",profile_id=pb,slot_role=role});
            using var response=await b.SendAsync(request);Assert.Equal(expected,(int)response.StatusCode);
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.GetProperty(expected==200?"data":"error").Clone();
        }
        var first=await Signup("tank");var second=await Signup("tank");Assert.Equal(first.GetProperty("assignment_id").GetGuid(),second.GetProperty("assignment_id").GetGuid());
        Assert.Equal("IDEMPOTENCY_KEY_REUSED",(await Signup("dps",409)).GetProperty("code").GetString());
        using(var scope=_factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<YanyunDbContext>();
            await db.Events.Where(row=>row.Id==raid).ExecuteUpdateAsync(update=>update.SetProperty(row=>row.StartsAt,DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        version=(await Data(b,"/me")).GetProperty("membership").GetProperty("guild_version").GetInt64();
        await Data(b,$"/guilds/{guild}/leave","POST",new{expected_version=version,confirm_guild_id=guild});
        await Data(a,$"/guilds/{guild}/events/{raid}",expected:404);
        using var check=_factory.Services.CreateScope();var database=check.ServiceProvider.GetRequiredService<YanyunDbContext>();
        Assert.False(await database.Templates.AnyAsync(row=>row.CreatedBy==member.UserId));
        Assert.False(await database.Applications.AnyAsync(row=>row.UserId==member.UserId));
        Assert.False(await database.Idempotency.AnyAsync(row=>row.RelatedUserIds.Contains(member.UserId)));
        Assert.True(await database.Guilds.AnyAsync(row=>row.Id==guild));Assert.True(await database.Profiles.AnyAsync(row=>row.Id==pb));
    }

    [InfrastructureFact]
    public async Task RemindersReadCursorAndSignalRDeliverRealNotifications()
    {
        var account=await Account();using var client=account.Client;var profile=await Profile(client);var guild=await Guild(client);
        var raid=await Event(client,guild,DateTimeOffset.UtcNow.AddMinutes(20));
        await Data(client,$"/events/{raid}/assignments","POST",new{action="signup",profile_id=profile,slot_role="tank"},idempotent:true);
        using var negotiate=await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1",null);negotiate.EnsureSuccessStatusCode();
        using var negotiation=JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync());
        var connection=Uri.EscapeDataString(negotiation.RootElement.GetProperty("connectionToken").GetString()!);var hub=$"/hubs/notifications?id={connection}";
        using(var poll=await client.GetAsync(hub))poll.EnsureSuccessStatusCode();
        using(var handshake=await client.PostAsync(hub,new StringContent("{\"protocol\":\"json\",\"version\":1}\u001e")))handshake.EnsureSuccessStatusCode();
        using(var poll=await client.GetAsync(hub))Assert.Contains("{}",await poll.Content.ReadAsStringAsync());
        async Task Tick()
        {
            using var scope=_factory.Services.CreateScope();
            await ReminderService.RunOnceAsync(scope.ServiceProvider,CancellationToken.None);
        }
        await Tick();
        using(var poll=await client.GetAsync(hub))Assert.Contains("notifications.changed",await poll.Content.ReadAsStringAsync());
        Assert.Equal(2,(await Data(client,"/notifications/unread-count")).GetProperty("unread_count").GetInt32());
        await Tick();Assert.Equal(2,(await Data(client,"/notifications/unread-count")).GetProperty("unread_count").GetInt32());
        var page=await Data(client,"/notifications?limit=1");var cursor=page.GetProperty("next_cursor").GetString();Assert.NotNull(cursor);
        Assert.Single((await Data(client,$"/notifications?limit=1&cursor={Uri.EscapeDataString(cursor!)}")).GetProperty("items").EnumerateArray());
        await Data(client,"/notifications/read","POST",new{all=true});Assert.Equal(0,(await Data(client,"/notifications/unread-count")).GetProperty("unread_count").GetInt32());
        using var disconnect=await client.DeleteAsync(hub);disconnect.EnsureSuccessStatusCode();
    }

    [InfrastructureFact]
    public async Task TimeConflictsIgnoreWaitlistsUseHalfOpenIntervalsAndDiscoveryFiltersJsonb()
    {
        var owner=await Account();var member=await Account();using var a=owner.Client;using var b=member.Client;
        var pa=await Profile(a);var pb=await Profile(b);var guild=await Guild(a);await Join(a,b,member.UserId,guild);
        var start=DateTimeOffset.UtcNow.AddHours(3);var one=await Event(a,guild,start);var two=await Event(a,guild,start);var boundary=await Event(a,guild,start.AddMinutes(60));
        await Data(a,$"/events/{one}/assignments","POST",new{action="signup",profile_id=pa,slot_role="tank"},idempotent:true);
        Assert.Equal("ACCOUNT_TIME_CONFLICT",(await Data(a,$"/events/{two}/assignments","POST",new{action="signup",profile_id=pa,slot_role="tank"},409,true)).GetProperty("code").GetString());
        var memberAssignment=await Data(b,$"/events/{two}/assignments","POST",new{action="signup",profile_id=pb,slot_role="tank"},idempotent:true);
        Assert.Equal("waitlist",(await Data(a,$"/events/{two}/assignments","POST",new{action="signup",profile_id=pa,slot_role="tank"},idempotent:true)).GetProperty("status").GetString());
        await Data(b,$"/events/{two}/assignments/{memberAssignment.GetProperty("assignment_id").GetGuid()}","DELETE");
        var detail=await Data(a,$"/guilds/{guild}/events/{two}");Assert.Empty(detail.GetProperty("confirmed_assignments").EnumerateArray());Assert.Single(detail.GetProperty("waitlist_assignments").EnumerateArray());
        Assert.Equal("confirmed",(await Data(a,$"/events/{boundary}/assignments","POST",new{action="signup",profile_id=pa,slot_role="tank"},idempotent:true)).GetProperty("status").GetString());
        var outsider=await Account();using var c=outsider.Client;await Profile(c);
        var found=await Data(a,"/players?weekday=2&start_minute=1250&end_minute=1300&style=test_style&server=official");
        Assert.Single(found.GetProperty("items").EnumerateArray());Assert.Equal(outsider.UserId,found.GetProperty("items")[0].GetProperty("user_id").GetGuid());
        Assert.Empty((await Data(a,"/players?weekday=2&start_minute=1100&end_minute=1200")).GetProperty("items").EnumerateArray());
        await Data(c,$"/guilds/{guild}/events/{one}",expected:404);
        await Data(b,$"/guilds/{guild}/participation",expected:403);
    }
}
