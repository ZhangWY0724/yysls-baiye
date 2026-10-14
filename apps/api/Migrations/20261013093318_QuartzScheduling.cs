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
            // 原用于创建 Quartz 持久化表；调度已改为 BackgroundService，表由 SimplifyNotifications 迁移删除。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
