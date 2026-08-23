using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Food.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES IMAGES PASSENT AU SERVICE MÉDIA (C6, cahier Food §3 et §6).
    ///
    /// CE QU'UNE MIGRATION NE PEUT PAS FAIRE, ET POURQUOI LES ANCIENNES
    /// COLONNES SURVIVENT.
    ///
    /// Un `MediaAsset` exige une empreinte SHA-256, une taille, un type MIME réel
    /// — autant de choses qu'on ne déduit pas d'une URL sans aller relire le
    /// fichier. Fabriquer des lignes média approximatives à partir d'URL
    /// produirait des métadonnées fausses que plus rien ne corrigerait, et une
    /// déduplication qui rapprocherait des fichiers différents.
    ///
    /// Les URL existantes sont donc CONSERVÉES sous un nom qui dit ce qu'elles
    /// sont : `LegacyLogoUrl`, `LegacyImageUrl`. La projection préfère le
    /// `MediaId` et retombe dessus.
    ///
    /// RENOMMAGE, ET NON DROP + ADD.
    ///
    /// EF ne reconnaît pas un renommage de colonne et produit volontiers un
    /// `DropColumn` suivi d'un `AddColumn` — chaque logo existant serait perdu.
    /// Le renommage est donc explicite.
    ///
    /// CETTE MIGRATION A EU UN DOUBLON, ET IL A COÛTÉ UN DÉMARRAGE.
    ///
    /// Une seconde migration écrite à la main — `20260814000000_
    /// RepriseImagesVersMedia` — refaisait EXACTEMENT ces deux renommages, en
    /// croyant les introduire. Sur une base neuve, elle tombait sur
    ///
    ///     42703: column "LogoUrl" does not exist
    ///
    /// puisque la colonne s'appelait déjà `LegacyLogoUrl` depuis ici. Elle n'a
    /// donc jamais pu s'appliquer nulle part, et a été supprimée. Sa
    /// documentation — celle que vous lisez — a été rapatriée ici, à l'endroit
    /// où le changement se produit réellement.
    ///
    /// C'EST UNE SECONDE VÉRITÉ, ET ELLE EST TEMPORAIRE PAR CONSTRUCTION.
    ///
    /// `SetMedia` efface l'URL héritée dès qu'un média est rattaché : le premier
    /// nouveau logo fait disparaître l'ancien. Les colonnes se videront donc
    /// d'elles-mêmes, restaurant après restaurant, et pourront être supprimées
    /// quand elles ne contiendront plus rien. C'est noté dans l'audit.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class ImagesVersMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LogoUrl",
                schema: "food",
                table: "restaurants",
                newName: "LegacyLogoUrl");

            migrationBuilder.RenameColumn(
                name: "ImageUrl",
                schema: "food",
                table: "menu_items",
                newName: "LegacyImageUrl");

            migrationBuilder.AddColumn<Guid>(
                name: "CoverMediaId",
                schema: "food",
                table: "restaurants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LogoMediaId",
                schema: "food",
                table: "restaurants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImageMediaId",
                schema: "food",
                table: "menu_items",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverMediaId",
                schema: "food",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "LogoMediaId",
                schema: "food",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "ImageMediaId",
                schema: "food",
                table: "menu_items");

            migrationBuilder.RenameColumn(
                name: "LegacyLogoUrl",
                schema: "food",
                table: "restaurants",
                newName: "LogoUrl");

            migrationBuilder.RenameColumn(
                name: "LegacyImageUrl",
                schema: "food",
                table: "menu_items",
                newName: "ImageUrl");
        }
    }
}
