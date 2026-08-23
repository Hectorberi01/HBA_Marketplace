using HBA.Food.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Food.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN FONDATEUR POUR CHAQUE ÉTABLISSEMENT DÉJÀ ENREGISTRÉ.
    ///
    /// CETTE MIGRATION N'EST PAS COSMÉTIQUE : SANS ELLE, LES RESTAURANTS
    /// EXISTANTS DEVIENNENT INACCESSIBLES À LEUR PROPRE PROPRIÉTAIRE.
    ///
    /// Les routes de l'espace restaurateur autorisaient jusqu'ici en comparant le
    /// porteur du jeton à `Restaurant.OwnerUserId`. Depuis l'arrivée du personnel
    /// (§8), elles autorisent sur l'APPARTENANCE — sans quoi aucun manager,
    /// caissier ni cuisinier n'aurait jamais accès à l'application.
    ///
    /// Un établissement sans ligne de personnel n'a donc plus personne pour y
    /// entrer. Il continue d'exister, de s'afficher, d'être commandable, et son
    /// propriétaire ne peut plus toucher à sa carte.
    ///
    /// ÉCRITE À LA MAIN ET SÉPARÉE DE LA MIGRATION DE SCHÉMA, DÉLIBÉRÉMENT.
    ///
    /// C'est le même découpage que `RepriseStoresFromSellers` : le schéma est
    /// généré par `dotnet ef` et régénérable, la reprise de données ne l'est pas.
    /// Les fondre exposerait ce bloc à disparaître au prochain scaffold.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(FoodDbContext))]
    [Migration("20260812235900_SeedRestaurantFounders")]
    public partial class SeedRestaurantFounders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `Role = 0` est Owner — la valeur EST la hiérarchie, voir StaffRole.
            // `IsFounder = TRUE` rend la ligne intouchable : ni rétrogradable, ni
            // désactivable, même par un autre propriétaire. C'est la clé de
            // dernier recours d'un établissement.
            //
            // `NOT EXISTS` rend la migration rejouable sans doublon — que l'index
            // unique refuserait de toute façon, mais avec une erreur de contrainte
            // au lieu d'un silence.
            migrationBuilder.Sql("""
                INSERT INTO food.restaurant_staff
                    ("Id", "RestaurantId", "UserId", "Role", "IsFounder", "IsActive", "CreatedOnUtc")
                SELECT
                    gen_random_uuid(),
                    r."Id",
                    r."OwnerUserId",
                    0,
                    TRUE,
                    TRUE,
                    COALESCE(r."CreatedOnUtc", NOW() AT TIME ZONE 'UTC')
                FROM food.restaurants r
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM food.restaurant_staff s
                    WHERE s."RestaurantId" = r."Id"
                      AND s."UserId" = r."OwnerUserId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ON NE SUPPRIME QUE LES LIGNES QUE CETTE MIGRATION A POSÉES.
            //
            // Un `DELETE FROM restaurant_staff` emporterait le personnel embauché
            // depuis — des managers, des caissiers, des cuisiniers créés en
            // production. On se limite aux fondateurs, et uniquement à ceux qui
            // correspondent encore au propriétaire déclaré du restaurant.
            migrationBuilder.Sql("""
                DELETE FROM food.restaurant_staff s
                USING food.restaurants r
                WHERE s."RestaurantId" = r."Id"
                  AND s."UserId" = r."OwnerUserId"
                  AND s."IsFounder" = TRUE;
                """);
        }
    }
}
