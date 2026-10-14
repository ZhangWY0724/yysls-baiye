using Microsoft.AspNetCore.SignalR;
using Yanyun.Api.Modules.Notifications;

namespace Yanyun.Api.Modules.Business;

public static class NotificationFactory
{
    public static Notification Create(Guid userId, Guid? guildId, Guid? eventId, string type, string title, string body,
        object target, IEnumerable<Guid>? related = null, string? dedupeKey = null)
    {
        var notification = new Notification
        {
            UserId = userId, GuildId = guildId, EventId = eventId, Type = type, Title = title, Body = body,
            TargetJson = BusinessJson.Serialize(target),
            RelatedUserIds = (related ?? []).Append(userId).Distinct().ToArray()
        };
        notification.DedupeKey = dedupeKey ?? $"notification:{notification.Id:D}";
        return notification;
    }
}

// 收集本次请求涉及的用户，事务提交后再推送 SignalR 变更提示；推送失败不影响业务结果，客户端会在重连或聚焦时补查。
public sealed class NotificationPublisher(IHubContext<NotificationHub> hub, ILogger<NotificationPublisher> logger)
{
    private readonly HashSet<(Guid User, string Reason)> _pending = [];
    public void Queue(Guid user, string reason) => _pending.Add((user, reason));

    public async Task FlushAsync()
    {
        if (_pending.Count == 0) return;
        var items = _pending.ToArray();
        _pending.Clear();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Task.WhenAll(items.Select(item => hub.Clients.User(item.User.ToString("D"))
                .SendAsync("notifications.changed", new { item.Reason }, timeout.Token)));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("站内通知提示发送未完成：{ExceptionType}", exception.GetType().Name);
        }
    }
}
