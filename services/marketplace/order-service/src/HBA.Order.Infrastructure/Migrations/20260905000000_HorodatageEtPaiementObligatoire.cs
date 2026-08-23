using System;
using HBA.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Orders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUAND CETTE COMMANDE A-T-ELLE BOUGÉ POUR LA DERNIÈRE FOIS, ET UNE COMMANDE
    /// PAYÉE PORTE-T-ELLE VRAIMENT UN PAIEMENT ? (§9.1, §9.2)
    ///
    /// DEUX DÉFAUTS DE LA MÊME TABLE, DONC UNE SEULE MIGRATION.
    ///
    /// Les séparer ferait deux allers-retours de schéma sur <c>ordering.orders</c>
    /// pour un seul geste de correction, et laisserait une fenêtre où la moitié
    /// du lot est déployée.
    ///
    /// ── 1. <c>UpdatedAtUtc</c>
    ///
    /// <c>orders</c> portait <c>CreatedAtUtc</c> et rien d'autre. En incident, la
    /// question posée n'est pourtant jamais « quand cette commande a-t-elle été
    /// créée » — on le voit à l'écran — mais « depuis combien de temps est-elle
    /// coincée dans cet état ». Sans cette colonne, une commande bloquée en
    /// <c>AwaitingPayment</c> depuis trois jours est indiscernable d'une créée il
    /// y a trois jours et payée à l'instant.
    ///
    /// NULLABLE, sans valeur par défaut : les commandes antérieures à cette
    /// migration restent à <c>NULL</c>, ce qui se lit « on ne sait pas ».
    /// <c>DEFAULT now()</c> leur aurait fait dire qu'elles ont toutes été touchées
    /// à la seconde du déploiement — une contrevérité, et de celles qu'on ne
    /// remarque pas.
    ///
    /// ── 2. <c>ck_orders_paid_requires_payment</c>
    ///
    /// <c>PaymentId</c> est nullable à raison (une commande non payée n'en a pas)
    /// mais rien ne le liait au statut. Le raisonnement complet — et notamment
    /// pourquoi QUATRE statuts et non le seul <c>Paid</c>, et pourquoi
    /// <c>Cancelled</c> en est exclu — est dans <c>OrderConfiguration</c>, à côté
    /// de la déclaration. Il n'est pas recopié ici : deux exemplaires d'un
    /// raisonnement finissent par se contredire, et c'est celui qui est loin du
    /// code qu'on croit.
    ///
    /// CETTE CONTRAINTE PEUT FAIRE ÉCHOUER LA MIGRATION.
    ///
    /// Sur une base contenant déjà une commande payée sans paiement, PostgreSQL
    /// refuse la contrainte et le service ne démarre pas — les migrations sont
    /// appliquées AVANT l'ouverture du port, délibérément. C'est le comportement
    /// voulu : un <c>NOT VALID</c> aurait laissé ces lignes fausses en place pour
    /// toujours, et la contrainte aurait menti sur ce qu'elle garantit.
    ///
    /// Repérage avant déploiement :
    ///
    ///     SELECT "Id", "Status", "CreatedAtUtc"
    ///     FROM ordering.orders
    ///     WHERE "Status" IN ('Paid','Confirmed','Delivered','UnderReview')
    ///       AND "PaymentId" IS NULL;
    ///
    /// Une ligne qui remonte est une commande encaissée dont on a perdu le
    /// rapprochement : elle se corrige à la main, pas par un assouplissement de
    /// la contrainte.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(OrderingDbContext))]
    [Migration("20260905000000_HorodatageEtPaiementObligatoire")]
    public partial class HorodatageEtPaiementObligatoire : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                schema: "ordering",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_paid_requires_payment",
                schema: "ordering",
                table: "orders",
                sql: "\"Status\" NOT IN ('Paid', 'Confirmed', 'Delivered', 'UnderReview') OR \"PaymentId\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CE RETOUR EN ARRIÈRE REND POSSIBLE UNE COMMANDE PAYÉE SANS
            // PAIEMENT, et perd l'horodatage de toutes les lignes. Les deux sont
            // irréversibles : rejouer la migration ne reconstruira aucune des
            // dates effacées.
            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_paid_requires_payment",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                schema: "ordering",
                table: "orders");
        }
    }
}
