using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Orders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUI A ÉTÉ VENDU NE S'EFFACE PLUS PAR EFFET DE BORD (§8).
    ///
    /// CINQ CLÉS ÉTRANGÈRES EN CASCADE, SUR DEUX NIVEAUX CHACUNE.
    ///
    /// Un <c>DELETE FROM ordering.orders WHERE …</c> mal ciblé emportait, sans une
    /// erreur ni une trace : les lignes de la commande, leurs options de repas, les
    /// imputations de retour — l'argent repris au vendeur — et leurs propres lignes.
    /// L'en-tête aurait survécu avec son total ; plus rien n'aurait expliqué ce
    /// total, ni ce que le client a reçu, ni ce que le vendeur a expédié.
    ///
    /// LES CINQ, ALORS QUE L'AUDIT N'EN NOMMAIT QUE DEUX.
    ///
    /// `order_return_settlements` est de l'argent repris ; `seller_order_lines` est
    /// l'ordre d'expédition. N'en protéger que deux produirait le pire des états :
    /// un effacement qui échoue à mi-chemin. Une protection par moitié ne tient que
    /// tant que l'effacement est transactionnel — ce n'est pas une hypothèse à
    /// prendre sur une donnée comptable.
    ///
    /// VÉRIFIÉ AVANT DE TOUCHER. Trois configurations prévenaient explicitement
    /// que « retirer le OnDelete, geste anodin en apparence, ferait RÉELLEMENT
    /// basculer en sévérance » — une ligne retirée de la collection serait mise à
    /// NULL au lieu d'être supprimée. C'est vrai SANS `IsRequired()` ; il est posé
    /// sur les cinq, et le NOT NULL est en base, donc EF lève au lieu de sévrer. Et
    /// rien dans le dépôt ne retire une ligne d'une commande : une commande ne perd
    /// pas de ligne, elle s'annule.
    ///
    /// AUCUNE REPRISE DE DONNÉES : changer le comportement d'une clé étrangère
    /// ne touche aucune ligne, et aucune donnée existante ne peut violer la nouvelle
    /// contrainte.
    ///
    /// LE NOM TRONQUÉ N'EST PAS UNE FAUTE DE FRAPPE.
    /// `FK_order_return_settlement_lines_order_return_settlements_Order~` porte un
    /// tilde : PostgreSQL plafonne les identifiants à 63 caractères et EF tronque
    /// en marquant la coupe. Le nom doit être repris EXACTEMENT — le « corriger »
    /// ferait échouer le `DropForeignKey` sur une contrainte introuvable.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Orders.Infrastructure.Persistence.OrderingDbContext))]
    [Migration("20260902000400_ContenuDeCommandeNonEffacable")]
    public partial class ContenuDeCommandeNonEffacable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_lines_orders_OrderId",
                schema: "ordering",
                table: "order_lines");

            migrationBuilder.AddForeignKey(
                name: "FK_order_lines_orders_OrderId",
                schema: "ordering",
                table: "order_lines",
                column: "OrderId",
                principalSchema: "ordering",
                principalTable: "orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_order_line_options_order_lines_OrderLineId",
                schema: "ordering",
                table: "order_line_options");

            migrationBuilder.AddForeignKey(
                name: "FK_order_line_options_order_lines_OrderLineId",
                schema: "ordering",
                table: "order_line_options",
                column: "OrderLineId",
                principalSchema: "ordering",
                principalTable: "order_lines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_order_return_settlements_orders_OrderId",
                schema: "ordering",
                table: "order_return_settlements");

            migrationBuilder.AddForeignKey(
                name: "FK_order_return_settlements_orders_OrderId",
                schema: "ordering",
                table: "order_return_settlements",
                column: "OrderId",
                principalSchema: "ordering",
                principalTable: "orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_order_return_settlement_lines_order_return_settlements_Order~",
                schema: "ordering",
                table: "order_return_settlement_lines");

            migrationBuilder.AddForeignKey(
                name: "FK_order_return_settlement_lines_order_return_settlements_Order~",
                schema: "ordering",
                table: "order_return_settlement_lines",
                column: "OrderReturnSettlementId",
                principalSchema: "ordering",
                principalTable: "order_return_settlements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_seller_order_lines_seller_orders_SellerOrderId",
                schema: "ordering",
                table: "seller_order_lines");

            migrationBuilder.AddForeignKey(
                name: "FK_seller_order_lines_seller_orders_SellerOrderId",
                schema: "ordering",
                table: "seller_order_lines",
                column: "SellerOrderId",
                principalSchema: "ordering",
                principalTable: "seller_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_lines_orders_OrderId",
                schema: "ordering",
                table: "order_lines");

            migrationBuilder.AddForeignKey(
                name: "FK_order_lines_orders_OrderId",
                schema: "ordering",
                table: "order_lines",
                column: "OrderId",
                principalSchema: "ordering",
                principalTable: "orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_order_line_options_order_lines_OrderLineId",
                schema: "ordering",
                table: "order_line_options");

            migrationBuilder.AddForeignKey(
                name: "FK_order_line_options_order_lines_OrderLineId",
                schema: "ordering",
                table: "order_line_options",
                column: "OrderLineId",
                principalSchema: "ordering",
                principalTable: "order_lines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_order_return_settlements_orders_OrderId",
                schema: "ordering",
                table: "order_return_settlements");

            migrationBuilder.AddForeignKey(
                name: "FK_order_return_settlements_orders_OrderId",
                schema: "ordering",
                table: "order_return_settlements",
                column: "OrderId",
                principalSchema: "ordering",
                principalTable: "orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_order_return_settlement_lines_order_return_settlements_Order~",
                schema: "ordering",
                table: "order_return_settlement_lines");

            migrationBuilder.AddForeignKey(
                name: "FK_order_return_settlement_lines_order_return_settlements_Order~",
                schema: "ordering",
                table: "order_return_settlement_lines",
                column: "OrderReturnSettlementId",
                principalSchema: "ordering",
                principalTable: "order_return_settlements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_seller_order_lines_seller_orders_SellerOrderId",
                schema: "ordering",
                table: "seller_order_lines");

            migrationBuilder.AddForeignKey(
                name: "FK_seller_order_lines_seller_orders_SellerOrderId",
                schema: "ordering",
                table: "seller_order_lines",
                column: "SellerOrderId",
                principalSchema: "ordering",
                principalTable: "seller_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
