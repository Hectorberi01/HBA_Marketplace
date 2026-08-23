using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Promotions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "promotions");

            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "promotions",
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
                name: "coupons",
                schema: "promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    PerUserLimit = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "promotions",
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
                name: "outbox_messages",
                schema: "promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                schema: "promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Budget = table.Column<long>(type: "bigint", nullable: true),
                    BudgetConsumed = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "coupon_usages",
                schema: "promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiscountAmount = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_usages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_coupon_usages_coupons_CouponId",
                        column: x => x.CouponId,
                        principalSchema: "promotions",
                        principalTable: "coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_rules",
                schema: "promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    RuleJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promotion_rules_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalSchema: "promotions",
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_processed_at",
                schema: "promotions",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_coupon_usages_coupon_user",
                schema: "promotions",
                table: "coupon_usages",
                columns: new[] { "CouponId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_usages_expiring",
                schema: "promotions",
                table: "coupon_usages",
                column: "ExpiresAtUtc",
                filter: "\"Status\" = 'Held'");

            migrationBuilder.CreateIndex(
                name: "ix_coupon_usages_order",
                schema: "promotions",
                table: "coupon_usages",
                column: "OrderId",
                filter: "\"OrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_coupon_usages_live_hold",
                schema: "promotions",
                table: "coupon_usages",
                columns: new[] { "CouponId", "CartId" },
                unique: true,
                filter: "\"Status\" = 'Held'");

            migrationBuilder.CreateIndex(
                name: "ix_coupons_promotion",
                schema: "promotions",
                table: "coupons",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "ux_coupons_code",
                schema: "promotions",
                table: "coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "promotions",
                table: "idempotency_keys",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "promotions",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "promotions",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_rules_promotion",
                schema: "promotions",
                table: "promotion_rules",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_ends_at",
                schema: "promotions",
                table: "promotions",
                column: "EndsAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_scope_status",
                schema: "promotions",
                table: "promotions",
                columns: new[] { "Scope", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "coupon_usages",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "promotion_rules",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "coupons",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "promotions",
                schema: "promotions");
        }
    }
}
