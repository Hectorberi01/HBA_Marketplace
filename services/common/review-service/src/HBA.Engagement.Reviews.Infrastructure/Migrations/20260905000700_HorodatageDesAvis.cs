using System;
using HBA.Engagement.Reviews.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Engagement.Reviews.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUAND CET AVIS A-T-IL ÉTÉ MODIFIÉ OU MODÉRÉ ? (§9.2)
    ///
    /// Un avis change de statut (publié, masqué, rejeté), reçoit une réponse du
    /// vendeur, se corrige. Seule sa création était datée — et un acheteur qui
    /// conteste une modération demande toujours quand elle a eu lieu.
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
    [DbContext(typeof(ReviewsDbContext))]
    [Migration("20260905000700_HorodatageDesAvis")]
    public partial class HorodatageDesAvis : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                schema: "reviews",
                table: "reviews",
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
                schema: "reviews",
                table: "reviews");
        }
    }
}
