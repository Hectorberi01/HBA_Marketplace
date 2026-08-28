using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Delivery.Pricing.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// D'OÙ VENAIT LE CHIFFRE QUI A FACTURÉ CETTE COURSE — deux colonnes sur
    /// <c>delivery_pricing.delivery_quotes</c>.
    ///
    /// CE QUE ÇA RÉPARE. Un devis portait un nombre de mètres et rien ne disait
    /// si l'appelant l'avait mesuré ou si le service avait tiré une ligne droite
    /// entre deux points. Les deux produisaient un devis d'apparence identique,
    /// et la distance entre directement dans le prix — <c>km × PerKmFee</c>. Un
    /// litige sur une facture ne pouvait donc pas être instruit.
    ///
    /// POURQUOI LE FACTEUR EST PERSISTÉ ET PAS RELU DE LA CONFIGURATION.
    /// La configuration change. Un devis chiffré avec un facteur de 1,0 doit
    /// rester explicable après un passage à 1,3, sans quoi on jugerait un prix
    /// d'hier avec le réglage d'aujourd'hui.
    ///
    /// ÉCRITE À LA MAIN, comme <c>JournalDAuditDeliveryPricing</c> dans ce même
    /// service. <c>[DbContext]</c> et <c>[Migration]</c> sont portés par la
    /// classe, il n'y a pas de <c>.Designer.cs</c>, et le snapshot est mis à jour
    /// dans le même commit. Si l'un des deux attributs manquait, EF ignorerait
    /// cette migration EN SILENCE et les colonnes n'existeraient pas — le service
    /// démarrerait, puis échouerait au premier devis.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUE CETTE MIGRATION NE FAIT PAS, ET IL FAUT LE SAVOIR.
    ///
    ///   • AUCUNE REPRISE DES LIGNES EXISTANTES. Les devis déjà en base reçoivent
    ///     <c>''</c> et <c>0</c>, qui se lisent « on ne sait pas ». On POURRAIT
    ///     les marquer <c>FALLBACK_HAVERSINE</c> — c'était le seul chemin avant
    ///     ce commit — mais ce serait une déduction, pas une donnée : rien en
    ///     base ne dit si l'appelant avait fourni sa propre distance. Une colonne
    ///     qui affirme ce qu'elle n'a pas observé est pire que vide.
    ///
    ///   • ELLE NE CHANGE AUCUN PRIX. Le facteur par défaut vaut 1,0 ; les devis
    ///     produits après cette migration sont chiffrés à l'identique de ceux
    ///     d'avant. Ce commit rend le défaut VISIBLE et RÉGLABLE, il ne le corrige
    ///     pas — voir <c>EstimationItineraireOptions</c>.
    ///
    ///   • <c>IF NOT EXISTS</c> : une base alignée à la main ne doit pas empêcher
    ///     le service de démarrer, les migrations s'appliquant au démarrage.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Delivery.Pricing.Infrastructure.Persistence.DeliveryPricingDbContext))]
    [Migration("20260828120000_SourceEstimationDevis")]
    public partial class SourceEstimationDevis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOT NULL avec DEFAULT en un seul geste : PostgreSQL sait remplir la
            // colonne sans réécrire la table depuis la version 11. Faire l'inverse
            // — ajouter nullable, remplir, puis contraindre — prendrait trois
            // migrations pour le même résultat, avec deux fenêtres pendant
            // lesquelles le modèle EF et la base ne s'accordent pas.
            migrationBuilder.Sql(@"
                ALTER TABLE delivery_pricing.delivery_quotes
                    ADD COLUMN IF NOT EXISTS ""SourceEstimation""
                        character varying(40) NOT NULL DEFAULT '';");

            migrationBuilder.Sql(@"
                ALTER TABLE delivery_pricing.delivery_quotes
                    ADD COLUMN IF NOT EXISTS ""FacteurCorrectionApplique""
                        numeric(4,2) NOT NULL DEFAULT 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Réversible sans perte métier : on ne perd que la traçabilité de
            // provenance, qui n'entre dans aucun calcul.
            migrationBuilder.Sql(@"
                ALTER TABLE delivery_pricing.delivery_quotes
                    DROP COLUMN IF EXISTS ""SourceEstimation"";");

            migrationBuilder.Sql(@"
                ALTER TABLE delivery_pricing.delivery_quotes
                    DROP COLUMN IF EXISTS ""FacteurCorrectionApplique"";");
        }
    }
}
