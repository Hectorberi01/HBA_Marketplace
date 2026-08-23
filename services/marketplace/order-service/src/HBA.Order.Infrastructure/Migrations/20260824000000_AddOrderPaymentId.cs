using HBA.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Orders.Infrastructure.Migrations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════
/// CETTE MIGRATION ÉTAIT INERTE — ELLE N'A JAMAIS ÉTÉ APPLIQUÉE.
///
/// Elle était écrite, relue, versionnée, et EF ne l'a jamais vue : il ne charge
/// que les classes portant À LA FOIS <c>[DbContext]</c> et <c>[Migration]</c>.
/// Sans elles, le fichier est du code mort qui a l'air vivant.
///
/// Conséquence : <c>ordering.orders."PaymentId"</c> existait dans le modèle EF et
/// dans le snapshot, et dans AUCUNE base. Toute lecture de la colonne aurait rendu
/// « 42703: column o.PaymentId does not exist » — la même famille de panne que la
/// colonne <c>TraceParent</c> de l'outbox.
///
/// `scripts/check-migrations.py` ne pouvait pas l'attraper : il lit les fichiers
/// `.cs` du dossier, là où EF lit les attributs (limite L2 de DATABASE_AUDIT).
/// ═════════════════════════════════════════════════════════════════════════
/// </summary>
[DbContext(typeof(OrderingDbContext))]
[Migration("20260824000000_AddOrderPaymentId")]
public partial class AddOrderPaymentId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "PaymentId",
            schema: "ordering",
            table: "orders",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_orders_PaymentId",
            schema: "ordering",
            table: "orders",
            column: "PaymentId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_orders_PaymentId",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "PaymentId",
            schema: "ordering",
            table: "orders");
    }
}
