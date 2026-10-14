using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Business;

namespace Yanyun.Api.Modules.Notifications;

// 周期扫描即将开始的场次，为正式排班成员写入开打提醒；去重键保证重启或多实例不会重复提醒。
public sealed class ReminderService(IServiceScopeFactory scopes, ILogger<ReminderService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await RunOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                logger.LogError("开打提醒任务失败，将在下一轮重试：{ExceptionType}", exception.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task RunOnceAsync(IServiceProvider services, CancellationToken token)
    {
        var database = services.GetRequiredService<YanyunDbContext>();
        var publisher = services.GetRequiredService<NotificationPublisher>();
        await using (var transaction = await database.Database.BeginTransactionAsync(token))
        {
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72420261014)", token);
            var now = DateTimeOffset.UtcNow;
            var dueAssignments = await (from assignment in database.Assignments
                join raid in database.Events on assignment.EventId equals raid.Id
                join member in database.Memberships on assignment.MembershipId equals member.Id
                join user in database.Users on assignment.UserId equals user.Id
                where raid.Status == "published" && raid.ReminderEnabled && raid.StartsAt > now &&
                    raid.StartsAt.AddMinutes(-raid.RemindMinutes) <= now && assignment.Status == "confirmed" &&
                    member.GuildId == raid.GuildId && member.UserId == assignment.UserId && user.Status == "active" && user.EmailVerifiedAt != null &&
                    !database.Notifications.Any(notification => notification.DedupeKey ==
                        "reminder:" + raid.Id.ToString() + ":" + assignment.UserId.ToString())
                orderby raid.StartsAt, raid.Id
                select new { assignment.UserId, Raid = raid }).Take(500).ToArrayAsync(token);
            foreach (var due in dueAssignments)
            {
                var raid = due.Raid;
                database.Notifications.Add(NotificationFactory.Create(due.UserId, raid.GuildId, raid.Id, "raid.reminder", "副本即将开始",
                    $"「{raid.Title}」即将开始，请查看安排", new { Kind = "raid_event", raid.GuildId, EventId = raid.Id },
                    dedupeKey: $"reminder:{raid.Id:D}:{due.UserId:D}"));
                publisher.Queue(due.UserId, "created");
            }
            await database.Idempotency.Where(record => record.ExpiresAt <= now).ExecuteDeleteAsync(token);
            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        await publisher.FlushAsync();
    }
}
