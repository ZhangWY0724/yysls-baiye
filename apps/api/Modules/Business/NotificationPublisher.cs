namespace Yanyun.Api.Modules.Business;

public sealed class NotificationPublisher(NotificationDispatchQueue queue)
{
    private readonly HashSet<(Guid User, string Reason)> _pending = [];
    public void Queue(Guid user, string reason) => _pending.Add((user, reason));
    public void Discard() => _pending.Clear();
    public Task FlushAsync(CancellationToken token)
    {
        foreach (var item in _pending) queue.Enqueue(item);
        _pending.Clear();
        return Task.CompletedTask;
    }
}
