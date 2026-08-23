using System;
using HBA.FoodOrders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HBA.FoodOrders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA COMMANDE DE REPAS PREND SON PROPRE SCHÉMA.
    ///
    /// AUCUNE REPRISE DES COMMANDES EXISTANTES DANS CETTE MIGRATION.
    ///
    /// Le raisonnement qui valait pour le panier — « une intention en cours se
    /// ressaisit » — NE VAUT PAS ici : une commande est un fait comptable, elle
    /// est payée, et elle se conserve. Les commandes de repas déjà passées
    /// restent donc dans le schéma `ordering`, où elles continueront de se lire.
    ///
    /// Ce qu'il ne faut PAS faire : les recopier ici et les laisser là-bas. Deux
    /// exemplaires de la même vente, deux statuts qui divergeront à la première
    /// annulation, et personne pour dire lequel a été facturé. Le lot de purge
    /// tranchera — reprise ou clôture de l'ancien chemin — avec les chiffres
    /// réels sous les yeux.
    ///
    /// TROIS DIFFÉRENCES DE SCHÉMA AVEC `ordering`, ET AUCUNE N'EST COSMÉTIQUE.
    ///
    ///   1. `CartId` PORTE UN INDEX UNIQUE. Le passage en commande n'était pas
    ///      idempotent : un double-clic créait deux commandes et deux paiements.
    ///   2. `Name` EST FIGÉ SUR LA LIGNE. `order_lines` n'en portait aucun — une
    ///      fiche renommée réécrivait rétroactivement ce que le client avait
    ///      acheté, une fiche supprimée rendait la ligne muette.
    ///   3. AUCUNE COLONNE DE MARCHANDISE. `order_lines` en portait cinq —
    ///      `OfferId`, `ProductId`, `SellerId`, `Sku`, `ShipFromLocationId` —
    ///      vides pour tout repas, plus un `Kind` pour dire lesquelles lire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(MealOrderingDbContext))]
    [Migration("20260819190000_InitialFoodOrdering")]
    public partial class InitialFoodOrdering : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "food_ordering");

            migrationBuilder.CreateTable(
                name: "meal_orders",
                schema: "food_ordering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PromotionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalSellerDiscount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalPlatformDiscount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ShippingFee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DeliveryQuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CustomerNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnderReviewSinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShipToLabel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ShipToRecipient = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ShipToPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ShipToCommuneCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ShipToQuartier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ShipToLandmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShipToLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShipToCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ShipToLatitude = table.Column<double>(type: "double precision", nullable: true),
                    ShipToLongitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meal_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "meal_order_lines",
                schema: "food_ordering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitBasePrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SellerDiscount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PlatformDiscount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    FinalUnitPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MealOrderId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meal_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meal_order_lines_meal_orders_MealOrderId",
                        column: x => x.MealOrderId,
                        principalSchema: "food_ordering",
                        principalTable: "meal_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meal_order_line_options",
                schema: "food_ordering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealOrderLineId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meal_order_line_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meal_order_line_options_meal_order_lines_MealOrderLineId",
                        column: x => x.MealOrderLineId,
                        principalSchema: "food_ordering",
                        principalTable: "meal_order_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "food_ordering",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "food_ordering",
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

            // L'UNICITÉ DU PANIER — VOIR L'ENCADRÉ DE `MealOrderConfiguration`.
            //
            // C'est la seule chose qui ferme la course entre deux requêtes
            // simultanées de passage en commande. La lecture préalable ne la voit
            // pas : les deux lisent « aucune commande » avant que l'une ait écrit.
            migrationBuilder.CreateIndex(
                name: "IX_meal_orders_CartId",
                schema: "food_ordering",
                table: "meal_orders",
                column: "CartId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meal_orders_BuyerId_CreatedAtUtc",
                schema: "food_ordering",
                table: "meal_orders",
                columns: new[] { "BuyerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_meal_orders_RestaurantId_CreatedAtUtc",
                schema: "food_ordering",
                table: "meal_orders",
                columns: new[] { "RestaurantId", "CreatedAtUtc" });

            // Index PARTIEL : la file d'arbitrage est minuscule à côté de la
            // table, et un index complet ferait payer chaque commande normale
            // pour servir les rares bloquées.
            migrationBuilder.CreateIndex(
                name: "ix_meal_orders_under_review",
                schema: "food_ordering",
                table: "meal_orders",
                column: "UnderReviewSinceUtc",
                filter: "\"UnderReviewSinceUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_meal_order_lines_MealOrderId",
                schema: "food_ordering",
                table: "meal_order_lines",
                column: "MealOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_meal_order_line_options_MealOrderLineId",
                schema: "food_ordering",
                table: "meal_order_line_options",
                column: "MealOrderLineId");

            // « qu'est-il arrivé à CETTE commande » — la question d'un litige.
            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_EntityType_EntityId_OccurredOnUtc",
                schema: "food_ordering",
                table: "audit_entries",
                columns: new[] { "EntityType", "EntityId", "OccurredOnUtc" });

            // « qu'a fait CE compte » — la question d'une enquête.
            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_ActorUserId_OccurredOnUtc",
                schema: "food_ordering",
                table: "audit_entries",
                columns: new[] { "ActorUserId", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "food_ordering",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "food_ordering",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "meal_order_line_options", schema: "food_ordering");
            migrationBuilder.DropTable(name: "audit_entries", schema: "food_ordering");
            migrationBuilder.DropTable(name: "outbox_messages", schema: "food_ordering");
            migrationBuilder.DropTable(name: "meal_order_lines", schema: "food_ordering");
            migrationBuilder.DropTable(name: "meal_orders", schema: "food_ordering");
        }
    }
}
