using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Yanyun.Api.Infrastructure.Configuration;

namespace Yanyun.Api.Infrastructure.Redis;

public sealed class RedisConnection : IDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _connection;

    public RedisConnection(IConfiguration configuration, IOptions<RedisOptions> options)
    {
        var settings = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("缺少 Redis 连接配置。"));
        settings.AbortOnConnectFail = false;
        settings.ConnectRetry = 0;
        settings.ConnectTimeout = options.Value.ConnectTimeoutMilliseconds;
        settings.AsyncTimeout = options.Value.OperationTimeoutMilliseconds;
        settings.SyncTimeout = options.Value.OperationTimeoutMilliseconds;
        _connection = new Lazy<Task<ConnectionMultiplexer>>(() => ConnectionMultiplexer.ConnectAsync(settings));
    }

    public Task<ConnectionMultiplexer> GetAsync() => _connection.Value;

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            // 延迟建立连接，未启动 Redis 也能运行 API；连接最终完成时仍释放资源。
            _ = _connection.Value.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    task.Result.Dispose();
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
