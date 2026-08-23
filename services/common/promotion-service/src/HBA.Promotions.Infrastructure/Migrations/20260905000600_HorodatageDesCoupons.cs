using System;
using HBA.Promotions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Promotions.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUAND CE COUPON A-T-IL ÉTÉ MODIFIÉ ? (§9.2)
    ///
    /// Un coupon se désactive, se prolonge, voit son plafond changer — et un
    /// coupon qui coûte plus cher que prévu est une réclamation qui commence par
    /// « qui a touché à ça, et quand ». La date de création ne répond pas.
    ///
    /// CETTE COLONNE NE DIT PAS QUI. Pour l'acteur, il faut activer
    /// KeepsAuditTrail sur PromotionsDbContext, avec la table qui va avec — ce
    /// n'est pas fait, et ce n'est pas ce lot.
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
    [DbContext(typeof(PromotionsDbContext))]
    [Migration("20260905000600_HorodatageDesCoupons")]
    public partial class HorodatageDesCoupons : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                schema: "promotions",
                table: "coupons",
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
                schema: "promotions",
                table: "coupons");
        }
    }
}
