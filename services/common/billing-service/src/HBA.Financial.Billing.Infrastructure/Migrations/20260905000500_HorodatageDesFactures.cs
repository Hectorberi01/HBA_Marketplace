using System;
using HBA.Financial.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Billing.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUAND CETTE FACTURE A-T-ELLE ÉTÉ TOUCHÉE POUR LA DERNIÈRE FOIS ? (§9.2)
    ///
    /// Une facture naît en brouillon, s'émet, s'acquitte, s'annule. Seule sa
    /// création était datée. Une facture émise et jamais payée ne disait pas
    /// depuis quand elle attendait — c'est-à-dire s'il fallait relancer.
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
    [DbContext(typeof(BillingDbContext))]
    [Migration("20260905000500_HorodatageDesFactures")]
    public partial class HorodatageDesFactures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                schema: "billing",
                table: "invoices",
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
                schema: "billing",
                table: "invoices");
        }
    }
}
