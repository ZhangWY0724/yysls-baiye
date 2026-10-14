using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yanyun.Api.Migrations
{
    /// <inheritdoc />
    public partial class QuartzScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 来自 Quartz.NET v3.15.1 官方 PostgreSQL 建表脚本，仅保留 CREATE 部分。
            using var stream = typeof(QuartzScheduling).Assembly.GetManifestResourceStream(
                "Yanyun.Api.Infrastructure.Persistence.Quartz.postgres.sql")
                ?? throw new InvalidOperationException("缺少 Quartz 建表资源");
            using var reader = new System.IO.StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Quartz 持久化任务回退需先停止调度并人工确认删除范围。");

        }
    }
}
