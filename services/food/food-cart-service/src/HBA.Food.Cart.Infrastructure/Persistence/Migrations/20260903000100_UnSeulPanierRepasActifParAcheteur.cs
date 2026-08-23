using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.FoodCarts.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN SEUL PANIER REPAS ACTIF PAR ACHETEUR — ET LA FUSION DES DOUBLONS (§5).
    ///
    /// Même défaut que côté marketplace, même remède.
    /// `FoodCartRepository.GetActiveByBuyerAsync` fait un `FirstOrDefault` sur
    /// <c>BuyerId == x AND Status == Active</c>, SANS TRI : avec deux paniers actifs,
    /// l'acheteur en voit un au hasard.
    ///
    /// LE RESTAURANT S'AJOUTE AUX CONDITIONS DE FUSION, ET C'EST LA DIFFÉRENCE.
    ///
    /// Un panier repas est lié à UN restaurant : `RestaurantId` est une colonne du
    /// panier, pas de la ligne. Deux paniers actifs visant des restaurants
    /// différents ne peuvent pas fusionner — le résultat serait une commande
    /// adressée à deux cuisines. Ils sont seulement abandonnés, lignes conservées.
    ///
    /// ET C'EST LE CAS LE PLUS PROBABLE ICI, contrairement au marketplace : un
    /// client qui hésite entre deux restaurants produit exactement cette situation.
    /// La règle « un seul panier repas actif » est celle du code ; la migration ne
    /// l'invente pas, elle la fait tenir.
    ///
    /// AUCUN INDEX UNIQUE SUR LES LIGNES ICI, contrairement au marketplace : la
    /// fusion peut donc déplacer les lignes sans précaution. Le revers est que deux
    /// lignes du même plat peuvent cohabiter — leur unicité tient à la combinaison
    /// plat + options, vérifiée en mémoire, qu'aucune colonne ne porte. L'acheteur
    /// les voit et peut en retirer une.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.FoodCarts.Infrastructure.Persistence.FoodCartDbContext))]
    [Migration("20260903000100_UnSeulPanierRepasActifParAcheteur")]
    public partial class UnSeulPanierRepasActifParAcheteur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SURVIVANT = LE PLUS FOURNI, départagé par `Id`. `food_carts` n'a pas
            // davantage d'horodatage que `carts` : rien ne dit lequel est le plus
            // récent (lot 8.7).
            //
            // Fusionnable si MÊME restaurant ET MÊME devise. Un panier vide se
            // fusionne trivialement — il n'a rien à apporter.
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE fusion_paniers_repas ON COMMIT DROP AS
                WITH actifs AS (
                    SELECT c.""Id""           AS cart_id,
                           c.""BuyerId""      AS buyer_id,
                           c.""RestaurantId"" AS restaurant_id,
                           c.""Currency""     AS devise,
                           (SELECT count(*) FROM food_cart.food_cart_items i
                             WHERE i.""FoodCartId"" = c.""Id"") AS lignes
                    FROM food_cart.food_carts c
                    WHERE c.""Status"" = 'Active'
                ),
                survivants AS (
                    SELECT DISTINCT ON (buyer_id)
                           buyer_id, cart_id, restaurant_id, devise
                    FROM actifs
                    ORDER BY buyer_id, lignes DESC, cart_id
                )
                SELECT a.cart_id AS absorbe,
                       s.cart_id AS survivant,
                       (a.restaurant_id = s.restaurant_id AND a.devise = s.devise) AS fusionnable
                FROM actifs a
                JOIN survivants s ON s.buyer_id = a.buyer_id
                WHERE a.cart_id <> s.cart_id;
            ");

            // Déplacement direct : aucun index unique ne s'y oppose. Les options
            // suivent, leur clé étrangère pointant sur la LIGNE et non sur le panier.
            migrationBuilder.Sql(@"
                UPDATE food_cart.food_cart_items i
                SET ""FoodCartId"" = f.survivant
                FROM fusion_paniers_repas f
                WHERE i.""FoodCartId"" = f.absorbe AND f.fusionnable;
            ");

            // `Abandoned`, PAS `DELETE` — y compris pour les paniers d'un autre
            // restaurant, qui gardent leurs lignes et restent lisibles.
            migrationBuilder.Sql(@"
                UPDATE food_cart.food_carts
                SET ""Status"" = 'Abandoned'
                WHERE ""Id"" IN (SELECT absorbe FROM fusion_paniers_repas);
            ");

            migrationBuilder.CreateIndex(
                name: "ux_food_carts_active_buyer",
                schema: "food_cart",
                table: "food_carts",
                column: "BuyerId",
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // La fusion n'est pas réversible : les lignes ont changé de panier et
            // aucune trace du lien d'origine n'a été conservée. `Down` retire la
            // contrainte, il ne défait pas la fusion.
            migrationBuilder.DropIndex(
                name: "ux_food_carts_active_buyer",
                schema: "food_cart",
                table: "food_carts");
        }
    }
}
