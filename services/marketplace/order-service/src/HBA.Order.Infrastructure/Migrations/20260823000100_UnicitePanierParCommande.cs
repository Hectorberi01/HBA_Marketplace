using HBA.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Orders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN PANIER NE PRODUIT QU'UNE COMMANDE.
    ///
    /// `POST /api/orders` N'ÉTAIT PAS IDEMPOTENT, ET RIEN NE S'Y OPPOSAIT.
    ///
    /// `CartId` n'avait ni contrainte d'unicité ni même un index. Un double-clic,
    /// un réseau lent suivi d'un renvoi, ou un rejeu de requête créait DEUX
    /// commandes sur le même panier — donc deux paiements à réclamer.
    ///
    /// La fenêtre n'est pas étroite : entre l'entrée dans le gestionnaire et la
    /// clôture du panier il y a une lecture gRPC du panier, une relecture de devis
    /// chez delivery-service et une boucle de réservation de stock. Et la clôture
    /// passe par Kafka, donc plus tard encore.
    ///
    /// POURQUOI LA VÉRIFICATION APPLICATIVE NE SUFFIT PAS.
    ///
    /// `GetByCartAsync` traite le cas courant — un second appel retrouve la
    /// première commande et la rend. Elle ne voit PAS deux requêtes SIMULTANÉES :
    /// les deux lisent « aucune commande » avant que l'une ait écrit. Seul cet
    /// index ferme la course, et il la ferme du bon côté — la seconde insertion
    /// échoue au lieu d'encaisser deux fois.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MIGRATION ÉCHOUERA SI DES DOUBLONS EXISTENT DÉJÀ.
    ///
    /// C'est voulu, et c'est même le seul comportement honnête : les doublons
    /// éventuels sont des commandes RÉELLES, peut-être payées. Aucune règle
    /// automatique ne peut décider laquelle garder — cela dépend de laquelle a été
    /// encaissée, livrée, remboursée. Un `DELETE` écrit ici effacerait une vente
    /// sans que personne ne l'ait décidé.
    ///
    /// Le contrôle préalable ci-dessous existe pour que l'échec soit LISIBLE. Sans
    /// lui, PostgreSQL rend « could not create unique index », qui ne dit ni
    /// combien de dossiers sont concernés ni comment les retrouver — et le service
    /// refuse de démarrer sur un message qu'on met une heure à décoder.
    ///
    /// Pour inspecter avant de reprendre :
    ///
    ///     SELECT "CartId", count(*), array_agg("Id"), array_agg("Status")
    ///     FROM ordering.orders
    ///     GROUP BY "CartId"
    ///     HAVING count(*) &gt; 1;
    ///
    /// En développement, repartir d'une base neuve est plus rapide que d'arbitrer.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(OrderingDbContext))]
    [Migration("20260823000100_UnicitePanierParCommande")]
    public partial class UnicitePanierParCommande : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
DECLARE doublons int;
BEGIN
    SELECT count(*) INTO doublons
    FROM (
        SELECT ""CartId""
        FROM ordering.orders
        GROUP BY ""CartId""
        HAVING count(*) > 1
    ) AS d;

    IF doublons > 0 THEN
        RAISE EXCEPTION
            'Impossible de rendre ordering.orders.""CartId"" unique : % panier(s) portent plusieurs commandes. Ce sont des ventes réelles, aucune règle automatique ne peut choisir laquelle garder. Pour les lister : SELECT ""CartId"", count(*), array_agg(""Id""), array_agg(""Status"") FROM ordering.orders GROUP BY ""CartId"" HAVING count(*) > 1;',
            doublons;
    END IF;
END $$;");

            migrationBuilder.CreateIndex(
                name: "IX_orders_CartId",
                schema: "ordering",
                table: "orders",
                column: "CartId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_CartId",
                schema: "ordering",
                table: "orders");
        }
    }
}
