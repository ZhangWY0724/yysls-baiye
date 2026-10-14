using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Filters;
using Yanyun.Api.Common;

namespace Yanyun.Api.Infrastructure.Authentication;

public sealed class CsrfGuard(IAntiforgery antiforgery, IConfiguration configuration) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var origin = request.Headers.Origin.ToString();
        var sameOrigin = $"{request.Scheme}://{request.Host}";
        var allowed = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (string.IsNullOrEmpty(origin) || !(origin.Equals(sameOrigin, StringComparison.OrdinalIgnoreCase) ||
            allowed.Contains(origin, StringComparer.OrdinalIgnoreCase)))
            throw new BusinessException(403, "ORIGIN_REJECTED", "请求来源不受允许");
        try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
        catch (AntiforgeryValidationException)
        { throw new BusinessException(400, "CSRF_INVALID", "请求凭证已失效，请刷新后重试"); }
        await next();
    }
}
