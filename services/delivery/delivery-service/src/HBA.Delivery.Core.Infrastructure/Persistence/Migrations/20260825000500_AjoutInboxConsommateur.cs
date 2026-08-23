using System;
using HBA.Deliveries.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TABLE QUI MANQUAIT POUR QUE `deliveries` SURVIVE À UN REJEU KAFKA.
    ///
    /// CE QUE SON ABSENCE LAISSAIT PASSER, ICI, CONCRÈTEMENT.
    ///
    /// `IntegrationEventDispatcher` porte désormais la garde d'idempotence
    /// centralement, mais il résout `IConsumerInbox` en OPTIONNEL : un service
    /// sans table `consumer_inbox` continue de tourner, avec un simple
    /// avertissement au journal. C'était le cas de delivery-service.
    ///
    /// Ses six consommateurs d'événements d'intégration sont les enregistreurs de
    /// webhook partenaire — `WebhookOnDeliveryCreated`, `…Accepted`,
    /// `…PickedUp`, `…Completed`, `…Cancelled`, `…NoDriver`. Chacun met une ligne
    /// en file dans `webhook_deliveries`, que `WebhookDispatchService` POSTe
    /// ensuite chez le partenaire.
    ///
    /// Kafka livre AU MOINS UNE FOIS. Un rééquilibrage de partitions, une reprise
    /// de consumer, un redéploiement en cours de lot — et le même
    /// `DeliveryCompletedIntegrationEvent` repasse. Sans trace, il produit une
    /// SECONDE ligne de webhook : le partenaire reçoit deux fois « course
    /// terminée » pour une seule course, et facture — ou déclenche sa propre
    /// chaîne de règlement — deux fois. Le doublon n'est visible d'aucun côté :
    /// nos deux lignes sont légitimes, ses deux appels sont signés, et rien dans
    /// nos journaux ne distingue un rejeu d'une vraie seconde course.
    ///
    /// C'est le pire cas de ce module parce que le destinataire est EXTERNE : un
    /// effet interne se corrige par un script, un webhook parti chez un tiers ne
    /// se rattrape que par téléphone.
    ///
    /// CLÉ COMPOSITE (EventId, ConsumerName), ET NON EventId SEUL.
    ///
    /// Un même événement a plusieurs consommateurs dans ce service. Une clé sur
    /// le seul `EventId` ferait passer le premier handler pour la preuve que tous
    /// ont tourné : les cinq autres seraient sautés définitivement — un webhook
    /// jamais envoyé, ce qui est aussi grave que le doublon, dans l'autre sens.
    ///
    /// ÉCRITE À LA MAIN : LES DEUX ATTRIBUTS SONT OBLIGATOIRES.
    ///
    /// Il n'y a pas de `.Designer.cs` ici. `[DbContext]` et `[Migration]` portent
    /// donc sur la classe. Si l'un des deux manque, EF ignore la migration EN
    /// SILENCE — elle ne s'applique jamais, et rien ne le signale.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(DeliveriesDbContext))]
    [Migration("20260825000500_AjoutInboxConsommateur")]
    public partial class AjoutInboxConsommateur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "deliveries",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_inbox", x => new { x.EventId, x.ConsumerName });
                });

            // Sert UNIQUEMENT la purge : la table n'est jamais lue autrement que
            // par sa clé primaire. Voir ConsumerInboxConfiguration — la fenêtre de
            // rétention doit couvrir celle du topic Kafka, sinon effacer la trace
            // rouvre la porte au double traitement.
            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_processed_at",
                schema: "deliveries",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "deliveries");
        }
    }
}
