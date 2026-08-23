using System;
using HBA.Food.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Food.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TABLE QUI MANQUAIT POUR QUE `food` SURVIVE À UN REJEU KAFKA.
    ///
    /// CE QUE SON ABSENCE LAISSAIT PASSER, ICI, CONCRÈTEMENT.
    ///
    /// `IntegrationEventDispatcher` porte la garde d'idempotence centralement,
    /// mais il résout `IConsumerInbox` en OPTIONNEL : un service sans table
    /// `consumer_inbox` continue de tourner, avec un simple avertissement au
    /// journal. C'était le cas de restaurant-service — et c'est le service où le
    /// doublon se voit le plus vite, parce qu'il produit un objet PHYSIQUE.
    ///
    /// Ses quatre consommateurs :
    ///
    ///   • `ReceiveFoodOrderOnOrderConfirmedHandler` — `order.confirmed` ouvre le
    ///     ticket de cuisine. Rejoué, il en ouvre un SECOND sur la même commande :
    ///     le restaurateur voit deux tickets identiques, prépare deux repas, et
    ///     n'en facture qu'un. La perte est sèche, et personne ne peut la
    ///     rattraper — le plat est cuit.
    ///   • `CreateDeliveryOnFoodOrderReadyHandler` — `food.order.ready` achète une
    ///     course. Rejoué, il en crée une seconde : deux livreurs sont dépêchés
    ///     au même restaurant, l'un repart à vide, et la plateforme paie deux
    ///     courses pour un repas que le client a payé une fois.
    ///   • `MarkFoodOrderPickedUpOnDeliveryPickedUpHandler` et
    ///     `MarkFoodOrderDeliveredOnDeliveryCompletedHandler` — les retours de
    ///     course. Rejoués, ils rejouent des transitions d'état déjà faites.
    ///
    /// Kafka livre AU MOINS UNE FOIS : il ne faut ni panne ni bug pour déclencher
    /// tout cela, un simple rééquilibrage de partitions suffit.
    ///
    /// CLÉ COMPOSITE (EventId, ConsumerName), ET NON EventId SEUL.
    ///
    /// `DeliveryCompleted` a ici plusieurs consommateurs. Une clé sur le seul
    /// `EventId` ferait passer le premier pour la preuve que tous ont tourné : les
    /// autres seraient sautés DÉFINITIVEMENT — un ticket jamais clos, donc un
    /// restaurateur jamais réglé, ce qui est aussi grave que le doublon.
    ///
    /// ÉCRITE À LA MAIN : LES DEUX ATTRIBUTS SONT OBLIGATOIRES.
    ///
    /// Il n'y a pas de `.Designer.cs` ici. `[DbContext]` et `[Migration]` portent
    /// donc sur la classe. Si l'un des deux manque, EF ignore la migration EN
    /// SILENCE — elle ne s'applique jamais, et rien ne le signale.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(FoodDbContext))]
    [Migration("20260825000600_AjoutInboxConsommateur")]
    public partial class AjoutInboxConsommateur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "food",
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
                schema: "food",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "food");
        }
    }
}
