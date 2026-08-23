using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaChallengesInboxAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "identity",
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
                schema: "identity",
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
                name: "mfa_challenges",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mfa_challenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_processed_at",
                schema: "identity",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "identity",
                table: "idempotency_keys",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_mfa_challenges_active",
                schema: "identity",
                table: "mfa_challenges",
                columns: new[] { "UserId", "ExpiresAtUtc" },
                filter: "\"ConsumedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mfa_challenges_expires_at",
                schema: "identity",
                table: "mfa_challenges",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "mfa_challenges",
                schema: "identity");
        }
    }
}
