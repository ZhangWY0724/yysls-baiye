using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Configuration;
using Yanyun.Api.Infrastructure.Redis;

namespace Yanyun.Api.Modules.Auth;

public sealed class AuthRedisStore(RedisConnection connection, IOptions<RedisOptions> options, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Yanyun.RefreshRetry.v1");
    private string Key(string value) => options.Value.KeyPrefix + "auth:" + value;
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string RandomToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private async Task<IDatabase> DatabaseAsync()
    {
        try { return (await connection.GetAsync()).GetDatabase(); }
        catch (RedisException) { throw new BusinessException(503, "REFRESH_STORE_UNAVAILABLE", "认证存储暂不可用"); }
    }

    public async Task LimitAsync(string action, string identity, int maximum, int seconds)
    {
        var database = await DatabaseAsync();
        var result = (long)await database.ScriptEvaluateAsync("""
            local count = redis.call('INCR', KEYS[1])
            if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
            if count > tonumber(ARGV[2]) then return redis.call('TTL', KEYS[1]) end
            return 0
            """, [Key($"rate:{action}:{Hash(identity)}")], [seconds, maximum]);
        if (result > 0) throw new BusinessException(429, "RATE_LIMITED", "操作过于频繁，请稍后重试", new RetryDelay(result));
    }

    public async Task ReserveCodeAsync(Guid challenge, string email, string code)
    {
        var database = await DatabaseAsync();
        var result = (long)await database.ScriptEvaluateAsync("""
            if not redis.call('SET', KEYS[1], ARGV[1], 'NX', 'EX', 60) then return 0 end
            redis.call('HSET', KEYS[2], 'email', ARGV[2], 'code', ARGV[3], 'state', 'pending', 'attempts', 0)
            redis.call('EXPIRE', KEYS[2], 600)
            return 1
            """, [Key($"code-cooldown:{Hash(email)}"), Key($"registration:{challenge:D}")],
            [challenge.ToString("D"), email, Hash($"{challenge:D}:{code}")]);
        if (result == 0) throw new BusinessException(429, "RATE_LIMITED", "请等待 60 秒后再发送验证码", new RetryDelay(60));
    }

    public async Task ActivateCodeAsync(Guid challenge)
    {
        var database = await DatabaseAsync();
        await database.ScriptEvaluateAsync("""
            if redis.call('EXISTS', KEYS[1]) == 1 then
              redis.call('HSET', KEYS[1], 'state', 'active') return 1
            end
            return 0
            """, [Key($"registration:{challenge:D}")]);
    }

    public async Task CancelCodeAsync(Guid challenge, string email)
    {
        var database = await DatabaseAsync();
        await database.ScriptEvaluateAsync("""
            redis.call('DEL', KEYS[2])
            if redis.call('GET', KEYS[1]) == ARGV[1] then redis.call('DEL', KEYS[1]) end
            return 1
            """, [Key($"code-cooldown:{Hash(email)}"), Key($"registration:{challenge:D}")], [challenge.ToString("D")]);
    }

    public Task ConsumeCodeAsync(Guid challenge, string email, string code) => VerifyCodeAsync(challenge, email, code, true);

    public async Task VerifyCodeAsync(Guid challenge, string email, string code, bool consume = false)
    {
        var database = await DatabaseAsync();
        var result = (long)await database.ScriptEvaluateAsync("""
            if redis.call('HGET', KEYS[1], 'state') ~= 'active' then return 0 end
            if redis.call('HGET', KEYS[1], 'email') ~= ARGV[1] then return 0 end
            if redis.call('HGET', KEYS[1], 'code') ~= ARGV[2] then
              local attempts = redis.call('HINCRBY', KEYS[1], 'attempts', 1)
              if attempts >= 5 then redis.call('DEL', KEYS[1]) end
              return 0
            end
            if ARGV[3] == '1' then redis.call('DEL', KEYS[1]) end
            return 1
            """, [Key($"registration:{challenge:D}")], [email, Hash($"{challenge:D}:{code}"), consume ? "1" : "0"]);
        if (result == 0) throw new BusinessException(400, "VERIFICATION_CODE_INVALID", "验证码错误、已使用或已过期");
    }

    public async Task<(string Token, DateTimeOffset Expires)> CreateSessionAsync(Guid userId)
    {
        var database = await DatabaseAsync();
        var sessionId = Guid.NewGuid().ToString("N");
        var secret = RandomToken();
        var expires = DateTimeOffset.UtcNow.AddDays(7);
        await database.ScriptEvaluateAsync("""
            redis.call('HSET', KEYS[1], 'user_id', ARGV[1], 'hash', ARGV[2], 'expires', ARGV[3])
            redis.call('EXPIRE', KEYS[1], 604800)
            redis.call('SADD', KEYS[2], ARGV[4]) redis.call('EXPIRE', KEYS[2], 604800) return 1
            """, [Key($"session:{sessionId}"), Key($"user-sessions:{userId:D}")],
            [userId.ToString("D"), Hash(secret), expires.ToUnixTimeSeconds(), sessionId]);
        return ($"{sessionId}.{secret}", expires);
    }

    public async Task<(Guid UserId, string Token, DateTimeOffset Expires)> RotateSessionAsync(string? token)
    {
        var parts = ParseToken(token);
        var database = await DatabaseAsync();
        var secret = RandomToken();
        var protectedToken = _protector.Protect($"{parts[0]}.{secret}");
        var result = (RedisResult[]?)await database.ScriptEvaluateAsync("""
            local stored = redis.call('HGET', KEYS[1], 'hash')
            if not stored then return {} end
            if stored ~= ARGV[1] then
              if redis.call('HGET', KEYS[3], 'hash') == ARGV[1] then
                return {redis.call('HGET', KEYS[1], 'user_id'), redis.call('HGET', KEYS[1], 'expires'), redis.call('HGET', KEYS[3], 'token')}
              end
              if redis.call('SISMEMBER', KEYS[2], ARGV[1]) == 1 then redis.call('DEL', KEYS[1], KEYS[2], KEYS[3]) end
              return {}
            end
            redis.call('SADD', KEYS[2], stored)
            redis.call('EXPIRE', KEYS[2], redis.call('TTL', KEYS[1]))
            redis.call('HSET', KEYS[1], 'hash', ARGV[2])
            redis.call('HSET', KEYS[3], 'hash', stored, 'token', ARGV[3])
            redis.call('EXPIRE', KEYS[3], 10)
            return {redis.call('HGET', KEYS[1], 'user_id'), redis.call('HGET', KEYS[1], 'expires'), ARGV[3]}
            """, [Key($"session:{parts[0]}"), Key($"session-used:{parts[0]}"), Key($"session-retry:{parts[0]}")], [Hash(parts[1]), Hash(secret), protectedToken]);
        if (result is not { Length: 3 }) throw InvalidRefresh();
        return (Guid.Parse((string)result[0]!), _protector.Unprotect((string)result[2]!),
            DateTimeOffset.FromUnixTimeSeconds(long.Parse((string)result[1]!, System.Globalization.CultureInfo.InvariantCulture)));
    }

    public async Task<Guid> ValidateSessionAsync(string? token)
    {
        var parts = ParseToken(token);
        var database = await DatabaseAsync();
        var result = (string?)await database.ScriptEvaluateAsync("""
            local stored = redis.call('HGET', KEYS[1], 'hash')
            if not stored then return false end
            if stored ~= ARGV[1] then
              if redis.call('HGET', KEYS[3], 'hash') == ARGV[1] then return redis.call('HGET', KEYS[1], 'user_id') end
              if redis.call('SISMEMBER', KEYS[2], ARGV[1]) == 1 then redis.call('DEL', KEYS[1], KEYS[2], KEYS[3]) end
              return false
            end
            return redis.call('HGET', KEYS[1], 'user_id')
            """, [Key($"session:{parts[0]}"), Key($"session-used:{parts[0]}"), Key($"session-retry:{parts[0]}")], [Hash(parts[1])]);
        return Guid.TryParse(result, out var userId) ? userId : throw InvalidRefresh();
    }

    public async Task RevokeSessionAsync(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        var parts = ParseToken(token);
        var database = await DatabaseAsync();
        await database.ScriptEvaluateAsync("""
            if redis.call('HGET', KEYS[1], 'hash') == ARGV[1]
               or redis.call('SISMEMBER', KEYS[2], ARGV[1]) == 1 then
              redis.call('DEL', KEYS[1], KEYS[2], KEYS[3])
            end
            return 1
            """, [Key($"session:{parts[0]}"), Key($"session-used:{parts[0]}"), Key($"session-retry:{parts[0]}")], [Hash(parts[1])]);
    }

    public async Task RevokeUserSessionsAsync(Guid userId)
    {
        var database = await DatabaseAsync();
        await database.ScriptEvaluateAsync("""
            local sessions = redis.call('SMEMBERS', KEYS[1])
            for _, session in ipairs(sessions) do
              redis.call('DEL', ARGV[1] .. 'session:' .. session, ARGV[1] .. 'session-used:' .. session, ARGV[1] .. 'session-retry:' .. session)
            end
            redis.call('DEL', KEYS[1]) return 1
            """, [Key($"user-sessions:{userId:D}")], [Key("")]);
    }

    private static string[] ParseToken(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 2 } || !Guid.TryParseExact(parts[0], "N", out _) ||
            parts[1].Length != 64 || !parts[1].All(Uri.IsHexDigit)) throw InvalidRefresh();
        return parts;
    }
    private static BusinessException InvalidRefresh() => new(401, "REFRESH_TOKEN_INVALID", "登录会话已失效，请重新登录");
}
