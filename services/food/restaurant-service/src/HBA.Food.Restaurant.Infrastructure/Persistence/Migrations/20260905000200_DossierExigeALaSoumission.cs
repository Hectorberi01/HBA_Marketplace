using HBA.Food.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Food.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN ÉTABLISSEMENT SOUMIS À VALIDATION PORTE SON DOSSIER DE REVERSEMENT (§9.1).
    ///
    /// L'AUDIT DEMANDAIT UNE CONTRAINTE SUR « Submitted ». CET ÉTAT N'EXISTE PAS.
    ///
    /// RestaurantStatus vaut Draft / PendingApproval / Active / Suspended / Closed.
    /// Submit() est le geste, PendingApproval l'état qui en résulte — et c'est
    /// Submit() qui refuse un établissement sans PayoutSellerId, faute de quoi le
    /// restaurateur est mis en service sans qu'aucun chemin ne permette de le payer.
    /// Écrite telle que demandée, la contrainte aurait été TOUJOURS VRAIE : le pire
    /// des contrôles, celui qui rassure sans rien vérifier.
    ///
    /// « Active » EN EST EXCLU, ET C'EST DÉLIBÉRÉ.
    ///
    /// La migration 20260820000000_DossierDeReversementDuRestaurant a créé la
    /// colonne nullable en assumant que les établissements DÉJÀ EN SERVICE
    /// continuent de fonctionner sans dossier — Submit n'est pas rejoué sur eux.
    /// Étendre la contrainte à Active contredirait cette décision et mettrait hors
    /// la loi des lignes qu'on a délibérément laissées ainsi.
    ///
    /// AUCUNE COLONNE UpdatedAtUtc ICI : restaurants porte déjà UpdatedOnUtc,
    /// rempli par Restaurant.Touch(). Deux colonnes de même sens sur la même table
    /// sont un piège de lecture — on finit par interroger celle qui ne bouge pas.
    ///
    /// CETTE CONTRAINTE PEUT FAIRE ÉCHOUER LA MIGRATION. Repérage :
    ///
    ///     SELECT "Id", "Name" FROM food.restaurants
    ///     WHERE "Status" = 'PendingApproval' AND "PayoutSellerId" IS NULL;
    ///
    /// Une ligne qui remonte est un dossier en attente de validation que personne
    /// ne pourra payer : elle se corrige en rattachant le vendeur, ou en renvoyant
    /// l'établissement en Draft. Pas en assouplissant la contrainte.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(FoodDbContext))]
    [Migration("20260905000200_DossierExigeALaSoumission")]
    public partial class DossierExigeALaSoumission : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_restaurants_pending_requires_payout",
                schema: "food",
                table: "restaurants",
                sql: "\"Status\" <> 'PendingApproval' OR \"PayoutSellerId\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CE RETOUR EN ARRIÈRE REND À NOUVEAU POSSIBLE UN ÉTABLISSEMENT EN
            // ATTENTE DE VALIDATION SANS DOSSIER DE REVERSEMENT — c'est-à-dire un
            // restaurateur qu'on met en service et qu'on ne peut pas payer. Rien
            // n'est perdu ici : la garde de `Submit()` tient toujours en mémoire.
            // C'est le filet de la base qui disparaît, pas la règle.
            migrationBuilder.DropCheckConstraint(
                name: "ck_restaurants_pending_requires_payout",
                schema: "food",
                table: "restaurants");
        }
    }
}
