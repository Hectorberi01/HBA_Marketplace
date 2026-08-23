using System;
using HBA.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Inventory.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE STATUT DES RÉSERVATIONS, ET L'UNICITÉ DE CELLES QUI SONT EN COURS.
    ///
    /// Trois anomalies, une seule table : ISSUE-045 (aucun statut), ISSUE-075
    /// (réservation non idempotente) et ISSUE-031 (échéance jamais relue).
    ///
    /// CE QUI ÉTAIT CASSÉ.
    ///
    /// `stock_reservations` ne portait que `OrderId`, `Quantity` et `ExpiresAtUtc`.
    /// Libérer ou confirmer SUPPRIMAIT la ligne, si bien qu'une vente confirmée ne
    /// se distinguait plus d'une réservation inexistante — et que rien n'empêchait
    /// de « libérer » du stock déjà vendu et déjà décrémenté. Rien, non plus,
    /// n'empêchait deux réservations de la MÊME commande sur le MÊME article :
    /// l'appelant est derrière une échéance de 5 s, un rejeu immobilisait le stock
    /// deux fois pour une seule vente.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES LIGNES EXISTANTES DEVIENNENT `Active`, ET C'EST LA SEULE VALEUR
    /// JUSTE.
    ///
    /// Toutes les réservations présentes en base sont, par construction, EN
    /// COURS : les libérées et les confirmées avaient été effacées par
    /// `RemoveAll`. `defaultValue: "Active"` n'invente donc rien — il nomme ce
    /// qu'elles sont déjà. Le défaut RESTE posé sur la colonne après la migration
    /// (voir `StockReservationConfiguration`), pour que même une insertion qui ne
    /// passerait pas par EF soit correcte.
    ///
    /// CONSÉQUENCE À CONNAÎTRE AVANT DE DÉPLOYER : LE PREMIER BALAYAGE VA
    /// LIBÉRER BEAUCOUP.
    ///
    /// `ExpiresAtUtc` n'a JAMAIS été relue (ISSUE-031). Ces lignes `Active` sont
    /// donc, pour l'essentiel, des paniers abandonnés depuis des semaines. Dès que
    /// `ExpireStockReservationsWorker` démarre, il va les passer en `Expired` par
    /// lots et rendre leur stock à la vente — c'est le but, mais le volume
    /// journalisé au premier tour sera sans commune mesure avec le régime normal,
    /// et le stock disponible de nombreux articles va remonter d'un coup. Ce n'est
    /// pas un incident : c'est la mesure de ce que l'absence de balayeur coûtait.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES DOUBLONS EXISTANTS SONT FUSIONNÉS, PAS REFUSÉS. VOICI POURQUOI.
    ///
    /// L'index unique partiel échouerait si la base porte déjà deux réservations
    /// actives pour le même `(InventoryItemId, OrderId)` — c'est très exactement
    /// le doublon qu'ISSUE-075 décrit, et il y en a forcément : la boucle de
    /// `PlaceOrderCommandHandler` réservait une fois PAR LIGNE de commande, donc
    /// deux fois pour deux lignes de panier portant le même SKU au même
    /// emplacement.
    ///
    /// `UnicitePanierParCommande` (order-service), dans une situation d'apparence
    /// identique, REFUSE de migrer. Le choix inverse est fait ici, et la différence
    /// n'est pas d'opinion :
    ///
    ///   • Là-bas, les doublons sont des COMMANDES — des ventes réelles, peut-être
    ///     payées, livrées, remboursées. Choisir laquelle garder est une décision
    ///     commerciale qu'aucune règle automatique ne peut prendre, et un `DELETE`
    ///     effacerait une vente que personne n'a décidé d'annuler.
    ///
    ///   • Ici, les doublons sont des RÉSERVATIONS : une immobilisation technique
    ///     et temporaire, sans argent, sans expédition, sans trace comptable. Elles
    ///     appartiennent à la MÊME commande, sur le MÊME article. Leur somme est
    ///     précisément ce que l'appelant corrigé produit désormais en un seul
    ///     appel — deux lignes de panier du même SKU font une réservation de la
    ///     quantité totale.
    ///
    /// ET SURTOUT : LA FUSION NE CHANGE AUCUN STOCK.
    ///
    /// `Reserved` est la somme des quantités `Active`. Remplacer deux lignes de 3
    /// et 2 par une ligne de 5 laisse cette somme rigoureusement identique — donc
    /// `Available` aussi, sur chaque article, à l'unité près. Aucun stock
    /// n'apparaît, aucun ne disparaît. C'est ce qui rend la reprise sûre, et c'est
    /// ce qui manquait à l'autre cas.
    ///
    /// Refuser aurait bloqué le démarrage du service sur une condition que
    /// l'exploitant ne pourrait résoudre qu'en faisant À LA MAIN exactement cette
    /// somme, sur des lignes qu'il n'a aucun moyen d'arbitrer autrement.
    ///
    /// ON GARDE L'ÉCHÉANCE LA PLUS LOINTAINE (`max`).
    ///
    /// Le sens du refus est celui qui protège : garder la plus proche
    /// raccourcirait la fenêtre d'une commande peut-être encore en cours de
    /// paiement, et le balayeur libérerait son stock sous ses pieds. Garder la plus
    /// lointaine ne coûte au pire que quelques minutes d'immobilisation en trop,
    /// que le balayeur reprendra de lui-même.
    ///
    /// La ligne conservée est la plus petite `Id` du groupe — arbitraire et
    /// assumé : les lignes fusionnées sont interchangeables, seule leur somme
    /// compte. `array_agg("Id" ORDER BY "Id")` plutôt que `min("Id")` : l'agrégat
    /// `min` n'est pas défini pour le type `uuid` sur toutes les versions de
    /// PostgreSQL, l'ordre de tri l'est.
    ///
    /// Pour inspecter AVANT de migrer :
    ///
    ///     SELECT "InventoryItemId", "OrderId", count(*), sum("Quantity")
    ///     FROM inventory.stock_reservations
    ///     GROUP BY "InventoryItemId", "OrderId"
    ///     HAVING count(*) &gt; 1;
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// <para>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de fichier
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE — la colonne
    /// `Status` n'existe jamais, et le premier `SELECT` d'inventory tombe sur
    /// « column s.Status does not exist », au démarrage, après le déploiement.
    /// </para>
    /// </summary>
    [DbContext(typeof(InventoryDbContext))]
    [Migration("20260831000100_StatutDeReservation")]
    public partial class StatutDeReservation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "inventory",
                table: "stock_reservations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAtUtc",
                schema: "inventory",
                table: "stock_reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleasedAtUtc",
                schema: "inventory",
                table: "stock_reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAtUtc",
                schema: "inventory",
                table: "stock_reservations",
                type: "timestamp with time zone",
                nullable: true);

            // Fusion des doublons AVANT la pose de l'index — voir l'encadré.
            // Le filtre sur `Status` est redondant à cet instant (toutes les lignes
            // viennent de recevoir « Active ») mais il dit ce que la requête vise,
            // et il resterait juste si cette migration était rejouée plus tard.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    groupes int;
    lignes_fusionnees int;
