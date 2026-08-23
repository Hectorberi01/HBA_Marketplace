using HBA.Merchants.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Merchants.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE BOUTIQUE POUR CHAQUE VENDEUR EXISTANT.
    ///
    /// MIGRATION SÉPARÉE DE CELLE QUI CRÉE LES TABLES, ET C'EST DÉLIBÉRÉ.
    ///
    /// La reprise a déjà été perdue deux fois dans ce dépôt : `dotnet ef migrations
    /// add` régénère le fichier et efface le SQL écrit à la main dedans. Une
    /// migration DISTINCTE, qui ne contient QUE la reprise, ne peut pas être
    /// écrasée par une régénération du schéma.
    ///
    /// L'IDENTIFIANT DE LA BOUTIQUE EST CELUI DU VENDEUR.
    ///
    /// C'est le cœur de cette reprise. `products.product_offers.StoreId` est
    /// peuplé depuis toujours avec l'identifiant du VENDEUR — le champ mentait,
    /// mais il mentait de façon cohérente. En donnant à la boutique de reprise ce
    /// même identifiant, TOUTES les offres existantes deviennent justes d'un coup,
    /// sans réécrire une seule ligne de la table des offres.
    ///
    /// La coïncidence ne vaut que pour la première boutique de chaque vendeur
    /// existant : les suivantes reçoivent un identifiant neuf. Rien dans le code
    /// ne doit jamais déduire l'un de l'autre.
    ///
    /// LA REPRISE PRÉSERVE L'ÉTAT DE VENTE, ELLE NE LE REMET PAS À ZÉRO.
    ///
    /// Première version : toutes les boutiques naissaient en « Draft ». C'était
    /// prudent et FAUX. Une boutique en Draft ne vend pas, et la création d'offre
    /// exige désormais une boutique ouverte : au déploiement, TOUS les vendeurs
    /// actifs se seraient retrouvés incapables de mettre quoi que ce soit en
    /// vente, sans message expliquant pourquoi.
    ///
    /// La boutique naît donc « Open » quand le vendeur vendait déjà — compte
    /// ACTIF, lieu d'expédition rattaché, téléphone connu — c'est-à-dire quand
    /// elle satisfait exactement les conditions que le domaine exige pour ouvrir.
    /// Sinon « Draft », et le vendeur la complète.
    ///
    /// Une reprise doit refléter ce qui EST, pas ce qu'on aurait voulu.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(SellersDbContext))]
    [Migration("20260813000000_RepriseStoresFromSellers")]
    public partial class RepriseStoresFromSellers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─────────────────────────────────────────────────────────────────
            // GARDE-FOU : ne rien faire si la reprise a déjà eu lieu.
            //
            // Sans elle, rejouer la migration violerait la clé primaire. Le test
            // porte sur l'existence d'une boutique dont l'identifiant est celui
            // d'un vendeur — la signature exacte de cette reprise.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    deja_repris integer;
                BEGIN
                    SELECT count(*) INTO deja_repris
                    FROM sellers.stores st
                    JOIN sellers.sellers se ON se."Id" = st."Id";

                    IF deja_repris > 0 THEN
                        RAISE NOTICE 'Reprise des boutiques déjà effectuée (% ligne(s)) — rien à faire.', deja_repris;
                        RETURN;
                    END IF;

                    -- Le TÉLÉPHONE, par ordre de fiabilité décroissante :
                    --
                    --   1. celui du LIEU D'EXPÉDITION du vendeur — obligatoire à la
                    --      saisie depuis la refonte des adresses, et c'est celui
                    --      qu'appelle un livreur devant la porte ;
                    --   2. à défaut, celui déclaré dans la fiche société (KYB) —
                    --      c'est le numéro du gérant, moins utile mais réel ;
                    --   3. à défaut, une chaîne vide.
                    --
                    -- LA CHAÎNE VIDE N'EST PAS UN REPLI COMMODE : elle rend la
                    -- boutique inouvrable telle quelle, ce qui est exactement le
                    -- comportement voulu. Le vendeur devra saisir un numéro. Écrire
                    -- un faux numéro plausible, lui, aurait envoyé un livreur nulle
                    -- part sans que personne ne s'en aperçoive.
                    -- EN MICROSERVICES, CETTE BRANCHE EST MORTE — ET LA GARDE
                    --    NOUS SAUVE.
                    --
                    -- Écrite pour le monolithe, où les vingt-neuf schémas
                    -- partageaient UNE base : `inventory.fulfillment_locations`
                    -- était atteignable, simplement pas toujours déjà migrée.
                    --
                    -- Depuis le découpage, merchant-service écrit dans
                    -- `hba_merchant` et inventory-service dans `hba_inventory`.
                    -- PostgreSQL n'interroge pas une autre base : `to_regclass`
                    -- rendra TOUJOURS NULL ici, et c'est la branche ELSE qui
                    -- s'exécute. Sans cette garde, écrite pour une autre raison,
                    -- la reprise échouerait à chaque installation.
                    --
                    -- Rattacher les lieux se fera par appel gRPC, pas par SQL.
                    --
                    -- PL/pgSQL ne prépare une requête qu'à sa première exécution :
                    -- la branche non prise n'est jamais analysée, donc jamais
                    -- rejetée pour une table absente.
                    IF to_regclass('inventory.fulfillment_locations') IS NOT NULL THEN

                        INSERT INTO sellers.stores (
                            "Id", "SellerId", "Name", "LogoUrl", "Description",
                            "Status", "StatusReason", "FulfillmentLocationId",
                            "ContactPhone", "ContactEmail",
                            "CreatedOnUtc", "UpdatedOnUtc")
                        SELECT
                            se."Id",
                            se."Id",
                            se."ShopName",
                            se."LogoUrl",
                            se."Description",
                            -- Ouverte SI et SEULEMENT SI elle remplit déjà les
                            -- conditions du domaine. Toute autre combinaison
                            -- resterait invendable, et mieux vaut le dire par un
                            -- « Draft » explicite que par une erreur à la
                            -- première offre.
                            CASE
                                WHEN se."Status" = 'Active'
                                     AND loc."Id" IS NOT NULL
                                     AND COALESCE(loc."address_contact_phone", '') <> ''
                                THEN 'Open'
                                ELSE 'Draft'
                            END,
                            NULL,
                            loc."Id",
                            COALESCE(
                                NULLIF(loc."address_contact_phone", ''),
                                NULLIF(se."metadata" ->> 'Phone', ''),
                                ''),
                            NULL,
                            se."CreatedOnUtc",
                            NULL
                        FROM sellers.sellers se
                        LEFT JOIN LATERAL (
                            -- Le lieu d'expédition du vendeur. LEFT JOIN : un vendeur
                            -- qui n'en a pas encore reçoit quand même sa boutique, en
                            -- Draft, qu'il complétera. La perdre serait pire.
                            SELECT fl."Id", fl."address_contact_phone"
                            FROM inventory.fulfillment_locations fl
                            WHERE fl."OwnerId" = se."Id"
                            ORDER BY fl."CreatedOnUtc"
                            LIMIT 1
                        ) loc ON TRUE;

                    ELSE

                        -- Sans Inventory, on reprend quand même : perdre la boutique
                        -- d'un vendeur existant serait bien pire que la créer sans
                        -- lieu rattaché. Elle reste en Draft, donc invendable, et le
                        -- vendeur la complétera.
                        INSERT INTO sellers.stores (
                            "Id", "SellerId", "Name", "LogoUrl", "Description",
                            "Status", "StatusReason", "FulfillmentLocationId",
                            "ContactPhone", "ContactEmail",
                            "CreatedOnUtc", "UpdatedOnUtc")
                        SELECT
                            se."Id",
                            se."Id",
                            se."ShopName",
                            se."LogoUrl",
                            se."Description",
                            'Draft',
                            NULL,
                            NULL,
                            COALESCE(NULLIF(se."metadata" ->> 'Phone', ''), ''),
                            NULL,
                            se."CreatedOnUtc",
                            NULL
                        FROM sellers.sellers se;

                    END IF;

                    RAISE NOTICE 'Reprise des boutiques terminée.';
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // On ne retire QUE les boutiques de reprise — celles dont
            // l'identifiant est celui d'un vendeur. Une boutique créée depuis par
            // un marchand n'a rien à voir avec cette migration, et l'effacer
            // supprimerait son travail.
            migrationBuilder.Sql("""
                DELETE FROM sellers.stores st
                USING sellers.sellers se
                WHERE st."Id" = se."Id";
                """);
        }
    }
}
