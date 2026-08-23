using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderType",
                schema: "payments",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Marketplace");

            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "payments",
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
                schema: "payments",
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

            migrationBuilder.CreateIndex(
                name: "ix_payments_order",
                schema: "payments",
                table: "payments",
                columns: new[] { "OrderType", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_processed_at",
                schema: "payments",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "payments",
                table: "idempotency_keys",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_order",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "OrderType",
                schema: "payments",
                table: "payments");
        }
    }
}
