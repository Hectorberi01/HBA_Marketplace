using HBA.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Orders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TABLE QUI MANQUAIT POUR QU'UN REJEU KAFKA NE COMMANDE PAS UN SECOND
    /// LIVREUR.
    ///
    /// CE QUE SON ABSENCE LAISSAIT PASSER, ICI, CONCRÈTEMENT.
    ///
    /// order-service consomme neuf événements d'intégration — capture et échec de
    /// paiement, fin et annulation de course, refus, annulation et remise d'une
    /// commande de repas, et ses propres confirmation et annulation. Kafka livre
    /// AU MOINS UNE FOIS : un rééquilibrage de partitions ou une reprise du
    /// consumer les repasse tous.
    ///
    /// La plupart retombent sur une garde d'état de l'agrégat : `Confirm()` exige
    /// « payée », `MarkDelivered()` exige « confirmée ». Au rejeu, la transition
    /// échoue et `SagaOutcome` journalise. C'est de l'idempotence par accident —
    /// elle tient tant que la transition reste interdite, et elle ne dit rien du
    /// gestionnaire qui n'en a pas.
    ///
    /// `CreateDeliveryOnOrderConfirmedHandler` n'en a pas. Il ne relit rien, il ne
    /// vérifie rien : il demande une course à delivery-service. Un
    /// `OrderConfirmed` rejoué en demande donc une SECONDE pour la même commande —
    /// deux livreurs envoyés au même lieu d'expédition pour un seul colis, deux
    /// devis de course facturés, et la commande close à la première remise
    /// pendant que l'autre course reste ouverte. Le vendeur est réglé sur une
    /// course, la plateforme en paie deux.
    ///
    /// CETTE TABLE SEULE NE PROTÈGE PAS CE GESTIONNAIRE-LÀ.
    ///
    /// Elle est la condition nécessaire, pas suffisante. `IntegrationEventDispatcher`
    /// pose la trace AVANT d'appeler le handler pour qu'elle parte dans le même
    /// `SaveChangesAsync` que l'effet métier — or ce gestionnaire-ci n'écrit dans
    /// `ordering` que s'il bascule en arbitrage. Dans le cas nominal, aucun
    /// `SaveChanges` n'a lieu et la trace reste en attente, jamais committée :
    /// c'est la limite nommée au §8.2 de `KAFKA_EVENT_MATRIX.md`. Les
    /// gestionnaires qui, eux, écrivent dans `ordering` — ceux qui confirment,
    /// annulent, mettent en arbitrage ou clôturent une commande — sont protégés
    /// dès cette migration.
    ///
    /// SANS CETTE TABLE, LE SERVICE N'AURAIT PAS ÉCHOUÉ — IL SE SERAIT TU.
    ///
    /// `IConsumerInbox` est résolu en OPTIONNEL par le dispatcher : un service qui
    /// ne l'enregistre pas continue de consommer sans garde, avec un simple
    /// avertissement au premier message. Rien ne casse, rien ne remonte, et le
    /// double effet ne se découvre qu'au premier rejeu réel.
    ///
    /// La clé est composite `(EventId, ConsumerName)` : deux gestionnaires
    /// distincts doivent pouvoir traiter le MÊME message, chacun une fois.
    /// L'index sur `ProcessedAtUtc` ne sert qu'à la purge — la table n'est jamais
    /// lue autrement que par sa clé.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(OrderingDbContext))]
    [Migration("20260825000100_AjoutInboxConsommateur")]
    public partial class AjoutInboxConsommateur : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "ordering",
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

            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_processed_at",
                schema: "ordering",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "ordering");
        }
    }
}
