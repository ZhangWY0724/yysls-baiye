using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yanyun.Api.Infrastructure.Authentication;

namespace Yanyun.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly string? _signingKey;

    public ApiFactory() : this("Development") { }

    internal ApiFactory(string environment, string? signingKey = null)
    {
        _environment = environment;
        _signingKey = signingKey;
    }

    // 仅用于测试签名，不是部署凭证。
    public const string TestSigningKey = "test-only-signing-key-1234567890-abcdefghijklmnopqrstuvwxyz";
    public ConcurrentDictionary<Guid, UserAccountState> Accounts { get; } = new();
    public ConcurrentDictionary<Guid, Exception> AccountFailures { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Auth:Issuer"] = "test-issuer",
                ["Auth:Audience"] = "test-audience",
                ["Auth:SigningKey"] = _signingKey ?? TestSigningKey
                , ["Scheduling:Enabled"] = "false"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUserAccountStateReader>();
            services.AddSingleton<IUserAccountStateReader>(new TestAccountReader(Accounts, AccountFailures));
            services.AddControllers().AddApplicationPart(typeof(ProbeController).Assembly);
            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration("postgres",
                    _ => new FixedHealthCheck(HealthCheckResult.Healthy()), null, ["ready"]));
                options.Registrations.Add(new HealthCheckRegistration("redis",
                    _ => new FixedHealthCheck(HealthCheckResult.Unhealthy("private-connection-secret")),
                    null, ["ready"]));
            });
        });
    }

    private sealed class TestAccountReader(ConcurrentDictionary<Guid, UserAccountState> accounts,
        ConcurrentDictionary<Guid, Exception> failures)
        : IUserAccountStateReader
    {
        public Task<UserAccountState?> FindAsync(Guid userId, CancellationToken cancellationToken)
            => failures.TryGetValue(userId, out var failure)
                ? Task.FromException<UserAccountState?>(failure)
                : Task.FromResult(accounts.TryGetValue(userId, out var state) ? state : null);
    }

    private sealed class FixedHealthCheck(HealthCheckResult result) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
