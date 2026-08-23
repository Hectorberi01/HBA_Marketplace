using HBA.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Catalog.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES IMAGES PRODUIT PASSENT À L'IDENTIFIANT DE MÉDIA.
    ///
    /// `Url` NE BOUGE PAS, ET C'EST TOUT L'INTÉRÊT DE CETTE MIGRATION.
    ///
    /// Contrairement aux pièces KYB et aux visuels de restaurant, la colonne `Url`
    /// n'est pas renommée en « Legacy… » : elle reste, et devient la copie de
    /// lecture décrite sur `ProductMedia`. Toutes les vignettes du site — listes,
    /// résultats de recherche, paniers — continuent donc de s'afficher pendant et
    /// après le déploiement, sans qu'aucune requête ne change.
    ///
    /// RENOMMAGE DE `ExternalId`, PAS SUPPRESSION.
    ///
    /// `dotnet ef migrations add` aurait émis DropColumn + AddColumn. Ce champ est
    /// la seule chose qui désigne encore les fichiers dans l'ANCIEN stockage
    /// (hbamediacore / R2 du module) : le perdre rendrait tout nettoyage ultérieur
    /// impossible, et ces octets seraient facturés indéfiniment sans que rien ne
    /// puisse les retrouver. Le piège s'est déjà refermé deux fois sur ce dépôt.
    ///
    /// LES OCTETS NE SONT PAS DÉPLACÉS.
    ///
    /// `MediaId` naît à zéro pour les lignes existantes — c'est ce que
    /// `ProductMedia.IsLegacy` interroge. Conséquences assumées, et vraies tant
    /// que la reprise des octets n'a pas eu lieu :
    ///
    ///   • détacher une vieille image ne supprime aucun fichier (aucun média à
    ///     nommer) ; les octets restent dans l'ancien stockage ;
    ///   • ces images n'ont pas de variantes générées par le service média ;
    ///   • leur URL n'est jamais rafraîchie — pour elles, elle EST la vérité.
    ///
    /// La reprise des octets, si elle a lieu, sera un travail distinct : lire
    /// chaque objet, l'enregistrer comme média du vendeur, écrire le `MediaId`
    /// obtenu et la nouvelle URL, puis effacer l'ancien objet.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260816000000_RepriseImagesProduitVersMedia")]
    public partial class RepriseImagesProduitVersMedia : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // L'ancienne référence survit sous un nom qui dit ce qu'elle est.
            migrationBuilder.RenameColumn(
                name: "ExternalId",
                schema: "catalog",
                table: "product_media",
                newName: "LegacyExternalId");

            // NON NULLABLE AVEC DÉFAUT ZÉRO, ET NON « NULLABLE ».
            //
            // Une image d'avant la bascule n'a pas de média : le champ doit porter
            // une valeur d'absence. La faire porter par NULL obligerait chaque
            // lecture à distinguer trois cas — nul, vide, renseigné — au lieu de
            // deux. `Guid.Empty` dit « aucun média » une seule fois, et `IsLegacy`
            // est le seul endroit qui l'interprète.
            migrationBuilder.AddColumn<Guid>(
                name: "MediaId",
                schema: "catalog",
                table: "product_media",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // LE RETOUR EN ARRIÈRE PERD LES IMAGES DÉPOSÉES APRÈS LA BASCULE.
            //
            // Leur `Url` survit — elles resteront donc AFFICHÉES, ce qui rend ce
            // Down moins destructeur que celui des pièces KYB. Ce qui se perd,
            // c'est le lien vers le service média : plus moyen de supprimer ces
            // fichiers ni de régénérer leurs variantes.
            migrationBuilder.DropColumn(
                name: "MediaId",
                schema: "catalog",
                table: "product_media");

            migrationBuilder.RenameColumn(
                name: "LegacyExternalId",
                schema: "catalog",
                table: "product_media",
                newName: "ExternalId");
        }
    }
}
