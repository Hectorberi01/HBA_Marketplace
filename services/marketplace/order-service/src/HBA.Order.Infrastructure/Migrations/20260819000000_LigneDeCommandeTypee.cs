using HBA.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Orders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA COMMANDE SAIT DÉSORMAIS PORTER DES PLATS.
    ///
    /// Une ligne de commande était toujours une offre : SKU, stock réservé, lieu
    /// d'expédition. Un plat n'a rien de tout cela, mais porte des options et une
    /// note pour la cuisine. Le discriminant `Kind` dit lequel des deux jeux de
    /// colonnes lire, et surtout ce qui doit se produire APRÈS le paiement.
    ///
    /// TOUTES LES LIGNES EXISTANTES SONT DE LA MARCHANDISE.
    ///
    /// La valeur par défaut « Goods » dit la vérité sur les données en place :
    /// aucun plat n'a jamais pu entrer dans une commande. Elle est posée sur la
    /// colonne pour que les lignes déjà écrites soient correctes sans balayage.
    ///
    /// CETTE MIGRATION ACCOMPAGNE UNE CORRECTION QUI NE LEVAIT AUCUNE ERREUR.
    ///
    /// Avant elle, un panier de plats produisait une commande dont chaque ligne
    /// avait un SKU vide. `TryReserveAsync` répond VRAI pour un SKU sans
    /// enregistrement de stock — comportement voulu pour les articles non suivis —
    /// si bien que la réservation « réussissait ». La commande partait au paiement,
    /// puis Shipping en faisait un colis attribué au vendeur « 00000000-… ». Le
    /// client était débité, et personne ne cuisinait.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(OrderingDbContext))]
    [Migration("20260819000000_LigneDeCommandeTypee")]
    public partial class LigneDeCommandeTypee : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "ordering",
                table: "order_lines",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Goods");

            migrationBuilder.AddColumn<Guid>(
                name: "RestaurantId",
                schema: "ordering",
                table: "order_lines",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<Guid>(
                name: "MenuItemId",
                schema: "ordering",
                table: "order_lines",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "ordering",
                table: "order_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // TABLE ENFANT, PAS UNE COLONNE JSON.
            //
            // EF Core 8 persiste une collection primitive mais la relit VIDE — le
            // défaut a déjà coûté deux corrections dans ce dépôt. Une option relue
            // vide, ici, c'est une commande payée qui part en cuisine sans les
            // choix du client, et un plat servi qui n'est pas celui qu'il a réglé.
            migrationBuilder.CreateTable(
                name: "order_line_options",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_line_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_line_options_order_lines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalSchema: "ordering",
                        principalTable: "order_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_line_options_OrderLineId",
                schema: "ordering",
                table: "order_line_options",
                column: "OrderLineId");

            // Index PARTIEL : sans le filtre, toutes les lignes de marchandise
            // s'indexeraient sous la même clé vide — le pire cas pour un B-tree.
            migrationBuilder.CreateIndex(
                name: "IX_order_lines_RestaurantId",
                schema: "ordering",
                table: "order_lines",
                column: "RestaurantId",
                filter: "\"Kind\" = 'Food'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CE RETOUR EN ARRIÈRE DÉTRUIT DES COMMANDES PAYÉES.
            //
            // Contrairement au panier, une commande de repas ne se refait pas : elle
            // a été réglée. Retirer `Kind`, `RestaurantId` et les options laisse des
            // lignes qui ne désignent plus ni article ni restaurant — la commande
            // existe, le montant aussi, et plus rien ne dit ce qui a été acheté.
            //
            // Ce `Down` n'est utilisable qu'immédiatement après le déploiement,
            // avant la première commande de repas. Passé ce point, il faut d'abord
            // exporter ces lignes.
            migrationBuilder.DropIndex(
                name: "IX_order_lines_RestaurantId",
                schema: "ordering",
                table: "order_lines");

            migrationBuilder.DropTable(
                name: "order_line_options",
                schema: "ordering");

            migrationBuilder.DropColumn(name: "Notes", schema: "ordering", table: "order_lines");
            migrationBuilder.DropColumn(name: "MenuItemId", schema: "ordering", table: "order_lines");
            migrationBuilder.DropColumn(name: "RestaurantId", schema: "ordering", table: "order_lines");
            migrationBuilder.DropColumn(name: "Kind", schema: "ordering", table: "order_lines");
        }
    }
}
