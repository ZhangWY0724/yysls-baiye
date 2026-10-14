using Microsoft.AspNetCore.Authorization;

namespace Yanyun.Api.Infrastructure.Authentication;

public sealed class VerifiedEmailRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "VerifiedEmailRequired";
    internal const string StateItemKey = "Yanyun.CurrentAccountState";
    internal const string ErrorItemKey = "Yanyun.AuthenticationError";
}

public sealed class VerifiedEmailHandler : AuthorizationHandler<VerifiedEmailRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, VerifiedEmailRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext &&
            httpContext.Items[VerifiedEmailRequirement.StateItemKey] is UserAccountState
            { IsActive: true, HasVerifiedEmail: true })
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
