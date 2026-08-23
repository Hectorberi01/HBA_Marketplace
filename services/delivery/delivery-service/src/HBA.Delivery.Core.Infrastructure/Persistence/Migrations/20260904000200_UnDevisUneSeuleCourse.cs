using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN DEVIS NE PAIE QU'UNE SEULE COURSE (§5).
    ///
    /// CE N'EST PAS LA CORRECTION — C'EST LE FILET.
    ///
    /// La vraie cause était dans `EfDeliveryPricingStore.ConsumeQuoteAsync`, qui
    /// lisait le devis, testait `Status == "ACTIVE"`, puis écrivait `CONSUMED` :
    /// entre le test et l'écriture, rien ne tenait la ligne. Deux courses
    /// concurrentes passaient toutes deux, et la plateforme payait deux livraisons
    /// pour un devis. Elle est fermée par un `UPDATE … WHERE Status = 'ACTIVE'`
    /// atomique, dans une transaction partagée avec l'outbox.
    ///
    /// Cet index garde l'AUTRE bout de la chaîne : `deliveries.QuoteId`. Il refuse
    /// que deux courses citent le même devis, quel que soit le chemin qui les a
    /// écrites — y compris un chemin futur qui ne passerait pas par le magasin de
    /// tarification.
    ///
    /// INDEX PARTIEL SUR `QuoteId IS NOT NULL`.
    ///
    /// PostgreSQL autorise déjà plusieurs `NULL` dans un index unique, donc le
    /// filtre ne change pas la sémantique — il change le COÛT. La majorité des
    /// courses n'a pas de devis (une commande marketplace part sans devis, ce qui
    /// est un trou de recette connu et documenté ailleurs) : sans filtre, l'index
    /// porterait toutes ces lignes pour ne rien garder.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MIGRATION PEUT ÉCHOUER AU DÉPLOIEMENT, ET C'EST VOULU.
    ///
    /// Si un devis a DÉJÀ payé deux courses, la création de l'index échoue et le
    /// déploiement s'arrête. Décision de HECTOR, et c'est la bonne : un devis
    /// consommé deux fois est une anomalie FINANCIÈRE — une course a été payée sans
    /// tarification propre. La résoudre automatiquement, ce serait décider seul du
    /// sort d'un versement à un livreur, en silence, pendant un déploiement.
    ///
    /// Pour savoir à l'avance si le cas existe :
    ///
    ///   SELECT "QuoteId", count(*), array_agg("Id")
    ///   FROM deliveries.deliveries
    ///   WHERE "QuoteId" IS NOT NULL
    ///   GROUP BY "QuoteId" HAVING count(*) > 1;
    ///
    /// Chaque ligne rendue est une course à arbitrer à la main, pas une donnée à
    /// nettoyer.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Deliveries.Infrastructure.Persistence.DeliveriesDbContext))]
    [Migration("20260904000200_UnDevisUneSeuleCourse")]
    public partial class UnDevisUneSeuleCourse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_deliveries_quote",
                schema: "deliveries",
                table: "deliveries",
                column: "QuoteId",
                unique: true,
                filter: "\"QuoteId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_deliveries_quote",
                schema: "deliveries",
                table: "deliveries");
        }
    }
}
