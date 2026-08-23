using System;
using HBA.Financial.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Payments.Infrastructure.Migrations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════
/// CETTE MIGRATION ÉTAIT INERTE — LA TABLE N'A JAMAIS ÉTÉ CRÉÉE.
///
/// Comme <c>20260824000000_AddOrderPaymentId</c>, elle était dépourvue de
/// <c>[DbContext]</c> et de <c>[Migration]</c> : EF ne charge que les classes qui
/// portent les deux. Le fichier existait, il ne s'exécutait pas.
///
/// Le remboursement de paiement est donc resté sans table. Et comme
/// <c>PaymentRefund</c> était AUSSI absent du snapshot, un prochain
/// `dotnet ef migrations add` aurait généré une SECONDE création de la même table.
/// Le snapshot a été complété dans le même geste.
/// ═════════════════════════════════════════════════════════════════════════
/// </summary>
[DbContext(typeof(PaymentsDbContext))]
[Migration("20260824010000_AddPaymentRefunds")]
public partial class AddPaymentRefunds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "payment_refunds",
            schema: "payments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                ReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                ExternalRefundId = table.Column<Guid>(type: "uuid", nullable: true),
                amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ProviderRefundId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AttemptCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_refunds", x => x.Id);
                table.ForeignKey(
                    name: "FK_payment_refunds_payments_PaymentId",
                    column: x => x.PaymentId,
                    principalSchema: "payments",
                    principalTable: "payments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_payment_refunds_ExternalRefundId",
            schema: "payments",
            table: "payment_refunds",
            column: "ExternalRefundId");

        migrationBuilder.CreateIndex(
            name: "IX_payment_refunds_PaymentId_IdempotencyKey",
            schema: "payments",
            table: "payment_refunds",
            columns: new[] { "PaymentId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payment_refunds_ReturnId",
            schema: "payments",
            table: "payment_refunds",
            column: "ReturnId");

        migrationBuilder.CreateIndex(
            name: "IX_payment_refunds_Status",
            schema: "payments",
            table: "payment_refunds",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "payment_refunds",
            schema: "payments");
    }
}
