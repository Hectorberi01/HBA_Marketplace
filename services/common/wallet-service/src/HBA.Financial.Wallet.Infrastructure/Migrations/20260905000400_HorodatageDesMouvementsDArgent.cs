using System;
using HBA.Financial.Wallet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Wallet.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// TROIS TABLES D'ARGENT QUI CHANGENT D'ÉTAT SANS DIRE QUAND (§9.2).
    ///
    /// withdrawals, customer_refunds et seller_earnings portaient toutes un
    /// CreatedAtUtc et aucune Updated*Utc — alors que ce sont exactement les
    /// lignes dont l'état change plusieurs fois, et dont un état intermédiaire
    /// qui dure est un incident financier :
    ///
    ///   • un retrait vendeur en Processing qui ne se solde pas, c'est de
    ///     l'argent parti chez un prestataire sans confirmation ;
    ///   • un remboursement client en Processing, c'est un acheteur qui attend ;
    ///   • un gain vendeur non libéré, c'est une facture qui ne partira pas.
    ///
    /// Dans les trois cas la question est la même — « depuis QUAND ? » — et la
    /// base ne savait pas y répondre. Les portefeuilles eux-mêmes (seller_wallets,
    /// driver_wallets, customer_wallets, platform_wallet) ne sont PAS concernés :
    /// ils portent déjà un UpdatedAtUtc réel, rempli par leur propre Touch().
    ///
    /// NULLABLE, SANS VALEUR PAR DÉFAUT.
    ///
    /// Les lignes antérieures à cette migration restent à NULL, ce qui se lit
    /// « on ne sait pas ». Un DEFAULT now() leur ferait toutes dire qu'elles ont
    /// été touchées à la seconde du déploiement : faux, et faux d'une manière qui
    /// ne se remarque pas — c'est-à-dire pire que l'absence de colonne.
    ///
    /// LA COLONNE N'EXISTE QUE DANS LE MODÈLE EF (propriété fantôme).
    ///
    /// Aucune propriété C# ne lui correspond : c'est une donnée d'EXPLOITATION,
    /// pas une donnée métier, et le domaine ne doit pas pouvoir fonder une règle
    /// sur l'heure d'un UPDATE. Elle est posée par ModuleDbContext à chaque
    /// écriture — INSERT compris, pour que NULL garde un sens unique. Voir
    /// HorodatageExtensions.
    ///
    /// CE QUE CETTE COLONNE NE VERRA PAS.
    ///
    /// Une écriture qui ne touche QUE des lignes enfants ne met pas la ligne
    /// parente en Modified : EF n'émet aucun UPDATE dessus, et l'estampille ne
    /// bouge pas. Même angle mort que le jeton de concurrence xmin, mêmes causes.
    ///
    /// CE N'EST PAS UN JOURNAL D'AUDIT : la colonne dit QUAND, jamais QUI ni
    /// QUOI, et elle est écrasée à chaque écriture. Les deux mécanismes se
    /// complètent et ne se remplacent pas.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(WalletDbContext))]
    [Migration("20260905000400_HorodatageDesMouvementsDArgent")]
    public partial class HorodatageDesMouvementsDArgent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                schema: "settlement",
                table: "withdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                schema: "settlement",
                table: "customer_refunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                schema: "settlement",
                table: "seller_earnings",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DESTRUCTIF : les dates effacées ne se reconstruisent pas. Rejouer
            // la migration recrée une colonne vide, et toutes les lignes
            // existantes redeviennent « on ne sait pas ».
            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                schema: "settlement",
                table: "seller_earnings");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                schema: "settlement",
                table: "customer_refunds");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                schema: "settlement",
                table: "withdrawals");
        }
    }
}