BEGIN
    CREATE TEMP TABLE _fusion_reservations ON COMMIT DROP AS
    SELECT ""InventoryItemId"",
           ""OrderId"",
           sum(""Quantity"")::int          AS total,
           max(""ExpiresAtUtc"")           AS echeance,
           (array_agg(""Id"" ORDER BY ""Id""))[1] AS garde,
           count(*)::int                   AS lignes
    FROM inventory.stock_reservations
    WHERE ""Status"" = 'Active'
    GROUP BY ""InventoryItemId"", ""OrderId""
    HAVING count(*) > 1;

    -- Le nom de la variable differe de celui de la colonne : en plpgsql, une
    -- variable qui porte le nom d'une colonne rend la reference AMBIGUE et le
    -- bloc echoue a l'execution.
    SELECT count(*)::int, coalesce(sum(lignes), 0)::int
      INTO groupes, lignes_fusionnees
      FROM _fusion_reservations;

    IF groupes > 0 THEN
        UPDATE inventory.stock_reservations r
        SET ""Quantity"" = f.total,
            ""ExpiresAtUtc"" = f.echeance
        FROM _fusion_reservations f
        WHERE r.""Id"" = f.garde;

        DELETE FROM inventory.stock_reservations r
        USING _fusion_reservations f
        WHERE r.""InventoryItemId"" = f.""InventoryItemId""
          AND r.""OrderId"" = f.""OrderId""
          AND r.""Status"" = 'Active'
          AND r.""Id"" <> f.garde;

        RAISE NOTICE 'stock_reservations : % ligne(s) actives regroupees en % reservation(s), une par (article, commande). La somme des quantites reservees est INCHANGEE — aucun stock n''apparait ni ne disparait.', lignes_fusionnees, groupes;
    END IF;
END $$;");

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_active_order",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "InventoryItemId", "OrderId" },
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <summary>
        /// LA DESCENTE PERD DE L'INFORMATION, ET NE PEUT PAS FAIRE AUTREMENT.
        ///
        /// Retirer `Status` ramène le modèle d'avant, où une réservation libérée,
        /// expirée ou CONFIRMÉE est indiscernable d'une réservation en cours.
        /// `Reserved` redeviendrait alors la somme de TOUTES les lignes — y compris
        /// les ventes déjà retirées d'`OnHand` — et le stock vendable s'effondrerait.
        ///
        /// Une descente n'est donc sûre que sur une base où aucune transition n'a
        /// encore eu lieu depuis la montée. Sur une base exploitée, il faut d'abord
        /// supprimer les lignes non actives à la main, en connaissance de cause.
        /// La fusion des doublons, elle, n'est pas réversible : les lignes
        /// d'origine n'existent plus.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_stock_reservations_active_order",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "ExpiredAtUtc",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "ReleasedAtUtc",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "inventory",
                table: "stock_reservations");
        }
    }
}
