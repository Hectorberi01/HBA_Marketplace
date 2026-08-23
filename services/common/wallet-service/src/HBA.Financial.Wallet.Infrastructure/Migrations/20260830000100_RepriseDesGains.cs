using HBA.Financial.Wallet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Wallet.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA REPRISE DES GAINS SUR VENTE REMBOURSÉE (ISSUE-050).
    ///
    /// UN GAIN VERSÉ SUR UNE VENTE REMBOURSÉE N'ÉTAIT JAMAIS REPRIS.
    ///
    /// `EarningStatus.Reversed` existait dans l'énumération depuis l'origine et
    /// RIEN ne le posait jamais. La contre-passation d'un retour débitait le
    /// PORTEFEUILLE du vendeur et laissait le gain « Released » : le lot de
    /// reversement suivant le ramassait et le comptait payable. La plateforme
    /// reprenait l'argent d'une main et le reversait de l'autre, et la vente
    /// remboursée restait dans le relevé du vendeur.
    ///
    /// QUATRE COLONNES ET NON UN DRAPEAU, PARCE QU'UN RETOUR EST SOUVENT
    /// PARTIEL.
    ///
    /// Un client renvoie un article sur trois. Un booléen « repris » sortirait
    /// toute la commande du circuit ; une seule colonne « brut repris » ne dirait
    /// ni quelle commission ni quels frais ont été restitués, et il faudrait les
    /// recalculer — c'est la duplication de calcul monétaire que
    /// `ReverseEarningsOnReturnRefundedHandler` dénonce en tête de fichier. Les
    /// quatre montants sont donc inscrits tels qu'ils ont été appliqués, et le
    /// statut ne bascule « Reversed » que lorsque le brut repris atteint le brut
    /// d'origine.
    ///
    /// DÉFAUT À 0, ET C'EST LE POINT QUI FAIT TOMBER CE GENRE DE MIGRATION.
    ///
    /// Un `ADD COLUMN` sans défaut remplit les lignes existantes de NULL. Les
    /// propriétés sont des `decimal` NON nullables : le premier chargement d'un
    /// gain antérieur échouerait à la matérialisation, c'est-à-dire au premier
    /// lot de reversement, en production, sur des données que personne n'a
    /// touchées. Le défaut est donc porté ICI, en base, et non par
    /// `HasDefaultValue` côté modèle — qui ferait aussi cesser EF d'écrire les
    /// zéros explicites d'un gain neuf.
    ///
    /// AUCUNE REPRISE DE DONNÉES, ET IL FAUT LE SAVOIR.
    ///
    /// Les gains ANTÉRIEURS dont la vente a été remboursée partent à zéro : rien
    /// ne distingue en base un gain jamais remboursé d'un gain remboursé avant
    /// cette migration. Le grand livre porte bien les écritures `("refund",
    /// returnRequestId)` correspondantes, mais rien ne les rattache à un gain
    /// précis — le rapprochement se ferait à la main, commande par commande. Sur
    /// une base déjà exploitée, ces gains-là resteront donc payables une dernière
    /// fois.
    ///
    /// <para>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de fichier
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE — les colonnes
    /// n'existent jamais, et le service tombe sur « column does not exist » à la
    /// première lecture d'un gain.
    /// </para>
    ///
    /// <para>
    /// Le nom de la classe de contexte est `WalletDbContext` (le FICHIER
    /// s'appelle `SettlementDbContext.cs`, la classe non).
    /// </para>
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(WalletDbContext))]
    [Migration("20260830000100_RepriseDesGains")]
    public partial class RepriseDesGains : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReversedGrossAmount",
                schema: "settlement",
                table: "seller_earnings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReversedCommissionAmount",
                schema: "settlement",
                table: "seller_earnings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReversedProviderFeeAmount",
                schema: "settlement",
                table: "seller_earnings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReversedNetAmount",
                schema: "settlement",
                table: "seller_earnings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReversedNetAmount",
                schema: "settlement",
                table: "seller_earnings");

            migrationBuilder.DropColumn(
                name: "ReversedProviderFeeAmount",
                schema: "settlement",
                table: "seller_earnings");

            migrationBuilder.DropColumn(
                name: "ReversedCommissionAmount",
                schema: "settlement",
                table: "seller_earnings");

            migrationBuilder.DropColumn(
                name: "ReversedGrossAmount",
                schema: "settlement",
                table: "seller_earnings");
        }
    }
}
