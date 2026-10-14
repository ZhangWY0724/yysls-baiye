using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yanyun.Api.Common;

namespace Yanyun.Api.Infrastructure.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        // 对外只报告状态，不输出异常、数据库地址或配置内容。
        var data = new
        {
            Status = report.Status.ToString().ToLowerInvariant(),
            Checks = report.Entries.ToDictionary(entry => entry.Key,
                entry => entry.Value.Status.ToString().ToLowerInvariant())
        };
        return context.Response.WriteAsJsonAsync(ApiResponse.Success(data), context.RequestAborted);
    }
}
