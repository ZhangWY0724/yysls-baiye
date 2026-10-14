using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yanyun.Api.Migrations
{
    /// <inheritdoc />
    public partial class PhaseOneBusiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_profile_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_user_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trace_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "game_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    server = table.Column<string>(type: "text", nullable: false),
                    power = table.Column<int>(type: "integer", nullable: false),
                    styles = table.Column<string[]>(type: "text[]", nullable: false),
                    online_slots_json = table.Column<string>(type: "jsonb", nullable: false),
                    bio = table.Column<string>(type: "text", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_profiles", x => x.id);
                    table.UniqueConstraint("AK_game_profiles_id_user_id", x => new { x.id, x.user_id });
                    table.CheckConstraint("ck_profiles_power", "power >= 0");
                    table.CheckConstraint("ck_profiles_server", "server IN ('official','channel')");
                    table.CheckConstraint("ck_profiles_styles", "cardinality(styles) BETWEEN 1 AND 20");
                    table.ForeignKey(
                        name: "FK_game_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "guilds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    server = table.Column<string>(type: "text", nullable: false),
                    intro = table.Column<string>(type: "text", nullable: false),
                    recruit_requirements = table.Column<string>(type: "text", nullable: false),
                    recruiting = table.Column<bool>(type: "boolean", nullable: false),
                    reminder_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    remind_minutes = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guilds", x => x.id);
                    table.CheckConstraint("ck_guild_reminder", "remind_minutes BETWEEN 1 AND 1440");
                    table.CheckConstraint("ck_guild_server", "server IN ('official','channel')");
                    table.ForeignKey(
                        name: "FK_guilds_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_user_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    operation = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_user_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    target_json = table.Column<string>(type: "jsonb", nullable: false),
                    dedupe_key = table.Column<string>(type: "text", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

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
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    handled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.id);
                    table.ForeignKey(
                        name: "FK_applications_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_applications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "dungeon_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    need_tank = table.Column<int>(type: "integer", nullable: false),
                    need_healer = table.Column<int>(type: "integer", nullable: false),
                    need_dps = table.Column<int>(type: "integer", nullable: false),
                    default_duration_min = table.Column<int>(type: "integer", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dungeon_templates", x => x.id);
                    table.CheckConstraint("ck_template_capacity", "need_tank BETWEEN 0 AND 10 AND need_healer BETWEEN 0 AND 10 AND need_dps BETWEEN 0 AND 10 AND (need_tank+need_healer+need_dps) BETWEEN 1 AND 10 AND default_duration_min BETWEEN 1 AND 1440");
                    table.ForeignKey(
                        name: "FK_dungeon_templates_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_dungeon_templates_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitations", x => x.id);
                    table.CheckConstraint("ck_invitation_uses", "used_count >= 0 AND max_uses > 0 AND used_count <= max_uses");
                    table.ForeignKey(
                        name: "FK_invitations_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_invitations_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_invitations_users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberships", x => x.id);
                    table.CheckConstraint("ck_members_role", "role IN ('owner','admin','member')");
                    table.ForeignKey(
                        name: "FK_memberships_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "raid_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    template_name = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_min = table.Column<int>(type: "integer", nullable: false),
                    signup_mode = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    need_tank = table.Column<int>(type: "integer", nullable: false),
                    need_healer = table.Column<int>(type: "integer", nullable: false),
                    need_dps = table.Column<int>(type: "integer", nullable: false),
                    reminder_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    remind_minutes = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    reminder_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raid_events", x => x.id);
                    table.CheckConstraint("ck_event_capacity", "need_tank BETWEEN 0 AND 10 AND need_healer BETWEEN 0 AND 10 AND need_dps BETWEEN 0 AND 10 AND (need_tank+need_healer+need_dps) BETWEEN 1 AND 10 AND duration_min BETWEEN 1 AND 1440 AND remind_minutes BETWEEN 1 AND 1440");
                    table.ForeignKey(
                        name: "FK_raid_events_dungeon_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "dungeon_templates",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_raid_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_raid_events_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "invitation_acceptances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitation_acceptances", x => x.id);
                    table.ForeignKey(
                        name: "FK_invitation_acceptances_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "invitations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_invitation_acceptances_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "memberships",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_role = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignments", x => x.id);
                    table.CheckConstraint("ck_assignment_role", "slot_role IN ('tank','healer','dps')");
                    table.CheckConstraint("ck_assignment_status", "status IN ('confirmed','waitlist')");
                    table.ForeignKey(
                        name: "FK_assignments_game_profiles_profile_id_user_id",
                        columns: x => new { x.profile_id, x.user_id },
                        principalTable: "game_profiles",
                        principalColumns: new[] { "id", "user_id" });
                    table.ForeignKey(
                        name: "FK_assignments_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "memberships",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_assignments_raid_events_event_id",
                        column: x => x.event_id,
                        principalTable: "raid_events",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_default_profile_id_id",
                table: "users",
                columns: new[] { "default_profile_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_applications_guild_id_user_id",
                table: "applications",
                columns: new[] { "guild_id", "user_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_applications_user_id",
                table: "applications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_event_id_status_slot_role_created_at_id",
                table: "assignments",
                columns: new[] { "event_id", "status", "slot_role", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_assignments_event_id_user_id",
                table: "assignments",
                columns: new[] { "event_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assignments_membership_id",
                table: "assignments",
                column: "membership_id");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_profile_id_user_id",
                table: "assignments",
                columns: new[] { "profile_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_event_id",
                table: "audit_logs",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_guild_id",
                table: "audit_logs",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_related_user_ids",
                table: "audit_logs",
                column: "related_user_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_dungeon_templates_created_by",
                table: "dungeon_templates",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_dungeon_templates_guild_id",
                table: "dungeon_templates",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_profiles_user_id",
                table: "game_profiles",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_guilds_owner_user_id",
                table: "guilds",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_event_id",
                table: "idempotency_records",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_guild_id",
                table: "idempotency_records",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_related_user_ids",
                table: "idempotency_records",
                column: "related_user_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_user_id_operation_key",
                table: "idempotency_records",
                columns: new[] { "user_id", "operation", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invitation_acceptances_invitation_id_membership_id",
                table: "invitation_acceptances",
                columns: new[] { "invitation_id", "membership_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invitation_acceptances_membership_id",
                table: "invitation_acceptances",
                column: "membership_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_code_hash",
                table: "invitations",
                column: "code_hash",
                unique: true,
                filter: "code_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_created_by",
                table: "invitations",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_guild_id",
                table: "invitations",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_target_user_id",
                table: "invitations",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_guild_id",
                table: "memberships",
                column: "guild_id",
                unique: true,
                filter: "role = 'owner'");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_user_id",
                table: "memberships",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_dedupe_key",
                table: "notifications",
                column: "dedupe_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_event_id",
                table: "notifications",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_guild_id",
                table: "notifications",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_related_user_ids",
                table: "notifications",
                column: "related_user_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_user_id_created_at",
                table: "notifications",
                columns: new[] { "user_id", "created_at" },
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_user_id_created_at_id",
                table: "notifications",
                columns: new[] { "user_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_event_id",
                table: "outbox_events",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_guild_id",
                table: "outbox_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_next_attempt_at",
                table: "outbox_events",
                column: "next_attempt_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_related_user_ids",
                table: "outbox_events",
                column: "related_user_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_raid_events_created_by",
                table: "raid_events",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_raid_events_guild_id_starts_at",
                table: "raid_events",
                columns: new[] { "guild_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "IX_raid_events_template_id",
                table: "raid_events",
                column: "template_id");

            migrationBuilder.AddForeignKey(
                name: "FK_users_game_profiles_default_profile_id_id",
                table: "users",
                columns: new[] { "default_profile_id", "id" },
                principalTable: "game_profiles",
                principalColumns: new[] { "id", "user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_game_profiles_default_profile_id_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "applications");

            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "invitation_acceptances");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropTable(
                name: "game_profiles");

            migrationBuilder.DropTable(
                name: "raid_events");

            migrationBuilder.DropTable(
                name: "invitations");

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "dungeon_templates");

            migrationBuilder.DropTable(
                name: "guilds");

            migrationBuilder.DropIndex(
                name: "IX_users_default_profile_id_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "default_profile_id",
                table: "users");
        }
    }
}
