using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Inventory.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE BALAYAGE DES RÉSERVATIONS EXPIRÉES N'AVAIT AUCUN INDEX (§4).
    ///
    /// `ExpireStockReservationsWorker` tourne en permanence et pose la même
    /// question à chaque tour : « quelles réservations `Active` ont dépassé leur
    /// échéance ? ». Sans index, c'est un balayage complet.
    ///
    /// ET LA TABLE NE DÉCROÎT JAMAIS. Depuis ISSUE-045, on ne supprime plus les
    /// réservations, on les marque (`Active`, `Confirmed`, `Released`, `Expired`) —
    /// c'est ce qui a donné au stock un historique. Le coût de ce balayage grandit
    /// donc indéfiniment, pour trouver à chaque tour une poignée de lignes.
    ///
    /// INDEX PARTIEL, COMME SON VOISIN `ux_stock_reservations_active_order`.
    ///
    /// Seule une réservation `Active` peut expirer. Un index complet indexerait
    /// toute l'histoire du stock pour servir sa frange vivante — et c'est
    /// exactement le reproche que l'audit faisait à l'ancien index d'outbox sur
    /// `ProcessedOnUtc`, lequel a d'ailleurs été remplacé par un index partiel
    /// pour cette raison.
    ///
    /// AUCUNE REPRISE DE DONNÉES : un index se construit sur l'existant.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Inventory.Infrastructure.Persistence.InventoryDbContext))]
    [Migration("20260902000600_BalayageDesReservationsIndexe")]
    public partial class BalayageDesReservationsIndexe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_expiring",
                schema: "inventory",
                table: "stock_reservations",
                column: "ExpiresAtUtc",
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_expiring",
                schema: "inventory",
                table: "stock_reservations");
        }
    }
}
