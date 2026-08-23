using HBA.Merchants.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Merchants.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES PIÈCES KYB PASSENT DE L'URL À L'IDENTIFIANT DE MÉDIA.
    ///
    /// RENOMMAGE, PAS SUPPRESSION-PUIS-CRÉATION.
    ///
    /// `dotnet ef migrations add` aurait émis `DropColumn("FileUrl")` suivi de
    /// `AddColumn("LegacyFileUrl")`. Le schéma résultant est identique — et TOUTES
    /// les pièces déposées à ce jour deviendraient introuvables. Ce sont des
    /// pièces d'identité : les redemander à chaque vendeur déjà validé, c'est
    /// rouvrir chaque dossier KYB. Le piège s'est déjà refermé deux fois sur ce
    /// dépôt (`menu_items.MenuId`), d'où la migration écrite à la main.
    ///
    /// MIGRATION SÉPARÉE DE TOUTE RÉGÉNÉRATION DE SCHÉMA.
    ///
    /// Même raison : un `migrations add` réécrit le fichier qu'il génère. Celui-ci
    /// ne contient que la reprise, et rien ne le régénérera.
    ///
    /// LES OCTETS NE BOUGENT PAS. Cette migration ne déplace aucun fichier vers
    /// le service média : elle préserve la seule chose qui les désigne encore.
    /// `MediaId` naît donc à zéro pour les pièces existantes — c'est exactement ce
    /// que `KybDocument.IsLegacy` interroge, et ce que les trois routes de lecture
    /// traduisent par « redéposez cette pièce » plutôt que par une erreur.
    ///
    /// La reprise des octets, si elle a lieu un jour, sera un travail distinct :
    /// lire chaque objet du bucket, l'enregistrer comme média du vendeur, écrire
    /// l'identifiant obtenu, et vider `LegacyFileUrl`. Tant qu'elle n'est pas
    /// faite, les deux colonnes coexistent et `IsLegacy` tranche entre elles.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(SellersDbContext))]
    [Migration("20260815000000_RepriseKybVersMedia")]
    public partial class RepriseKybVersMedia : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // L'adresse existante survit sous son nouveau nom. Aucune donnée ne se
            // perd : c'est un renommage in situ, pas une recopie.
            migrationBuilder.RenameColumn(
                name: "FileUrl",
                schema: "sellers",
                table: "kyb_documents",
                newName: "LegacyFileUrl");

            // LA COLONNE ÉTAIT « NOT NULL », ET NE PEUT PLUS L'ÊTRE.
            //
            // `FileUrl` était obligatoire : chaque pièce avait forcément une URL.
            // Les pièces déposées APRÈS la bascule n'en ont aucune — leurs octets
            // sont dans le service média. Sans cet assouplissement, le premier
            // dépôt post-déploiement échouerait sur une contrainte de la base,
            // c'est-à-dire au pire endroit : après le téléversement réussi du
            // fichier, qui resterait alors orphelin dans le bucket.
            migrationBuilder.AlterColumn<string>(
                name: "LegacyFileUrl",
                schema: "sellers",
                table: "kyb_documents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            // NON NULLABLE AVEC DÉFAUT ZÉRO, ET NON « NULLABLE ».
            //
            // Une pièce d'avant la bascule n'a pas de média : le champ doit bien
            // porter une valeur d'absence. La faire porter par NULL obligerait
            // chaque lecture à distinguer trois cas — nul, vide, renseigné — au
            // lieu de deux. `Guid.Empty` dit « aucun média » une seule fois, et
            // `IsLegacy` est le seul endroit qui l'interprète.
            migrationBuilder.AddColumn<Guid>(
                name: "MediaId",
                schema: "sellers",
                table: "kyb_documents",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // LE RETOUR EN ARRIÈRE PERD LES PIÈCES DÉPOSÉES APRÈS LA BASCULE.
            //
            // Elles n'ont pas d'URL : leurs octets sont dans le service média, et
            // c'est `MediaId` — supprimé ici — qui les désigne.
            migrationBuilder.DropColumn(
                name: "MediaId",
                schema: "sellers",
                table: "kyb_documents");

            // CETTE LIGNE EST LE PRIX DU RETOUR EN ARRIÈRE, PAS UN NETTOYAGE.
            //
            // La contrainte « NOT NULL » ne se rétablit pas tant qu'une pièce a une
            // adresse nulle — et toutes celles déposées après la bascule en ont
            // une. On les met donc à la chaîne vide, ce qui les rend définitivement
            // introuvables : leur `MediaId` vient d'être supprimé juste au-dessus,
            // et plus rien ne désigne leurs octets.
            //
            // Autrement dit, ce `Down` est utilisable immédiatement après le
            // déploiement, avant tout nouveau dépôt. Passé ce point, il détruit de
            // l'information — le rejouer suppose d'avoir d'abord exporté la
            // correspondance pièce → média.
            migrationBuilder.Sql(
                "UPDATE sellers.kyb_documents SET \"LegacyFileUrl\" = '' WHERE \"LegacyFileUrl\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "LegacyFileUrl",
                schema: "sellers",
                table: "kyb_documents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "LegacyFileUrl",
                schema: "sellers",
                table: "kyb_documents",
                newName: "FileUrl");
        }
    }
}
