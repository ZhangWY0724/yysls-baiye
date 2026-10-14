using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yanyun.Api.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyNotifications : Migration
    {
        // 站内通知改为在业务事务内直接写入，开打提醒改由 BackgroundService 扫描，不再需要 Outbox 和 Quartz 表。
        private static readonly string[] QuartzTables =
        [
            "qrtz_fired_triggers", "qrtz_paused_trigger_grps", "qrtz_scheduler_state", "qrtz_locks",
            "qrtz_simple_triggers", "qrtz_cron_triggers", "qrtz_simprop_triggers", "qrtz_blob_triggers",
            "qrtz_triggers", "qrtz_job_details", "qrtz_calendars"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "outbox_events");
            migrationBuilder.DropColumn(name: "reminder_version", table: "raid_events");
            foreach (var table in QuartzTables)
                migrationBuilder.Sql($"DROP TABLE IF EXISTS {table} CASCADE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "reminder_version",
                table: "raid_events",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_user_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_outbox_events", x => x.id));
        }
    }
}
