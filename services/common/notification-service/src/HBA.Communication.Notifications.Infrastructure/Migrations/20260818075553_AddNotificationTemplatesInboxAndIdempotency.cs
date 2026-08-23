using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Communication.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTemplatesInboxAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "notifications",
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
                name: "idempotency_keys",
                schema: "notifications",
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
                name: "notification_templates",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SubjectTemplate = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BodyTemplate = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_templates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_processed_at",
                schema: "notifications",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "notifications",
                table: "idempotency_keys",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_active",
                schema: "notifications",
                table: "notification_templates",
                columns: new[] { "Code", "Channel", "Locale" },
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ux_notification_templates_code_channel_locale_version",
                schema: "notifications",
                table: "notification_templates",
                columns: new[] { "Code", "Channel", "Locale", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_templates",
                schema: "notifications");
        }
    }
}
