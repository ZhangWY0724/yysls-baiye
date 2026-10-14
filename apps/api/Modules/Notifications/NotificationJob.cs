using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Business;

namespace Yanyun.Api.Modules.Notifications;

[DisallowConcurrentExecution]
public sealed class NotificationJob(YanyunDbContext database, NotificationPublisher publisher, ILogger<NotificationJob> logger) : IJob
{
    public Task Execute(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72420261013)", token);
            var now = DateTimeOffset.UtcNow;
            var outbox = await database.Outbox.Where(row => row.ProcessedAt == null && row.NextAttemptAt <= now)
                .OrderBy(row => row.CreatedAt).Take(100).ToListAsync(token);
            var userIds = outbox.Select(row => row.UserId).Distinct().ToArray();
            var guildIds = outbox.Where(row => row.GuildId != null).Select(row => row.GuildId!.Value).Distinct().ToArray();
            var eventIds = outbox.Where(row => row.EventId != null).Select(row => row.EventId!.Value).Distinct().ToArray();
            var activeUsers = (await database.Users.Where(user => userIds.Contains(user.Id) && user.Status == "active" && user.EmailVerifiedAt != null)
                .Select(user => user.Id).ToArrayAsync(token)).ToHashSet();
            var existingGuilds = (await database.Guilds.Where(guild => guildIds.Contains(guild.Id)).Select(guild => guild.Id).ToArrayAsync(token)).ToHashSet();
            var existingEvents = (await database.Events.Where(raid => eventIds.Contains(raid.Id)).Select(raid => raid.Id).ToArrayAsync(token)).ToHashSet();
            var managers = (await database.Memberships.Where(member => userIds.Contains(member.UserId) && guildIds.Contains(member.GuildId) && member.Role != "member")
                .Select(member => new { member.UserId, member.GuildId }).ToArrayAsync(token)).Select(member => (member.UserId, member.GuildId)).ToHashSet();
            var outboxKeys = outbox.Select(row => $"outbox:{row.Id:D}").ToArray();
            var delivered = (await database.Notifications.Where(notification => outboxKeys.Contains(notification.DedupeKey))
                .Select(notification => notification.DedupeKey).ToArrayAsync(token)).ToHashSet();
            foreach (var row in outbox)
            {
                row.Attempts++;
                if (!activeUsers.Contains(row.UserId) ||
                    row.GuildId is not null && !existingGuilds.Contains(row.GuildId.Value) ||
                    row.EventId is not null && !existingEvents.Contains(row.EventId.Value))
                { database.Outbox.Remove(row); continue; }
                var key = $"outbox:{row.Id:D}";
                if (!delivered.Contains(key))
                {
                    JsonElement payload;
                    NotificationTargetResponse target;
                    try
                    {
                        payload = BusinessJson.Parse(row.PayloadJson);
                        if (payload.GetProperty("type").ValueKind != JsonValueKind.String ||
                            payload.GetProperty("title").ValueKind != JsonValueKind.String ||
                            payload.GetProperty("body").ValueKind != JsonValueKind.String ||
                            payload.GetProperty("target").ValueKind != JsonValueKind.Object)
                            throw new JsonException();
                        target = payload.GetProperty("target").Deserialize<NotificationTargetResponse>(BusinessJson.Options) ?? throw new JsonException();
                        if (target.Kind is not ("guild" or "application" or "invitation" or "raid_event") || target.GuildId is null ||
                            target.Kind == "application" && target.ApplicationId is null ||
                            target.Kind == "invitation" && target.InvitationId is null ||
                            target.Kind == "raid_event" && target.EventId is null ||
                            payload.GetProperty("type").GetString() == "application.received" && target.ApplicationId is null)
                            throw new JsonException();
                    }
                    catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
                    {
                        // 单条格式异常不阻塞其他业务通知；退避持久化后可修复负载并重试。
                        row.NextAttemptAt = now.AddSeconds(Math.Min(3600, 10 * Math.Pow(2, Math.Min(row.Attempts, 9))));
                        logger.LogWarning("站内通知负载无效，延后重试：{OutboxId}，尝试 {Attempts}", row.Id, row.Attempts);
                        continue;
                    }
                    var type = payload.GetProperty("type").GetString();
                    if ((type is "application.received" or "invitation.accepted" or "invitation.declined") &&
                        (row.GuildId is null || !managers.Contains((row.UserId, row.GuildId.Value))))
                    { database.Outbox.Remove(row); continue; }
                    if (type == "application.received")
                    {
                        if (!await database.Applications.AnyAsync(application => application.Id == target.ApplicationId && application.Status == "pending", token))
                        { database.Outbox.Remove(row); continue; }
                    }
                    database.Notifications.Add(new()
                    {
                        UserId = row.UserId, GuildId = row.GuildId, EventId = row.EventId, RelatedUserIds = row.RelatedUserIds,
                        Type = payload.GetProperty("type").GetString()!, Title = payload.GetProperty("title").GetString()!,
                        Body = payload.GetProperty("body").GetString()!, TargetJson = BusinessJson.Serialize(target), DedupeKey = key
                    });
                    publisher.Queue(row.UserId, "created");
                }
                row.ProcessedAt = now;
            }
            var due = await database.Events.Where(raid => raid.Status == "published" && raid.ReminderEnabled &&
                raid.StartsAt > now && raid.StartsAt.AddMinutes(-raid.RemindMinutes) <= now &&
                database.Assignments.Any(assignment => assignment.EventId == raid.Id && assignment.Status == "confirmed" &&
                    !database.Notifications.Any(notification => notification.DedupeKey == "reminder:" + raid.Id.ToString() + ":" + assignment.UserId.ToString())))
                .OrderBy(raid => raid.StartsAt).ThenBy(raid => raid.Id).Take(100).ToListAsync(token);
            var dueIds = due.Select(raid => raid.Id).ToArray();
            var dueAssignments = await (from assignment in database.Assignments
                join member in database.Memberships on assignment.MembershipId equals member.Id
                join user in database.Users on assignment.UserId equals user.Id
                join raid in database.Events on assignment.EventId equals raid.Id
                where dueIds.Contains(assignment.EventId) && assignment.Status == "confirmed" && member.GuildId == raid.GuildId &&
                    member.UserId == assignment.UserId && user.Status == "active" && user.EmailVerifiedAt != null
                select assignment).ToArrayAsync(token);
            var reminderKeys = dueAssignments.Select(assignment => $"reminder:{assignment.EventId:D}:{assignment.UserId:D}").ToArray();
            var reminded = (await database.Notifications.Where(notification => reminderKeys.Contains(notification.DedupeKey))
                .Select(notification => notification.DedupeKey).ToArrayAsync(token)).ToHashSet();
            var assignmentsByEvent = dueAssignments.ToLookup(assignment => assignment.EventId);
            foreach (var raid in due)
            {
                var assignments = assignmentsByEvent[raid.Id];
                foreach (var assignment in assignments)
                {
                    var key = $"reminder:{raid.Id:D}:{assignment.UserId:D}";
                    if (!reminded.Add(key)) continue;
                    database.Notifications.Add(new() { UserId = assignment.UserId, GuildId = raid.GuildId, EventId = raid.Id,
                        RelatedUserIds = [assignment.UserId], Type = "raid.reminder", Title = "副本即将开始",
                        Body = $"「{raid.Title}」即将开始，请查看安排", TargetJson = BusinessJson.Serialize(new { Kind = "raid_event", raid.GuildId, EventId = raid.Id }), DedupeKey = key });
                    publisher.Queue(assignment.UserId, "created");
                }
            }
            await database.Idempotency.Where(record => record.ExpiresAt <= now).ExecuteDeleteAsync(token);
            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            await transaction.DisposeAsync();
            await publisher.FlushAsync(token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && exception is not OutOfMemoryException)
        {
            publisher.Discard();
            database.ChangeTracker.Clear();
            // 当前事务回滚后，下次持久化任务重试，不泄漏邮件/业务正文。
            logger.LogError("站内通知任务失败，将在下一轮重试：{ExceptionType}", exception.GetType().Name);
        }
    }
}
