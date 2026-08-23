using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Food.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE TICKET DE CUISINE DIT ENFIN DE QUEL UNIVERS VIENT SA COMMANDE.
    ///
    /// `food_orders."OrderId"` PORTAIT DEUX CHOSES DIFFÉRENTES.
    ///
    /// Deux ponts ouvrent un ticket : `OrderConfirmed` (une commande
    /// order-service dont une ligne est un plat) et `MealOrderConfirmed` (une
    /// `MealOrder` de food-order-service). Les deux écrivaient dans la même
    /// colonne, sans discriminant. Six gestionnaires inter-services lisaient
    /// cette colonne NUE et interrogeaient leur propre base avec.
    ///
    /// Le plus visible : `CreateDeliveryOnFoodOrderReadyHandler` demandait
    /// l'adresse de livraison à order-service. Pour un ticket né d'une
    /// `MealOrder`, la commande était introuvable, le gestionnaire levait, les
    /// reprises s'épuisaient — et AUCUNE COURSE N'ÉTAIT CRÉÉE. Le repas était
    /// prêt et personne ne cherchait de livreur.
    ///
    /// DÉFAUT `0` (= Marketplace), ET C'EST EXACT POUR TOUT L'EXISTANT.
    ///
    /// Le second pont est récent et son chemin de paiement n'a jamais abouti
    /// avant le lot 6.1 : aucune commande de repas n'a pu être confirmée, donc
    /// aucun ticket en base ne peut venir de là. Le défaut décrit donc les
    /// lignes existantes correctement, sans reprise de données.
    ///
    /// L'INDEX UNIQUE CHANGE DE CLÉ, ET L'ORDRE DES DEUX OPÉRATIONS COMPTE.
    ///
    /// `ux_food_orders_order` portait `"OrderId"` seul. Il devient
    /// `("Origin", "OrderId")`. On CRÉE la colonne avant de toucher à l'index :
    /// l'inverse laisserait un instant sans contrainte d'unicité, et cette
    /// migration s'applique au démarrage, donc sur une base qui peut recevoir des
    /// écritures.
    ///
    /// `IF NOT EXISTS` / `IF EXISTS` PARTOUT — même prudence que les migrations
    /// voisines : une base déjà alignée à la main ne doit pas empêcher un service
    /// de démarrer.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Food.Infrastructure.Persistence.FoodDbContext))]
    [Migration("20260827000000_OrigineDuTicketDeCuisine")]
    public partial class OrigineDuTicketDeCuisine : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"ALTER TABLE food.food_orders ADD COLUMN IF NOT EXISTS ""Origin"" integer NOT NULL DEFAULT 0;");

            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS food.ux_food_orders_order;");

            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ux_food_orders_order
                      ON food.food_orders (""Origin"", ""OrderId"");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // LE RETOUR EN ARRIÈRE PEUT ÉCHOUER, ET C'EST CORRECT.
            //
            // Si des tickets des deux univers cohabitent, deux lignes peuvent
            // partager un même `OrderId` : recréer l'index sur cette seule colonne
            // est alors IMPOSSIBLE, et PostgreSQL le dira. Forcer le retour en
            // supprimant l'unicité serait pire — on rendrait silencieusement
            // possible le double ticket que cet index existe pour interdire.
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS food.ux_food_orders_order;");

            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ux_food_orders_order
                      ON food.food_orders (""OrderId"");");

            migrationBuilder.Sql(
                @"ALTER TABLE food.food_orders DROP COLUMN IF EXISTS ""Origin"";");
        }
    }
}
