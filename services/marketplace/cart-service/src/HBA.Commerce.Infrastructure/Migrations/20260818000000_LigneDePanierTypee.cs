using HBA.Commerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Commerce.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE PANIER SAIT DÉSORMAIS PORTER DES PLATS.
    ///
    /// Une ligne de panier était toujours une offre marketplace : SKU, stock, lieu
    /// d'expédition. Un plat n'a rien de tout cela, mais porte des options
    /// choisies. Le discriminant `Kind` dit lequel des deux jeux de colonnes lire.
    ///
    /// TOUTES LES LIGNES EXISTANTES SONT DE LA MARCHANDISE.
    ///
    /// La valeur par défaut « Goods » n'est pas une commodité : c'est la vérité sur
    /// les données en place, puisqu'aucun plat n'a jamais pu entrer dans un panier.
    /// Elle est posée sur la colonne pour que les lignes déjà écrites soient
    /// correctes sans balayage.
    ///
    /// L'INDEX UNIQUE DEVIENT FILTRÉ, ET C'EST LE POINT CRITIQUE.
    ///
    /// `("CartId","OfferId") UNIQUE` couvrait toutes les lignes. Or chaque ligne
    /// food porte `OfferId = '00000000-…'` : le SECOND plat ajouté à un panier
    /// aurait violé la contrainte, et le client aurait reçu une erreur de base de
    /// données en cliquant sur « ajouter ». Le filtre conserve la garantie pour la
    /// marchandise — une offre ne figure qu'une fois — et la lève là où elle n'a
    /// pas de sens.
    ///
    /// L'unicité d'une ligne food repose sur la combinaison plat + options, qui ne
    /// s'exprime pas en une colonne : elle est vérifiée par l'agrégat.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(CartDbContext))]
    [Migration("20260818000000_LigneDePanierTypee")]
    public partial class LigneDePanierTypee : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── L'ancien index, avant d'ajouter la colonne qu'il doit filtrer ──
            migrationBuilder.DropIndex(
                name: "IX_cart_items_CartId_OfferId",
                schema: "cart",
                table: "cart_items");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "cart",
                table: "cart_items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Goods");

            migrationBuilder.AddColumn<Guid>(
                name: "RestaurantId",
                schema: "cart",
                table: "cart_items",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<Guid>(
                name: "MenuItemId",
                schema: "cart",
                table: "cart_items",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "cart",
                table: "cart_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // TABLE ENFANT, PAS UNE COLONNE JSON.
            //
            // EF Core 8 persiste une collection primitive mais la relit VIDE — le
            // défaut a déjà été constaté sur les pièces jointes de message, et
            // corrigé de la même façon. Une option relue vide, ici, c'est un plat
            // qui part en cuisine sans les choix du client.
            migrationBuilder.CreateTable(
                name: "cart_item_options",
                schema: "cart",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cart_item_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cart_item_options_cart_items_CartItemId",
                        column: x => x.CartItemId,
                        principalSchema: "cart",
                        principalTable: "cart_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cart_item_options_CartItemId",
                schema: "cart",
                table: "cart_item_options",
                column: "CartItemId");

            // ── Le même index, mais borné à la marchandise ──
            migrationBuilder.CreateIndex(
                name: "IX_cart_items_CartId_OfferId",
                schema: "cart",
                table: "cart_items",
                columns: new[] { "CartId", "OfferId" },
                unique: true,
                filter: "\"Kind\" = 'Goods'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CE RETOUR EN ARRIÈRE PERD LES PANIERS DE RESTAURATION.
            //
            // Les lignes food resteraient en base avec `OfferId` vide, et l'index
            // unique non filtré les REFUSERAIT dès le second plat d'un même
            // panier — la migration échouerait sur des données réelles. On les
            // supprime donc explicitement : un panier non validé est une donnée
            // que le client peut refaire, contrairement à une commande.
            migrationBuilder.Sql("DELETE FROM cart.cart_items WHERE \"Kind\" = 'Food';");

            migrationBuilder.DropTable(
                name: "cart_item_options",
                schema: "cart");

            migrationBuilder.DropIndex(
                name: "IX_cart_items_CartId_OfferId",
                schema: "cart",
                table: "cart_items");

            migrationBuilder.DropColumn(name: "Notes", schema: "cart", table: "cart_items");
            migrationBuilder.DropColumn(name: "MenuItemId", schema: "cart", table: "cart_items");
            migrationBuilder.DropColumn(name: "RestaurantId", schema: "cart", table: "cart_items");
            migrationBuilder.DropColumn(name: "Kind", schema: "cart", table: "cart_items");

            migrationBuilder.CreateIndex(
                name: "IX_cart_items_CartId_OfferId",
                schema: "cart",
                table: "cart_items",
                columns: new[] { "CartId", "OfferId" },
                unique: true);
        }
    }
}
