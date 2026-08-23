using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Users.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferencesDevicesInboxAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "users",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_inbox", x => new { x.EventId, x.ConsumerName });
                });

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PushToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RegisteredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "users",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Scope = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseBody = table.Column<string>(type: "jsonb", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => new { x.Key, x.Scope, x.Endpoint });
                });

            migrationBuilder.CreateTable(
                name: "preferences",
                schema: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PushEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MarketingOptIn = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_processed_at",
                schema: "users",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_devices_last_seen_at",
                schema: "users",
                table: "devices",
                column: "LastSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "ux_devices_user_push_token",
                schema: "users",
                table: "devices",
                columns: new[] { "UserId", "PushToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "users",
                table: "idempotency_keys",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "users");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "users");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "users");

            migrationBuilder.DropTable(
                name: "preferences",
                schema: "users");
        }
    }
}
