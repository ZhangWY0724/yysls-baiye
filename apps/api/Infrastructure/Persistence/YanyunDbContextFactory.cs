using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Yanyun.Api.Infrastructure.Persistence;

public sealed class YanyunDbContextFactory : IDesignTimeDbContextFactory<YanyunDbContext>
{
    public YanyunDbContext CreateDbContext(string[] args)
    {
        // 设计期生成迁移无需启动 API、连接数据库或加载 JWT 密钥。
        var assemblyDirectory = Path.GetDirectoryName(typeof(YanyunDbContext).Assembly.Location)!;
        var configuration = new ConfigurationBuilder()
            .SetBasePath(assemblyDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<YanyunDbContextFactory>(optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        var connection = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("缺少 PostgreSQL 连接配置。");
        return new YanyunDbContext(new DbContextOptionsBuilder<YanyunDbContext>()
            .UseNpgsql(connection).Options);
    }
}
