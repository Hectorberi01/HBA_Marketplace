using System;
using HBA.FoodCarts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.FoodCarts.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE PANIER DE RESTAURATION PREND SON PROPRE SCHÉMA.
    ///
    /// AUCUNE REPRISE DE DONNÉES, ET C'EST UNE DÉCISION, PAS UN OUBLI.
    ///
    /// Les lignes de repas du schéma `cart` ne sont pas migrées ici. Le lot qui
    /// purge le discriminant `Kind` de la marketplace les supprimera. Un panier
    /// est une intention en cours, pas un fait comptable : le perdre coûte au
    /// client quelques secondes de ressaisie, là où une reprise partielle
    /// laisserait des paniers à moitié lisibles des deux côtés.
    ///
    /// Ce raisonnement ne vaudra PAS pour les commandes.
    ///
    /// TROIS TABLES, ET AUCUNE COLONNE VIDE.
    ///
    /// `cart.cart_items` portait onze colonnes dont sept n'avaient de sens que
    /// pour la marchandise — offre, produit, catégorie, vendeur, SKU, lieu
    /// d'expédition — plus un `Kind` pour dire lesquelles lire. Une ligne de repas
    /// y stockait sept zéros et une chaîne vide. Ici, chaque colonne est lue.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(FoodCartDbContext))]
    [Migration("20260819180000_InitialFoodCart")]
    public partial class InitialFoodCart : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "food_cart");

            migrationBuilder.CreateTable(
                name: "food_carts",
                schema: "food_cart",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PromotionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_food_carts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "food_cart_items",
                schema: "food_cart",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnitBaseAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    FoodCartId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_food_cart_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_food_cart_items_food_carts_FoodCartId",
                        column: x => x.FoodCartId,
                        principalSchema: "food_cart",
                        principalTable: "food_carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "food_cart_item_options",
                schema: "food_cart",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FoodCartItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_food_cart_item_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_food_cart_item_options_food_cart_items_FoodCartItemId",
                        column: x => x.FoodCartItemId,
                        principalSchema: "food_cart",
                        principalTable: "food_cart_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "food_cart",
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

            migrationBuilder.CreateIndex(
                name: "IX_food_carts_BuyerId_Status",
                schema: "food_cart",
                table: "food_carts",
                columns: new[] { "BuyerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_food_cart_items_FoodCartId",
                schema: "food_cart",
                table: "food_cart_items",
                column: "FoodCartId");

            migrationBuilder.CreateIndex(
                name: "IX_food_cart_item_options_FoodCartItemId",
                schema: "food_cart",
                table: "food_cart_item_options",
                column: "FoodCartItemId");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "food_cart",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "food_cart",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "food_cart_item_options", schema: "food_cart");
            migrationBuilder.DropTable(name: "outbox_messages", schema: "food_cart");
            migrationBuilder.DropTable(name: "food_cart_items", schema: "food_cart");
            migrationBuilder.DropTable(name: "food_carts", schema: "food_cart");
        }
    }
}
