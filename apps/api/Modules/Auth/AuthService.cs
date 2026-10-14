using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Configuration;
using Yanyun.Api.Infrastructure.Email;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Users;

namespace Yanyun.Api.Modules.Auth;

public sealed class AuthService(YanyunDbContext database, AuthRedisStore store,
    IVerificationEmailSender emails, IOptions<AuthOptions> options, ILogger<AuthService> logger)
{
    private static readonly PasswordHasher<User> Hasher = new();
    private static readonly User DummyUser = new();
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, AuthRedisStore.RandomToken());

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public async Task<CodeChallenge> SendCodeAsync(string email, string ip, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        await store.LimitAsync("send-ip", ip, 10, 3600);
        await store.LimitAsync("send-email", normalized, 5, 3600);
        if (await database.Users.AnyAsync(user => user.NormalizedEmail == normalized, cancellationToken))
            throw new BusinessException(409, "EMAIL_ALREADY_IN_USE", "该邮箱已注册，请直接登录");
        var challenge = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        await store.ReserveCodeAsync(challenge, normalized, code);
        try
        {
            await emails.SendRegistrationAsync(email.Trim(), code, challenge, cancellationToken);
            await store.ActivateCodeAsync(challenge);
        }
        catch
        {
            await store.CancelCodeAsync(challenge, normalized);
            throw;
        }
        return new(challenge, 600, 60);
    }

    public async Task<(Guid UserId, SessionResult Session)> RegisterAsync(RegisterRequest request,
        string ip, CancellationToken cancellationToken)
    {
        await store.LimitAsync("register-ip", ip, 20, 600);
        var normalized = NormalizeEmail(request.Email);
        var nickname = request.Nickname?.Trim();
        if (nickname is { Length: 0 }) throw new BusinessException(400, "VALIDATION_FAILED", "昵称不能为空");
        if (await database.Users.AnyAsync(user => user.NormalizedEmail == normalized, cancellationToken))
            throw new BusinessException(409, "EMAIL_ALREADY_IN_USE", "该邮箱已注册");
        await store.VerifyCodeAsync(request.ChallengeId, normalized, request.VerificationCode);
        var user = new User
        {
            Email = request.Email.Trim(), NormalizedEmail = normalized,
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            Nickname = nickname ?? $"玩家{RandomNumberGenerator.GetInt32(100000, 1000000)}"
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        database.Users.Add(user);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        SessionResult? session = null;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            session = await CreateSessionAsync(user);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        { throw new BusinessException(409, "EMAIL_ALREADY_IN_USE", "该邮箱已注册"); }
        catch
        {
            if (session is not null)
            {
                try { await store.RevokeSessionAsync(session.RefreshToken); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                { logger.LogWarning("注册失败会话清理未完成：{ExceptionType}", exception.GetType().Name); }
            }
            throw;
        }
        // 数据库唯一邮箱约束阻止重复注册；清理失败不能把已成功注册返回成失败。
        try { await store.CancelCodeAsync(request.ChallengeId, normalized); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { logger.LogWarning("注册验证码清理未完成：{ExceptionType}", exception.GetType().Name); }
        return (user.Id, session);
    }

    public async Task<SessionResult> LoginAsync(LoginRequest request, string ip, CancellationToken cancellationToken)
    {
        await store.LimitAsync("login-ip", ip, 30, 600);
        var normalized = NormalizeEmail(request.Email);
        await store.LimitAsync("login-email", normalized, 10, 600);
        var user = await database.Users.SingleOrDefaultAsync(account => account.NormalizedEmail == normalized, cancellationToken);
        var result = Hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, request.Password);
        if (user is null || user.PasswordHash is null || result == PasswordVerificationResult.Failed)
            throw new BusinessException(401, "INVALID_CREDENTIALS", "邮箱或密码不正确");
        if (user.Status != "active") throw new BusinessException(403, "ACCOUNT_DISABLED", "账号不可用");
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, request.Password);
            await database.SaveChangesAsync(cancellationToken);
        }
        return await CreateSessionAsync(user);
    }

    public async Task<SessionResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        var userId = await store.ValidateSessionAsync(refreshToken);
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(account => account.Id == userId, cancellationToken);
        if (user is null || user.Status != "active")
        {
            await store.RevokeSessionAsync(refreshToken);
            throw new BusinessException(user is null ? 401 : 403,
                user is null ? "ACCOUNT_NOT_FOUND" : "ACCOUNT_DISABLED", "账号不可用");
        }
        var auth = IssueToken(user);
        var rotation = await store.RotateSessionAsync(refreshToken);
        return new(auth, rotation.Token, rotation.Expires);
    }

    private async Task<SessionResult> CreateSessionAsync(User user)
    {
        var auth = IssueToken(user);
        var session = await store.CreateSessionAsync(user.Id);
        return new(auth, session.Token, session.Expires);
    }

    private AuthResult IssueToken(User user)
    {
        var now = DateTime.UtcNow;
        var settings = options.Value;
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim("sub", user.Id.ToString("D")), new Claim("jti", Guid.NewGuid().ToString("N"))],
            now, now.AddMinutes(settings.AccessTokenMinutes),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", settings.AccessTokenMinutes * 60);
    }
}
