using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Yanyun.Api.Infrastructure.Redis;

namespace Yanyun.Api.Infrastructure.Health;

public sealed class RedisHealthCheck(RedisConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await connection.GetAsync().WaitAsync(cancellationToken);
            await client.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Redis 连接不可用");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Redis 健康检查超时或已取消");
        }
    }
}
