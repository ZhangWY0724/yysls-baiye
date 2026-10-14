using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yanyun.Api.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "slot_index",
                table: "assignments",
                type: "integer",
                nullable: true);

            // 已确认排班按定位对应的座位区间、报名先后补座位号；候补保持 NULL。
            migrationBuilder.Sql(@"
UPDATE assignments a
SET slot_index = r.base + r.rn
FROM (
    SELECT x.id,
           CASE x.slot_role WHEN 'tank' THEN 0 WHEN 'healer' THEN e.need_tank ELSE e.need_tank + e.need_healer END AS base,
           ROW_NUMBER() OVER (PARTITION BY x.event_id, x.slot_role ORDER BY x.created_at, x.id) AS rn
    FROM assignments x
    JOIN raid_events e ON e.id = x.event_id
    WHERE x.status = 'confirmed'
) r
WHERE a.id = r.id;");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_event_id_slot_index",
                table: "assignments",
                columns: new[] { "event_id", "slot_index" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_assignment_seat",
                table: "assignments",
                sql: "slot_index IS NULL OR slot_index BETWEEN 1 AND 10");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_assignments_event_id_slot_index",
                table: "assignments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_assignment_seat",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "slot_index",
                table: "assignments");
        }
    }
}
