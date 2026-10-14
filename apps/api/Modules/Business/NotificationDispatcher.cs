using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Notifications;

namespace Yanyun.Api.Modules.Business;

// 有界队列只承载变更提示；通知正文由数据库和 Outbox 持久化。
public sealed class NotificationDispatchQueue
{
    private readonly Channel<(Guid User, string Reason)> _channel = Channel.CreateBounded<(Guid, string)>(
        new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    internal ChannelReader<(Guid User, string Reason)> Reader => _channel.Reader;
    public void Enqueue((Guid User, string Reason) item) => _channel.Writer.TryWrite(item);
}

public sealed class NotificationDispatcher(NotificationDispatchQueue queue, IServiceScopeFactory scopes,
    IHubContext<NotificationHub> hub, ILogger<NotificationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                var batch = new HashSet<(Guid User, string Reason)>();
                var read = 0;
                while (read++ < 100 && queue.Reader.TryRead(out var item)) batch.Add(item);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    using var scope = scopes.CreateScope();
                    var database = scope.ServiceProvider.GetRequiredService<YanyunDbContext>();
                    var ids = batch.Select(item => item.User).Distinct().ToArray();
                    var active = (await database.Users.AsNoTracking().Where(user => ids.Contains(user.Id) &&
                        user.Status == "active" && user.EmailVerifiedAt != null).Select(user => user.Id)
                        .ToArrayAsync(timeout.Token)).ToHashSet();
                    await Task.WhenAll(batch.Where(item => active.Contains(item.User)).Select(item =>
                        hub.Clients.User(item.User.ToString("D")).SendAsync("notifications.changed",
                            new { item.Reason }, timeout.Token)));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                { logger.LogWarning("站内通知提示发送未完成：{ExceptionType}", exception.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
