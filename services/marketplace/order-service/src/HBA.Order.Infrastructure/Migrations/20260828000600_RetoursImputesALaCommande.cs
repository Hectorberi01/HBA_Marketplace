using HBA.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Orders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUE LES RETOURS ONT RETIRÉ À LA COMMANDE (ISSUE-014).
    ///
    /// `AlreadyReturnedQuantity: 0` ET `AlreadyRefundedAmount: 0m` ÉTAIENT
    /// CODÉS EN DUR.
    ///
    /// `OrderingModuleApi.GetOrderReturnContextAsync` est la lecture sur laquelle
    /// return-refund fonde CHAQUE ouverture de dossier et CHAQUE plafond de
    /// remboursement. Elle affirmait, à chaque appel, que rien n'était jamais
    /// revenu et que rien n'avait jamais été remboursé. Le même exemplaire
    /// pouvait donc être retourné et remboursé autant de fois qu'on ouvrait de
    /// demandes — chacune validée par deux garde-fous qui s'exécutaient sur des
    /// valeurs fausses.
    ///
    /// Ce n'était pas un calcul manquant : order-service ne possède pas les
    /// retours et n'avait aucune source. Ces deux tables sont cette source.
    ///
    /// POURQUOI DEUX TABLES ET NON DEUX COLONNES CUMULATIVES.
    ///
    /// Une colonne `RefundedAmount` sur `orders`, incrémentée à chaque message,
    /// aurait exigé sa propre idempotence — et un seul message compté deux fois
    /// aurait fermé durablement le plafond de remboursement d'un client, sans
    /// trace de la cause. Ici, on enregistre le FAIT (ce dossier a rendu tant, et
    /// repris tant d'exemplaires de telle ligne) et on somme à la lecture. Le
    /// même message rejoué écrase la même valeur ; la somme est inchangée.
    ///
    /// L'INDEX UNIQUE N'EST PAS DÉCORATIF.
    ///
    /// Le rapprochement en mémoire (« ce dossier est-il déjà connu ? ») ne voit
    /// pas deux messages du même dossier traités en parallèle : les deux lisent
    /// « inconnu » avant que l'un ait écrit. Sans cet index, la commande
    /// compterait deux fois la même marchandise rendue.
    ///
    /// RIEN À RÉTROPROJETER, ET C'EST À SAVOIR.
    ///
    /// Les tables naissent vides. Les retours ANTÉRIEURS à cette migration ne
    /// s'y trouvent pas : order-service ne les a jamais reçus, et return-refund
    /// ne les rejouera pas. Sur une base déjà exploitée, les commandes concernées
    /// resteront donc trop permissives jusqu'à ce qu'un rattrapage soit écrit —
    /// il exige de lire la base de return-refund, ce qu'une migration
    /// d'order-service ne peut pas faire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(OrderingDbContext))]
    [Migration("20260828000600_RetoursImputesALaCommande")]
    public partial class RetoursImputesALaCommande : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_return_settlements",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_return_settlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_return_settlements_orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ordering",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_return_settlement_lines",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderReturnSettlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_return_settlement_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_return_settlement_lines_order_return_settlements_Order~",
                        column: x => x.OrderReturnSettlementId,
                        principalSchema: "ordering",
                        principalTable: "order_return_settlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_return_settlements_OrderId_ReturnRequestId",
                schema: "ordering",
                table: "order_return_settlements",
                columns: new[] { "OrderId", "ReturnRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_return_settlement_lines_OrderReturnSettlementId",
                schema: "ordering",
                table: "order_return_settlement_lines",
                column: "OrderReturnSettlementId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_return_settlement_lines",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_return_settlements",
                schema: "ordering");
        }
    }
}
