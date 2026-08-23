using System;
using HBA.FoodOrders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.FoodOrders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TABLE QUI MANQUAIT POUR QUE LA COMMANDE DE REPAS RÉSISTE AUX REJEUX.
    ///
    /// SANS ELLE, CE SERVICE TOURNAIT SANS AUCUNE GARDE, ET RIEN NE LE DISAIT.
    ///
    /// `IntegrationEventDispatcher` résout `IConsumerInbox` en OPTIONNEL : un
    /// service qui ne l'enregistre pas démarre normalement et consomme
    /// normalement. Le seul signe était un avertissement au premier message.
    ///
    /// C'est le service le plus exposé du domaine food : CINQ gestionnaires
    /// d'intégration y écoutent l'argent et la cuisine — paiement encaissé,
    /// paiement échoué, ticket refusé, ticket annulé, repas remis. Kafka livre AU
    /// MOINS UNE FOIS, donc un rééquilibrage de partitions les relivre tous.
    ///
    /// CE QUE LE REJEU FAISAIT PASSER, CONCRÈTEMENT, ICI.
    ///
    /// La machine à états de `MealOrder` refuse la transition rejouée — `Confirm`
    /// exige `Paid`, `MarkDelivered` exige `Confirmed` ou `UnderReview`, `Cancel`
    /// refuse une commande déjà annulée. Le repas n'est donc pas confirmé deux
    /// fois et l'escrow n'est pas levé deux fois. Mais les gestionnaires
    /// transforment ce refus en alerte : `SagaOutcome.Exiger` journalise en erreur
    /// « annuler la commande après refus du restaurant — LE CLIENT A ÉTÉ DÉBITÉ ».
    ///
    /// Autrement dit, un simple rééquilibrage de partitions réveillait
    /// l'astreinte avec une alerte critique de débit sans commande, sur des
    /// commandes parfaitement saines. Une alerte qui crie faux finit par ne plus
    /// être lue — et c'est ce jour-là qu'une vraie passe.
    ///
    /// ET LA MACHINE À ÉTATS N'EST PAS UNE INBOX.
    ///
    /// Elle protège parce que chaque transition a été écrite serrée, une par une,
    /// à la main. `MarkDelivered` accepte déjà DEUX états de départ pour une
    /// raison métier documentée — la commande remise après un passage en
    /// arbitrage. Chaque assouplissement de ce genre est une porte qu'aucun garde
    /// ne surveille, et le gestionnaire suivant qu'on ajoutera ici partira sans
    /// garde du tout. L'inbox déplace la protection du domaine vers le dispatcher,
    /// où elle vaut pour tous les gestionnaires, y compris ceux qui n'existent pas
    /// encore.
    ///
    /// POURQUOI L'ATOMICITÉ EST ACQUISE SANS RIEN DEMANDER AUX GESTIONNAIRES.
    ///
    /// La trace est ajoutée au contexte AVANT l'appel, sans écriture : elle part
    /// dans le `SaveChangesAsync` de la commande MediatR que le gestionnaire
    /// déclenche. Effet métier et trace sont committés ensemble, ou pas du tout.
    /// Voir l'encadré d'`IntegrationEventDispatcher`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(MealOrderingDbContext))]
    [Migration("20260825000800_AjoutInboxConsommateur")]
    public partial class AjoutInboxConsommateur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "food_ordering",
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

            // Cet index ne sert PAS au chemin chaud — la table n'est lue que par sa
            // clé primaire. Il sert à la purge, qui doit conserver au moins la
            // fenêtre de rétention Kafka du sujet : effacer une trace avant le
            // message qu'elle protège rouvrirait la porte au double traitement.
            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_processed_at",
                schema: "food_ordering",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "food_ordering");
        }
    }
}
