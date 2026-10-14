using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using StackExchange.Redis;

namespace Yanyun.Api.Common;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        if (exception is BusinessException business)
        {
            context.Response.StatusCode = business.Status;
            if (business.Status == 429)
                context.Response.Headers.RetryAfter = (business.Details is RetryDelay retry ? retry.RetryAfter : 60)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(ApiResponse.Failure(context,
                business.Code, business.Message, business.Details), cancellationToken);
            return true;
        }
        if (exception is System.Text.Json.JsonException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(ApiResponse.Failure(context,
                "VALIDATION_FAILED", "字段格式不正确"), cancellationToken);
            return true;
        }
        if (exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(ApiResponse.Failure(context,
                "VERSION_CONFLICT", "数据已更新，请刷新后重试"), cancellationToken);
            return true;
        }

        var dependencyUnavailable = exception is NpgsqlException or RedisException ||
            exception is Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: NpgsqlException };
        var status = dependencyUnavailable ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status500InternalServerError;
        var code = dependencyUnavailable ? "DEPENDENCY_UNAVAILABLE" : "INTERNAL_ERROR";
        var message = dependencyUnavailable ? "服务依赖暂不可用，请稍后重试" : "服务暂时异常，请稍后重试";

        // 不记录请求体、凭证或完整 URL，客户端只收到稳定错误码。
        logger.LogError("请求失败：{ExceptionType}，追踪标识 {TraceId}",
            exception.GetType().Name, context.TraceIdentifier);
        logger.LogDebug("异常调用位置：{StackTrace}", exception.StackTrace);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            ApiResponse.Failure(context, code, message), cancellationToken);
        return true;
    }
}
