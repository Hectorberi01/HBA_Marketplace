using System;
using HBA.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Analytics.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES DEUX TABLES DU LOT 2.
    ///
    ///   seller_cancellation_daily   ce qu'un vendeur perd en annulations
    ///   payment_daily               les tentatives, par prestataire et par issue
    ///
    /// ELLES N'EXISTENT QUE PARCE QUE TROIS CONTRATS ONT GAGNÉ DES CHAMPS
    /// OPTIONNELS DANS LE MÊME COMMIT. Déployer cette migration sans les
    /// producteurs remplirait `payment_daily` d'une seule ligne « inconnu » par
    /// jour, et laisserait `seller_cancellation_daily` vide — deux tables qui ont
    /// l'air en panne alors qu'elles sont exactes.
    ///
    /// ÉCRITE À LA MAIN, COMME `InitialAnalytics`, et pour la même raison : aucun
    /// SDK .NET sur le poste. La vérification est la même et elle est à faire
    /// AVANT tout déploiement :
    ///
    ///     ./scripts/verifier-migrations.sh --contexte=AnalyticsDbContext
    ///
    /// AUCUN INDEX SECONDAIRE, ET C'EST DÉLIBÉRÉ. Les deux lectures du service
    /// filtrent sur un PRÉFIXE de la clé primaire — (vendeur, jour) pour l'une,
    /// (jour) pour l'autre. Un index de plus coûterait une écriture par événement
    /// consommé pour une lecture que l'index primaire sert déjà.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(AnalyticsDbContext))]
    [Migration("20260907120000_AnnulationsEtPaiements")]
    public partial class AnnulationsEtPaiements : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_daily",
                schema: "analytics",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_daily", x => new { x.Day, x.Provider, x.Currency, x.Outcome });
                });

            migrationBuilder.CreateTable(
                name: "seller_cancellation_daily",
                schema: "analytics",
                columns: table => new
                {
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OrdersCount = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_seller_cancellation_daily", x => new { x.SellerId, x.Day, x.Currency });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "payment_daily", schema: "analytics");
            migrationBuilder.DropTable(name: "seller_cancellation_daily", schema: "analytics");
        }
    }
}
