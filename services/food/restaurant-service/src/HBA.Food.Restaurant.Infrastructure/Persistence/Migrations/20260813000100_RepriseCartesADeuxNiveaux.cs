using HBA.Food.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Food.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA BASCULE À DEUX NIVEAUX (cahier des charges §5).
    ///
    /// Avant : <c>menus</c> contenait les SECTIONS — Entrées, Plats, Boissons —
    /// et <c>menu_items.MenuId</c> pointait dessus.
    ///
    /// Après : <c>menu_categories</c> contient les sections, <c>menus</c> contient
    /// les CARTES porteuses de créneaux, et <c>menu_items.MenuCategoryId</c>
    /// pointe sur les sections.
    ///
    /// CE FICHIER NE CRÉE AUCUNE TABLE ET N'EN SUPPRIME AUCUNE.
    ///
    /// Le schéma est généré par `dotnet ef`, qui met à jour le snapshot. Cette
    /// migration-ci ne fait que DÉPLACER LES DONNÉES, et c'est précisément ce
    /// qu'un scaffold ne saura jamais deviner : EF verra une table renommée comme
    /// une table détruite et une autre créée, et les sections existantes
    /// partiraient avec.
    ///
    /// Même découpage que `RepriseStoresFromSellers` et `SeedRestaurantFounders`.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE CORRECTION À FAIRE À LA MAIN DANS LA MIGRATION GÉNÉRÉE 
    ///
    /// `menu_items.MenuId` devient `menu_items.MenuCategoryId`. EF Core NE SAIT
    /// PAS reconnaître un renommage de colonne : il produira un
    /// `DropColumn("MenuId")` suivi d'un `AddColumn("MenuCategoryId")`.
    ///
    /// Appliqué tel quel, CHAQUE ARTICLE PERD SON RATTACHEMENT. Les plats
    /// existent encore, plus aucun n'appartient à une section, et la projection
    /// — qui parcourt cartes puis sections puis articles — n'en affichera plus un
    /// seul. Ni au client, ni au restaurateur.
    ///
    /// Dans la migration générée, remplacer les deux appels par :
    ///
    ///     migrationBuilder.RenameColumn(
    ///         name: "MenuId",
    ///         schema: "food",
    ///         table: "menu_items",
    ///         newName: "MenuCategoryId");
    ///
    /// C'est ce renommage qui rend l'étape 1 ci-dessous suffisante : les sections
    /// GARDENT leur identifiant, donc `MenuCategoryId` pointe déjà au bon endroit
    /// et aucun article n'a besoin d'être réécrit.
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// POURQUOI MAINTENANT
    ///
    /// Aucune commande ne référence encore un article : la reprise ne déplace que
    /// des lignes de carte. Une fois le panier et les commandes branchés, la même
    /// bascule aurait demandé de démêler des références historiques — et le §20 du
    /// cahier interdit de modifier rétroactivement une commande déjà passée.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(FoodDbContext))]
    [Migration("20260813000100_RepriseCartesADeuxNiveaux")]
    public partial class RepriseCartesADeuxNiveaux : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ═════════════════════════════════════════════════════════════════
            // 1. Les anciennes lignes de `menus` DEVIENNENT des sections.
            //
            // ON CONSERVE LEUR IDENTIFIANT. C'est ce qui rend l'étape 3
            // triviale : `menu_items.MenuCategoryId` vaut déjà l'ancien
            // `MenuId`, et aucun article n'a besoin d'être réécrit.
            //
            // Le rattachement à une carte se fait à l'étape suivante ; en
            // attendant, `MenuId` reçoit un GUID nul, remplacé juste après.
            // ═════════════════════════════════════════════════════════════════
            migrationBuilder.Sql("""
                INSERT INTO food.menu_categories
                    ("Id", "RestaurantId", "MenuId", "Name", "Description",
                     "DisplayOrder", "IsActive", "CreatedOnUtc", "UpdatedOnUtc")
                SELECT
                    m."Id",
                    m."RestaurantId",
                    '00000000-0000-0000-0000-000000000000'::uuid,
                    m."Name",
                    m."Description",
                    m."DisplayOrder",
                    m."IsActive",
                    m."CreatedOnUtc",
                    m."UpdatedOnUtc"
                FROM food.menus m
                WHERE NOT EXISTS (
                    SELECT 1 FROM food.menu_categories c WHERE c."Id" = m."Id");
                """);

            // ═════════════════════════════════════════════════════════════════
            // 2. UNE CARTE PAR RESTAURANT, servie en permanence.
            //
            // Les quatre colonnes de créneau restent NULLES : c'est la carte
            // permanente, exactement le comportement d'avant la bascule. Rien ne
            // change pour un restaurateur qui n'a jamais demandé de menu du midi
            // — et c'est la seule reprise honnête, puisque personne n'a saisi
            // d'horaires de carte.
            //
            // Les anciennes lignes de `menus` sont supprimées À LA FIN, pas
            // ici : elles servent encore de source à cette insertion.
            // ═════════════════════════════════════════════════════════════════
            migrationBuilder.Sql("""
                INSERT INTO food.menus
                    ("Id", "RestaurantId", "Name", "Description",
                     "DisplayOrder", "IsActive", "CreatedOnUtc",
                     "AvailableFrom", "AvailableUntil", "StartTime", "EndTime")
                SELECT
                    gen_random_uuid(),
                    r."Id",
                    'Carte',
                    NULL,
                    0,
                    TRUE,
                    NOW() AT TIME ZONE 'UTC',
                    NULL, NULL, NULL, NULL
                FROM food.restaurants r
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM food.menus m
                    WHERE m."RestaurantId" = r."Id"
                      AND NOT EXISTS (SELECT 1 FROM food.menu_categories c WHERE c."Id" = m."Id"));
                """);

            // 3. Rattacher chaque section à la carte de SON restaurant.
            migrationBuilder.Sql("""
                UPDATE food.menu_categories c
                SET "MenuId" = m."Id"
                FROM food.menus m
                WHERE m."RestaurantId" = c."RestaurantId"
                  AND c."MenuId" = '00000000-0000-0000-0000-000000000000'::uuid
                  AND NOT EXISTS (SELECT 1 FROM food.menu_categories x WHERE x."Id" = m."Id");
                """);

            // ═════════════════════════════════════════════════════════════════
            // 4. Les anciennes lignes de `menus` — devenues des sections —
            //    disparaissent de la table des cartes.
            //
            // ON NE SUPPRIME QUE CELLES QUI ONT UN JUMEAU DANS
            // `menu_categories`. Un `DELETE` non filtré emporterait les cartes
            // créées à l'étape 2, et l'on se retrouverait avec des sections
            // orphelines — invisibles jusque dans l'écran du restaurateur, comme
            // le décrit la garde de suppression.
            // ═════════════════════════════════════════════════════════════════
            migrationBuilder.Sql("""
                DELETE FROM food.menus m
                WHERE EXISTS (SELECT 1 FROM food.menu_categories c WHERE c."Id" = m."Id");
                """);

            // 5. Filet : une section qui n'aurait trouvé aucune carte signalerait
            //    une reprise incomplète. On échoue BRUYAMMENT plutôt que de livrer
            //    des sections que personne ne reverra jamais.
            migrationBuilder.Sql("""
                DO $$
                DECLARE orphelines INT;
                BEGIN
                    SELECT COUNT(*) INTO orphelines
                    FROM food.menu_categories
                    WHERE "MenuId" = '00000000-0000-0000-0000-000000000000'::uuid;

                    IF orphelines > 0 THEN
                        RAISE EXCEPTION
                            'Reprise incomplète : % section(s) sans carte. Aucune ne serait plus jamais visible.',
                            orphelines;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Le chemin inverse : les sections redeviennent des lignes de `menus`,
            // et les cartes créées par la reprise disparaissent.
            //
            // IRRÉVERSIBLE POUR LES CRÉNEAUX. Une carte du midi saisie après la
            // bascule n'a nulle part où retourner dans le modèle à un seul niveau :
            // ses horaires sont perdus, et ses sections rejoignent le tas commun.
            // C'est dit ici plutôt que découvert après coup.
            migrationBuilder.Sql("""
                DELETE FROM food.menus;

                INSERT INTO food.menus
                    ("Id", "RestaurantId", "Name", "Description",
                     "DisplayOrder", "IsActive", "CreatedOnUtc", "UpdatedOnUtc",
                     "AvailableFrom", "AvailableUntil", "StartTime", "EndTime")
                SELECT
                    c."Id", c."RestaurantId", c."Name", c."Description",
                    c."DisplayOrder", c."IsActive", c."CreatedOnUtc", c."UpdatedOnUtc",
                    NULL, NULL, NULL, NULL
                FROM food.menu_categories c;

                DELETE FROM food.menu_categories;
                """);
        }
    }
}
