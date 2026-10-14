using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Business;

namespace Yanyun.Api.Controllers;

[ApiController, Route("api/v1/notifications")]
public sealed class NotificationsController(YanyunDbContext database, BusinessOperations operations, NotificationPublisher publisher) : ControllerBase
{
    private sealed record Cursor(DateTimeOffset CreatedAt, Guid Id, bool UnreadOnly, Guid UserId);
    [HttpGet]
    [ProducesResponseType<ApiResponse<CursorResponse<NotificationResponse>>>(200)]
    public async Task<object> List(CancellationToken token, string? cursor = null, int limit = 20,
        [FromQuery(Name = "unread_only")] bool unreadOnly = false)
    {
        operations.RejectUnknownQuery("cursor", "limit", "unread_only");
        if (limit is < 1 or > 100) throw BusinessOperations.Invalid("条数不正确");
        var query = database.Notifications.Where(notification => notification.UserId == operations.UserId && (!unreadOnly || notification.ReadAt == null));
        if (cursor is not null)
        {
            Cursor? point;
            try { point = JsonSerializer.Deserialize<Cursor>(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)), BusinessJson.Options); }
            catch (Exception exception) when (exception is FormatException or JsonException) { throw BusinessOperations.Invalid("游标不正确"); }
            if (point is null || point.UserId != operations.UserId || point.UnreadOnly != unreadOnly) throw BusinessOperations.Invalid("游标不匹配");
            query = query.Where(notification => notification.CreatedAt < point.CreatedAt || notification.CreatedAt == point.CreatedAt && notification.Id.CompareTo(point.Id) < 0);
        }
        var rows = await query.OrderByDescending(notification => notification.CreatedAt).ThenByDescending(notification => notification.Id).Take(limit + 1).ToArrayAsync(token);
        var items = rows.Take(limit).ToArray();
        var next = rows.Length > limit ? Convert.ToBase64String(Encoding.UTF8.GetBytes(BusinessJson.Serialize(
            new Cursor(items[^1].CreatedAt, items[^1].Id, unreadOnly, operations.UserId)))) : null;
        return ApiResponse.Success(new { Items = items.Select(notification => new { notification.Id, notification.Type, notification.Title,
            notification.Body, notification.CreatedAt, notification.ReadAt,
            Target = JsonSerializer.Deserialize<NotificationTargetResponse>(notification.TargetJson, BusinessJson.Options) }), NextCursor = next });
    }
    [HttpGet("unread-count")]
    [ProducesResponseType<ApiResponse<UnreadResponse>>(200)]
    public async Task<object> Count(CancellationToken token) => ApiResponse.Success(new
    { UnreadCount = await database.Notifications.CountAsync(notification => notification.UserId == operations.UserId && notification.ReadAt == null, token) });
    [HttpPost("read")]
    [ProducesResponseType<ApiResponse<ReadResponse>>(200)]
    public async Task<object> Read(ReadNotificationsWrite request, CancellationToken token)
    {
        if (request.All == true ? request.NotificationIds is not null : request.All is not null || request.NotificationIds is null ||
            request.NotificationIds.Length is < 1 or > 100 || request.NotificationIds.Distinct().Count() != request.NotificationIds.Length)
            throw BusinessOperations.Invalid("请选择全部或 1 至 100 个不同通知");
        return ApiResponse.Success(await operations.WriteAsync("notification.read", request, null, null, false, async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var query = database.Notifications.Where(notification => notification.UserId == operations.UserId);
            if (request.All != true)
            {
                query = query.Where(notification => request.NotificationIds!.Contains(notification.Id));
                if (await query.CountAsync(token) != request.NotificationIds!.Length) throw BusinessOperations.Missing();
            }
            var changed = await query.Where(notification => notification.ReadAt == null && notification.CreatedAt <= now)
                .ExecuteUpdateAsync(update => update.SetProperty(notification => notification.ReadAt, now), token);
            publisher.Queue(operations.UserId, "read");
            return new { UpdatedCount = changed, UnreadCount = await database.Notifications.CountAsync(notification => notification.UserId == operations.UserId && notification.ReadAt == null, token) };
        }, token));
    }
}
