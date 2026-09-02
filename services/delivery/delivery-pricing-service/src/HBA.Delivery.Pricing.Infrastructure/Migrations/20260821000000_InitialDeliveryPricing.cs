using System;
using HBA.Delivery.Pricing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Delivery.Pricing.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// MIGRATION INITIALE — ELLE N'AVAIT JAMAIS ÉTÉ ÉCRITE.
    ///
    /// `DeliveryPricingDbContext` avait un modèle complet — trois entités, leurs
    /// types possédés, leurs longueurs — un dépôt, des commandes, un service gRPC,
    /// et AUCUN dossier `Migrations`. Le schéma `delivery_pricing` n'existait donc
    /// dans aucune base : la première lecture rendait « relation … does not exist ».
    ///
    /// le contrôle `migrations` le signalait depuis le début
    /// (« tables delivery_quotes, delivery_zones, pricing_rules »). Le service
    /// n'était pas non plus inscrit dans `HBA.sln`, donc invisible dans l'IDE —
    /// les deux oublis se protégeaient l'un l'autre.
    ///
    /// ÉCRITE À LA MAIN, comme toutes les migrations de ce dépôt : attributs
    /// `[DbContext]` + `[Migration]` (sans eux, EF ne la charge pas — voir
    /// `20260824000000_AddOrderPaymentId`), pas de `.Designer.cs`, snapshot tenu à
    /// jour dans le même geste.
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// <para>
    /// <b>`pricing_rules."SurgeMultiplier"` est un `numeric` SANS précision</b>, et
    /// c'est fidèle au modèle : `PricingRule.SurgeMultiplier` est un `decimal` qu'aucun
    /// `HasPrecision` ne cadre. Lui inventer ici une précision ferait diverger la base
    /// du modèle, et le prochain `migrations add` produirait une correction fantôme.
    /// Le cadrage se fera dans la couche de configuration, avec sa propre migration.
    /// </para>
    ///
    /// <para>
    /// <b>Les montants de ce module sont des entiers signés</b> (`bigint`), pas des
    /// `numeric(18,2)` comme partout ailleurs. C'est un choix assumé du domaine — le
    /// franc CFA n'a pas de sous-unité — partagé avec promotion-service. La migration
    /// s'y conforme ; la cohabitation de deux représentations de l'argent dans un même
    /// système reste un risque de conversion, documenté à part.
    /// </para>
    /// </summary>
    [DbContext(typeof(DeliveryPricingDbContext))]
    [Migration("20260821000000_InitialDeliveryPricing")]
    public partial class InitialDeliveryPricing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "delivery_pricing");

            // ─────────────────────────────────────────────────────────────────
            // Outbox du module. Forme COMPLÈTE dès la création : les colonnes de
            // réessai (`AttemptCount`, `NextAttemptAtUtc`, `DeadLetteredOnUtc`) et
            // `TraceParent` ont été ajoutées après coup aux modules historiques,
            // par des migrations séparées. Un module neuf n'a aucune raison de
            // rejouer cette dette.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "delivery_pricing",
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
                    DeadLetteredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TraceParent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            // ─────────────────────────────────────────────────────────────────
            // Devis de course. `Pickup`, `Dropoff` et `Components` sont des types
            // POSSÉDÉS déclarés sans configuration de colonnes : EF les aplatit
            // dans la même table sous la forme `{Navigation}_{Propriété}`.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "delivery_quotes",
                schema: "delivery_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: true),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    DistanceMeters = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    VehicleType = table.Column<string>(type: "text", nullable: true),
                    ServiceLevel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Subtotal = table.Column<long>(type: "bigint", nullable: false),
                    Discount = table.Column<long>(type: "bigint", nullable: false),
                    Total = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PricingVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConsumedByDeliveryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Pickup_Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Pickup_Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Dropoff_Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Dropoff_Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Components_BaseFee = table.Column<long>(type: "bigint", nullable: false),
                    Components_DistanceFee = table.Column<long>(type: "bigint", nullable: false),
                    Components_MinuteFee = table.Column<long>(type: "bigint", nullable: false),
                    Components_SurgeFee = table.Column<long>(type: "bigint", nullable: false),
                    Components_Discount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_quotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_rules",
                schema: "delivery_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ServiceLevel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VehicleType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BaseFee = table.Column<long>(type: "bigint", nullable: false),
                    PerKmFee = table.Column<long>(type: "bigint", nullable: false),
                    PerMinuteFee = table.Column<long>(type: "bigint", nullable: false),
                    MinFee = table.Column<long>(type: "bigint", nullable: false),
                    MaxFee = table.Column<long>(type: "bigint", nullable: true),
                    ActiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    SurgeMultiplier = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_zones",
                schema: "delivery_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    GeometryRef = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Serviceable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_zones", x => x.Id);
                });

            // Les deux index partiels de l'outbox, identiques à ceux des 22 autres
            // modules : le lot en attente d'un côté, les lettres mortes de l'autre.
            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "delivery_pricing",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "delivery_pricing",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "delivery_quotes", schema: "delivery_pricing");
            migrationBuilder.DropTable(name: "delivery_zones", schema: "delivery_pricing");
            migrationBuilder.DropTable(name: "outbox_messages", schema: "delivery_pricing");
            migrationBuilder.DropTable(name: "pricing_rules", schema: "delivery_pricing");
        }
    }
}
