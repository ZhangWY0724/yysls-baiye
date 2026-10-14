using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Yanyun.Api.Infrastructure.Authentication;

namespace Yanyun.Api.Modules.Notifications;

[Authorize(Policy = VerifiedEmailRequirement.PolicyName)]
public sealed class NotificationHub : Hub;

public sealed class NotificationUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => Guid.TryParse(connection.User?.FindFirst("sub")?.Value, out var userId)
            ? userId.ToString() : null;
}
