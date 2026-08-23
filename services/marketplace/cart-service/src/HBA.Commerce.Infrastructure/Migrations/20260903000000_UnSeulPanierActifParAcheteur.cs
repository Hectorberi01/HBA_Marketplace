using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Commerce.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN SEUL PANIER ACTIF PAR ACHETEUR — ET LA FUSION DES DOUBLONS (§5).
    ///
    /// LE CODE SUPPOSAIT DÉJÀ CETTE RÈGLE, LA BASE NE LA TENAIT PAS.
    ///
    /// `CartRepository.GetActiveCartAsync` fait un `FirstOrDefault` sur
    /// <c>BuyerId == x AND Status == Active</c>, SANS TRI. Avec deux paniers actifs,
    /// l'acheteur en voit donc un AU HASARD : ses articles apparaissent et
    /// disparaissent d'une requête à l'autre, sans que rien ne l'explique. Et la
    /// création est un « récupérer-ou-créer » non atomique — deux ajouts simultanés
    /// produisent deux paniers.
    ///
    /// LES DOUBLONS EXISTANTS SONT FUSIONNÉS, PAS ÉCARTÉS. Décision de HECTOR.
    ///
    /// L'alternative — garder le plus fourni et abandonner les autres — était plus
    /// simple, et perdait de vue les articles des paniers écartés. La fusion ne perd
    /// rien : les lignes rejoignent le panier survivant, et les quantités se cumulent
    /// quand la même offre figure des deux côtés.
    ///
    /// TROIS CAS NE SONT PAS FUSIONNABLES, ET SONT SEULEMENT ABANDONNÉS :
    ///
    ///   • DEVISES DIFFÉRENTES — `VerifierAjout` refuse une ligne dont la devise
    ///     n'est pas celle du panier. Fusionner produirait un panier que le domaine
    ///     lui-même rejetterait à la première modification ;
    ///   • NATURES DIFFÉRENTES (`Goods` d'un côté, `Food` de l'autre) — le panier ne
    ///     peut pas être mixte, c'est un invariant du domaine ;
    ///   • et par construction, un panier vide n'a rien à apporter.
    ///
    /// Dans ces cas, RIEN N'EST SUPPRIMÉ non plus : le panier passe en `Abandoned` et
    /// ses lignes restent en base, récupérables par le support.
    ///
    /// CE QUE LA FUSION NE RÉSOUT PAS : les lignes FOOD en double. Leur unicité
    /// tient à la combinaison plat + options, vérifiée en mémoire par
    /// `CartItem.MatchesFood` — aucune colonne ne la porte, donc aucun SQL ne peut la
    /// reconstituer ici. Deux paniers fusionnés peuvent laisser deux lignes pour le
    /// même plat. L'acheteur les voit toutes deux et peut en retirer une ; c'est
    /// visible et réparable, contrairement à ce qui précédait.
    ///
    /// CETTE MIGRATION ÉCRIT DES LIGNES MÉTIER. C'est la première du chantier
    /// dans ce cas. Elle est écrite en SQL ENSEMBLISTE — pas de boucle — pour que
    /// chaque étape soit relisible et que l'ensemble tienne dans la transaction de
    /// la migration : si une étape échoue, aucune n'a eu lieu.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Commerce.Infrastructure.Persistence.CartDbContext))]
    [Migration("20260903000000_UnSeulPanierActifParAcheteur")]
    public partial class UnSeulPanierActifParAcheteur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─────────────────────────────────────────────────────────────────
            // 1 · DÉSIGNER LE SURVIVANT ET LES ABSORBÉS.
            //
            // Le survivant est le panier actif qui porte LE PLUS DE LIGNES —
            // départagé par `Id` pour que le choix soit reproductible.
            //
            // POURQUOI PAS « LE PLUS RÉCENT » : `carts` n'a AUCUNE colonne
            // d'horodatage. Rien en base ne dit lequel a été créé en premier. C'est
            // un manque à part entière (lot 8.7) ; ici, il faut faire sans.
            //
            // `nature_min = nature_max` dit qu'un panier est homogène — le domaine
            // le garantit, on ne le suppose pas.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE fusion_paniers ON COMMIT DROP AS
                WITH actifs AS (
                    SELECT c.""Id""          AS cart_id,
                           c.""BuyerId""     AS buyer_id,
                           c.""Currency""    AS devise,
                           (SELECT count(*)      FROM cart.cart_items i WHERE i.""CartId"" = c.""Id"") AS lignes,
                           (SELECT min(i.""Kind"") FROM cart.cart_items i WHERE i.""CartId"" = c.""Id"") AS nature_min,
                           (SELECT max(i.""Kind"") FROM cart.cart_items i WHERE i.""CartId"" = c.""Id"") AS nature_max
                    FROM cart.carts c
                    WHERE c.""Status"" = 'Active'
                ),
                survivants AS (
                    SELECT DISTINCT ON (buyer_id)
                           buyer_id, cart_id, devise, nature_min, nature_max
                    FROM actifs
                    ORDER BY buyer_id, lignes DESC, cart_id
                )
                SELECT a.cart_id AS absorbe,
                       s.cart_id AS survivant,
                       (
                           a.devise = s.devise
                           AND (
                                a.nature_min IS NULL
                                OR s.nature_min IS NULL
                                OR (a.nature_min = a.nature_max
                                    AND s.nature_min = s.nature_max
                                    AND a.nature_min = s.nature_min)
                           )
                       ) AS fusionnable
                FROM actifs a
                JOIN survivants s ON s.buyer_id = a.buyer_id
                WHERE a.cart_id <> s.cart_id;
            ");

            // ─────────────────────────────────────────────────────────────────
            // 2 · CUMULER LES QUANTITÉS DES OFFRES QUE LE SURVIVANT PORTE DÉJÀ.
            //
            // L'AGRÉGAT `sum()` EST INDISPENSABLE, PAS UNE COQUETTERIE. Un
            // acheteur peut avoir TROIS paniers actifs. Un `UPDATE … FROM` qui
            // joindrait directement les lignes absorbées n'appliquerait qu'UNE
            // source par ligne cible — les quantités des autres seraient perdues
            // en silence, et l'étape 3 les supprimerait quand même.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                UPDATE cart.cart_items s
                SET ""Quantity"" = s.""Quantity"" + t.total
                FROM (
                    SELECT f.survivant, a.""OfferId"" AS offre, sum(a.""Quantity"") AS total
                    FROM cart.cart_items a
                    JOIN fusion_paniers f ON f.absorbe = a.""CartId"" AND f.fusionnable
                    WHERE a.""Kind"" = 'Goods'
                    GROUP BY f.survivant, a.""OfferId""
                ) t
                WHERE s.""CartId"" = t.survivant
                  AND s.""Kind"" = 'Goods'
                  AND s.""OfferId"" = t.offre;
            ");

            // ─────────────────────────────────────────────────────────────────
            // 3 · SUPPRIMER LES LIGNES ABSORBÉES DONT LA QUANTITÉ VIENT D'ÊTRE
            //     REPORTÉE. Elles n'ont plus rien à apporter, et les déplacer
            //     violerait `ux (CartId, OfferId) WHERE Kind = 'Goods'`.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                DELETE FROM cart.cart_items a
                USING fusion_paniers f, cart.cart_items s
                WHERE a.""CartId"" = f.absorbe
                  AND f.fusionnable
                  AND s.""CartId"" = f.survivant
                  AND s.""Kind"" = 'Goods'
                  AND a.""Kind"" = 'Goods'
                  AND s.""OfferId"" = a.""OfferId"";
            ");

            // ─────────────────────────────────────────────────────────────────
            // 4 · COLLAPSER LES DOUBLONS ENTRE PANIERS ABSORBÉS.
            //
            // SANS CETTE ÉTAPE, L'ÉTAPE 5 VIOLERAIT L'INDEX UNIQUE. Deux paniers
            // absorbés peuvent porter la MÊME offre que le survivant n'a pas : les
            // déplacer tous deux poserait deux lignes `(survivant, offre)`.
            //
            // On garde la ligne d'`Id` le plus petit, on lui donne le total, et on
            // supprime les autres.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                WITH candidates AS (
                    SELECT a.""Id"" AS ligne_id,
                           f.survivant,
                           a.""OfferId"" AS offre,
                           a.""Quantity"" AS quantite,
                           row_number() OVER (PARTITION BY f.survivant, a.""OfferId"" ORDER BY a.""Id"") AS rang,
                           sum(a.""Quantity"") OVER (PARTITION BY f.survivant, a.""OfferId"") AS total
                    FROM cart.cart_items a
                    JOIN fusion_paniers f ON f.absorbe = a.""CartId"" AND f.fusionnable
                    WHERE a.""Kind"" = 'Goods'
                )
                UPDATE cart.cart_items i
                SET ""Quantity"" = c.total
                FROM candidates c
                WHERE i.""Id"" = c.ligne_id AND c.rang = 1 AND c.total <> c.quantite;
            ");

            migrationBuilder.Sql(@"
                WITH candidates AS (
                    SELECT a.""Id"" AS ligne_id,
                           row_number() OVER (PARTITION BY f.survivant, a.""OfferId"" ORDER BY a.""Id"") AS rang
                    FROM cart.cart_items a
                    JOIN fusion_paniers f ON f.absorbe = a.""CartId"" AND f.fusionnable
                    WHERE a.""Kind"" = 'Goods'
                )
                DELETE FROM cart.cart_items i
                USING candidates c
                WHERE i.""Id"" = c.ligne_id AND c.rang > 1;
            ");

            // ─────────────────────────────────────────────────────────────────
            // 5 · DÉPLACER CE QUI RESTE VERS LE SURVIVANT.
            //
            // Les lignes FOOD passent ici sans traitement particulier : elles
            // portent toutes `OfferId = Guid.Empty` et l'index unique ne les couvre
            // pas. Leurs options suivent, la clé étrangère pointant sur la LIGNE et
            // non sur le panier.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                UPDATE cart.cart_items a
                SET ""CartId"" = f.survivant
                FROM fusion_paniers f
                WHERE a.""CartId"" = f.absorbe AND f.fusionnable;
            ");

            // ─────────────────────────────────────────────────────────────────
            // 6 · ABANDONNER TOUS LES ABSORBÉS — fusionnés ou non.
            //
            // `Abandoned`, PAS `DELETE`. Un panier non fusionnable garde ses
            // lignes : elles restent lisibles par le support, et l'acheteur n'a rien
            // perdu de définitif.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                UPDATE cart.carts
                SET ""Status"" = 'Abandoned'
                WHERE ""Id"" IN (SELECT absorbe FROM fusion_paniers);
            ");

            // ─────────────────────────────────────────────────────────────────
            // 7 · LA CONTRAINTE.
            //
            // INDEX PARTIEL. Un acheteur a un seul panier ACTIF, mais autant de
            // paniers `CheckedOut` et `Abandoned` que d'achats passés — c'est
            // l'historique. Un index unique sans filtre les refuserait tous.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "ux_carts_active_buyer",
                schema: "cart",
                table: "carts",
                column: "BuyerId",
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // LA FUSION N'EST PAS RÉVERSIBLE, ET IL FAUT LE DIRE. Les lignes ont
            // changé de panier, des quantités ont été cumulées, des lignes ont
            // disparu. `Down` retire la contrainte — il ne défait pas la fusion, et
            // aucun SQL ne le pourrait sans une trace qu'on n'a pas conservée.
            migrationBuilder.DropIndex(
                name: "ux_carts_active_buyer",
                schema: "cart",
                table: "carts");
        }
    }
}
