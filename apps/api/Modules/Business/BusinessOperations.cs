using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Auth;

namespace Yanyun.Api.Modules.Business;

public sealed class BusinessOperations(YanyunDbContext database, IHttpContextAccessor accessor, NotificationPublisher publisher)
{
    public Guid UserId => Guid.Parse(accessor.HttpContext!.User.FindFirstValue("sub")!);
    public HttpContext Context => accessor.HttpContext!;
    public static BusinessException Invalid(string message) => new(400, "VALIDATION_FAILED", message);
    public static BusinessException Missing() => new(404, "RESOURCE_NOT_FOUND", "资源不存在或已失效");
    public static BusinessException Forbidden() => new(403, "PERMISSION_DENIED", "你没有此操作的权限");
    public static BusinessException Conflict(string code, string message) => new(409, code, message);

    // 写操作按百业串行（无百业时按操作者），保证容量、候补和成员变更的检查与写入原子；不同百业互不等待。
    // 跨百业的唯一性（一个账号只能加入一个百业等）由数据库唯一约束兜底。
    private async Task<IDbContextTransaction> BeginAsync(Guid scope, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({scope.ToString("D")}, 0))", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<T> WriteAsync<T>(string operation, object request, Guid? guildId, Guid? eventId,
        bool idempotent, Func<Task<T>> action, CancellationToken cancellationToken) where T : notnull
    {
        await using var transaction = await BeginAsync(guildId ?? UserId, cancellationToken);
        var account = await database.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == UserId, cancellationToken)
            ?? throw new BusinessException(401, "ACCOUNT_NOT_FOUND", "账号已注销");
        if (account.Status != "active") throw new BusinessException(403, "ACCOUNT_DISABLED", "账号不可用");
        Guid key = default;
        // 旧调用保持兼容；任何写操作显式提供标识时都启用幂等记录。
        idempotent = idempotent || Context.Request.Headers.ContainsKey("Idempotency-Key");
        var hash = AuthRedisStore.Hash(BusinessJson.Serialize(request));
        if (idempotent)
        {
            if (!Guid.TryParse(Context.Request.Headers["Idempotency-Key"], out key))
                throw Invalid("需要有效的 Idempotency-Key");
            var previous = await database.Idempotency.SingleOrDefaultAsync(record => record.UserId == UserId &&
                record.Operation == operation && record.Key == key, cancellationToken);
            if (previous is not null && previous.ExpiresAt > DateTimeOffset.UtcNow)
            {
                if (previous.RequestHash != hash) throw Conflict("IDEMPOTENCY_KEY_REUSED", "同一请求标识不能用于不同内容");
                return JsonSerializer.Deserialize<T>(previous.ResponseJson, BusinessJson.Options)!;
            }
            if (previous is not null) database.Idempotency.Remove(previous);
        }
        var result = await action();
        var relatedUsers = database.ChangeTracker.Entries<Assignment>().Select(entry => entry.Entity.UserId)
            .Concat(database.ChangeTracker.Entries<Membership>().Select(entry => entry.Entity.UserId))
            .Concat(database.ChangeTracker.Entries<Notification>().SelectMany(entry => entry.Entity.RelatedUserIds))
            .Append(UserId).Distinct().ToArray();
        if (operation.StartsWith("member.role:", StringComparison.Ordinal) || operation.StartsWith("guild.transfer:", StringComparison.Ordinal) ||
            operation.StartsWith("event.patch:", StringComparison.Ordinal) || operation.StartsWith("event.cancel:", StringComparison.Ordinal) ||
            operation.StartsWith("assignment.cancel:", StringComparison.Ordinal))
            Audit(operation.Split(':')[0], eventId ?? guildId ?? UserId, guildId, eventId, relatedUsers);
        if (idempotent) database.Idempotency.Add(new()
        {
            UserId = UserId, Operation = operation, Key = key, RequestHash = hash,
            ResponseJson = BusinessJson.Serialize(result), GuildId = guildId, EventId = eventId,
            RelatedUserIds = relatedUsers
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        await publisher.FlushAsync();
        return result;
    }

    public async Task<Membership> MemberAsync(Guid guildId, bool manage, bool owner, CancellationToken token)
    {
        var member = await database.Memberships.SingleOrDefaultAsync(member => member.GuildId == guildId && member.UserId == UserId, token)
            ?? throw Missing();
        if (owner && member.Role != "owner" || manage && member.Role == "member") throw Forbidden();
        return member;
    }
    public async Task<Guild> GuildAsync(Guid guildId, CancellationToken token) =>
        await database.Guilds.SingleOrDefaultAsync(guild => guild.Id == guildId, token) ?? throw Missing();
    public static void Version(long actual, long? expected)
    {
        if (expected is null) throw Invalid("需要 expected_version");
        if (actual != expected) throw new BusinessException(409, "VERSION_CONFLICT", "数据已更新，请刷新后重试", new { CurrentVersion = actual });
    }
    public long HeaderVersion()
    {
        if (!long.TryParse(Context.Request.Headers["X-Expected-Version"], out var version)) throw Invalid("需要 X-Expected-Version");
        return version;
    }
    public void ConfirmHeader(string name, Guid id)
    {
        if (!Guid.TryParse(Context.Request.Headers[name], out var confirm) || confirm != id) throw Invalid("删除确认不匹配");
    }
    public static string Text(string? value, string field, int max, bool required = false)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length > max || required && trimmed.Length == 0 || trimmed.Contains('<') || trimmed.Contains('>'))
            throw Invalid($"{field} 格式不正确");
        return trimmed;
    }
    public static void Capacity(int tank, int healer, int dps)
    {
        if (tank < 0 || healer < 0 || dps < 0 || tank > 10 || healer > 10 || dps > 10 || tank + healer + dps is < 1 or > 10)
            throw Invalid("队伍人数合计必须为 1 至 10 人");
    }
    public static (int Page, int Size) Pagination(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > 1000000) throw Invalid("分页参数不正确");
        return (page, pageSize);
    }
    public static (DateTimeOffset Start, DateTimeOffset End, string Key) Week(string? week = null)
    {
        var local = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var monday = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        if (week is not null)
        {
            var parts = week.Split("-W");
            if (parts.Length != 2 || !int.TryParse(parts[0], out var year) || year is < 2000 or > 2200 ||
                !int.TryParse(parts[1], out var number) || number < 1 || number > System.Globalization.ISOWeek.GetWeeksInYear(year))
                throw new BusinessException(400, "INVALID_WEEK", "周编号不正确");
            monday = System.Globalization.ISOWeek.ToDateTime(year, number, DayOfWeek.Monday);
        }
        var start = new DateTimeOffset(DateTime.SpecifyKind(monday, DateTimeKind.Unspecified), TimeSpan.FromHours(8)).ToUniversalTime();
        return (start, start.AddDays(7), $"{System.Globalization.ISOWeek.GetYear(monday)}-W{System.Globalization.ISOWeek.GetWeekOfYear(monday):D2}");
    }
    public void RejectUnknownQuery(params string[] names)
    {
        if (Context.Request.Query.Keys.Except(names, StringComparer.Ordinal).Any()) throw Invalid("包含不支持的查询参数");
    }
    // 通知与业务数据同一事务写入；提交后由 NotificationPublisher 推送变更提示。
    // 通知里用“标题 + 北京时间开始时刻”指代场次，避免同一天同模板的多场次标题相同而无法区分。
    public static string RaidLabel(RaidEvent raid) => $"「{raid.Title}」（{raid.StartsAt.ToOffset(TimeSpan.FromHours(8)):M月d日 HH:mm}）";
    public void Notify(Guid userId, Guid? guildId, Guid? eventId, string type, string title, string body,
        object target, params Guid[] related)
    {
        database.Notifications.Add(NotificationFactory.Create(userId, guildId, eventId, type, title, body, target,
            related.Append(UserId).ToArray()));
        publisher.Queue(userId, "created");
    }
    public void Audit(string action, Guid target, Guid? guildId, Guid? eventId, params Guid[] related) =>
        database.Audit.Add(new() { ActorId = UserId, GuildId = guildId, EventId = eventId, TargetId = target,
            Action = action, TraceId = Context.TraceIdentifier, RelatedUserIds = related.Append(UserId).Distinct().ToArray() });
}
