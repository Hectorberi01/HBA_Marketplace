using System;
using HBA.Deliveries.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TARIFICATION APPARTIENT À delivery-pricing, ET LE SCHÉMA LE DIT ENFIN.
    ///
    /// TROIS AGRÉGATS ÉTAIENT DANS LE ModelSnapshot SANS EXISTER DANS LE CODE.
    ///
    /// `DeliveryQuote`, `DeliveryZone` et `PricingRule` y étaient déclarés sous le
    /// namespace `HBA.Deliveries.Domain.Pricing` — un namespace INTROUVABLE dans
    /// tout le dépôt. Le domaine de tarification a été déplacé vers
    /// delivery-pricing-service ; le code a été retiré d'ici, les migrations et le
    /// snapshot ne l'ont pas été.
    ///
    /// CE QUE CET ÉCART COÛTAIT, ET QU'AUCUN CONTRÔLE NE VOYAIT.
    ///
    /// Le prochain `dotnet ef migrations add` sur ce contexte aurait généré, tout
    /// seul, une migration supprimant ces quatre tables — sans que personne l'ait
    /// demandé, au milieu d'un diff portant sur autre chose. `check-migrations.py`
    /// ne l'attrape pas : il rejoue les migrations entre elles, il ne compare pas
    /// le snapshot au MODÈLE. C'est ce que le lot 9.4 doit outiller.
    ///
    /// La suppression est donc écrite ICI, à la main, DÉLIBÉRÉMENT, plutôt que
    /// subie plus tard par surprise.
    ///
    /// CETTE MIGRATION DÉTRUIT DES DONNÉES, ET LE `Down` NE LES REND PAS.
    ///
    /// Il recrée quatre tables VIDES. Les devis, zones et grilles qui s'y
    /// trouvaient encore sont perdus. Ce qu'il faut savoir avant de l'appliquer :
    ///
    ///   • ces tables N'ONT PLUS DE PRODUCTEUR depuis la séparation des services.
    ///     Aucune entité, aucun dépôt, aucune route ne les écrit — le seul magasin
    ///     de devis vivant est celui de delivery-pricing ;
    ///   • les lignes qui y restent sont donc ANTÉRIEURES à la séparation, et
    ///     n'ont plus aucun lecteur : `deliveries.QuoteId` n'a jamais porté de clé
    ///     étrangère vers `delivery_quotes`, seulement un index ;
    ///   • les grilles tarifaires en vigueur sont celles de delivery-pricing.
    ///
    /// Pour archiver avant de déployer, si l'exploitation le souhaite :
    ///
    ///     CREATE TABLE deliveries.archive_pricing_rules AS
    ///         SELECT * FROM deliveries.pricing_rules;
    ///     CREATE TABLE deliveries.archive_delivery_zones AS
    ///         SELECT * FROM deliveries.delivery_zones;
    ///     CREATE TABLE deliveries.archive_delivery_quotes AS
    ///         SELECT * FROM deliveries.delivery_quotes;
    ///
    /// L'ORDRE DES SUPPRESSIONS N'EST PAS INDIFFÉRENT.
    ///
    /// `pricing_weight_tiers` porte une clé étrangère vers `pricing_rules`.
    /// Supprimer la table parente d'abord échouerait — et cet échec-là arrive au
    /// démarrage du service, avant l'ouverture du port.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(DeliveriesDbContext))]
    [Migration("20260906000000_TarificationRendueAuServiceQuiLaTient")]
    public partial class TarificationRendueAuServiceQuiLaTient : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "pricing_weight_tiers", schema: "deliveries");
            migrationBuilder.DropTable(name: "pricing_rules", schema: "deliveries");
            migrationBuilder.DropTable(name: "delivery_zones", schema: "deliveries");
            migrationBuilder.DropTable(name: "delivery_quotes", schema: "deliveries");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CE RETOUR EN ARRIÈRE RECRÉE DES TABLES VIDES, ET RIEN D'AUTRE.
            //
            // Les blocs ci-dessous sont RECOPIÉS À L'IDENTIQUE de
            // `20260811091221_AddPricingAndZones` — colonnes, types, précisions,
            // index et clé étrangère. Écrire une forme « suffisante » de mémoire
            // aurait produit des tables qui portent le bon nom et pas la bonne
            // structure : un retour en arrière qui a l'air d'avoir marché.
            //
            // Aucune ligne n'est restaurée pour autant, et aucun code de ce
            // service ne sait plus les remplir. Le rejouer ne ramène pas la
            // tarification ici : elle est chez delivery-pricing.

            migrationBuilder.CreateTable(
                name: "delivery_quotes",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    pickup_latitude = table.Column<double>(type: "double precision", nullable: false),
                    pickup_longitude = table.Column<double>(type: "double precision", nullable: false),
                    dropoff_latitude = table.Column<double>(type: "double precision", nullable: false),
                    dropoff_longitude = table.Column<double>(type: "double precision", nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    Size = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeliveryType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VehicleType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DistanceKm = table.Column<double>(type: "double precision", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BasePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DistancePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ZoneSurcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    VehicleSurcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    WeightSurcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    UrgencySurcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PricingRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PricingVersion = table.Column<int>(type: "integer", nullable: false),
                    PickupZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    DropoffZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedByDeliveryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_quotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_zones",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    boundary = table.Column<string>(type: "jsonb", nullable: false),
                    BaseFee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ExtraFee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    RoadFactor = table.Column<double>(type: "double precision", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_zones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_rules",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    BasePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    PricePerKm = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    MinimumPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    MaximumDistanceKm = table.Column<double>(type: "double precision", nullable: true),
                    DeliveryType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    VehicleType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UrgencySurcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_weight_tiers",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromKg = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    ToKg = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: true),
                    Surcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    pricing_rule_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_weight_tiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pricing_weight_tiers_pricing_rules_pricing_rule_id",
                        column: x => x.pricing_rule_id,
                        principalSchema: "deliveries",
                        principalTable: "pricing_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_quotes_expiry",
                schema: "deliveries",
                table: "delivery_quotes",
                column: "ExpiresAtUtc",
                filter: "\"ConsumedByDeliveryId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_zones_active",
                schema: "deliveries",
                table: "delivery_zones",
                columns: new[] { "IsActive", "Priority" });

            migrationBuilder.CreateIndex(
                name: "ix_pricing_rules_active",
                schema: "deliveries",
                table: "pricing_rules",
                columns: new[] { "IsActive", "ValidFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_weight_tiers_pricing_rule_id",
                schema: "deliveries",
                table: "pricing_weight_tiers",
                column: "pricing_rule_id");
        }
    }
}
