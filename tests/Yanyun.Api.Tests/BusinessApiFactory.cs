using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Yanyun.Api.Infrastructure.Email;
using Yanyun.Api.Infrastructure.Persistence;
using Yanyun.Api.Modules.Profiles;

namespace Yanyun.Api.Tests;

public sealed class InfrastructureFactAttribute : FactAttribute
{
    public InfrastructureFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("YANYUN_RUN_INFRA_TESTS") != "1")
            Skip = "真实基础设施测试需显式设置 YANYUN_RUN_INFRA_TESTS=1";
    }
}

public sealed class BusinessApiFactory : WebApplicationFactory<Program>
{
    public string Schema { get; } = "test_" + Guid.NewGuid().ToString("N");
    public TestEmails Emails { get; } = new();
    public SaveFailureInterceptor SaveFailure { get; } = new();
    public QueryCounterInterceptor Queries { get; } = new();
    private string? _connection;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath("../../../../../apps/api/appsettings.Local.json", AppContext.BaseDirectory))
            .AddEnvironmentVariables().Build();
        var postgres = new NpgsqlConnectionStringBuilder(config.GetConnectionString("Postgres")) { SearchPath = Schema };
        _connection = postgres.ConnectionString;
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:SigningKey"] = ApiFactory.TestSigningKey, ["Auth:Issuer"] = "infra-test", ["Auth:Audience"] = "infra-test",
            ["ConnectionStrings:Postgres"] = _connection, ["ConnectionStrings:Redis"] = config.GetConnectionString("Redis"),
            ["Redis:KeyPrefix"] = $"yanyun:tests:{Schema}:", ["Scheduling:Enabled"] = "false"
        }));
        builder.ConfigureServices(services =>
        {
            services.AddDbContext<YanyunDbContext>(options => options.AddInterceptors(SaveFailure, Queries));
            services.RemoveAll<NpgsqlDataSource>();
            services.AddSingleton(_ => NpgsqlDataSource.Create(_connection));
            services.RemoveAll<IVerificationEmailSender>(); services.AddSingleton<IVerificationEmailSender>(Emails);
        });
    }
    public async Task InitializeDatabaseAsync()
    {
        _ = Services;
        var connection = new NpgsqlConnection(_connection);
        await using (connection)
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // schema 名由固定前缀和随机十六进制生成，测试不删除或改动 public 业务数据。
            command.CommandText = $"CREATE SCHEMA \"{Schema}\"";
            await command.ExecuteNonQueryAsync();
        }
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<YanyunDbContext>();
        await database.Database.MigrateAsync();
        database.ProfileStyleOptions.Add(new ProfileStyleOption { Code = "test_style", Label = "测试流派", Enabled = true, SortOrder = 1 });
        await database.SaveChangesAsync();
    }
    public sealed class TestEmails : IVerificationEmailSender
    {
        public ConcurrentDictionary<string, (Guid Challenge, string Code)> Sent { get; } = new();
        public Task SendRegistrationAsync(string recipient, string code, Guid challengeId, CancellationToken cancellationToken)
        { Sent[recipient] = (challengeId, code); return Task.CompletedTask; }
    }
    public sealed class SaveFailureInterceptor : SaveChangesInterceptor
    {
        public bool FailNext { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new NpgsqlException("测试数据库保存失败");
            }
            return ValueTask.FromResult(result);
        }
    }
    public sealed class QueryCounterInterceptor : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Reset() => Interlocked.Exchange(ref _count, 0);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }
}
