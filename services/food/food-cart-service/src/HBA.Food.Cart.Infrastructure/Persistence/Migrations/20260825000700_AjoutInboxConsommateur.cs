using System;
using HBA.FoodCarts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.FoodCarts.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TABLE QUI MANQUAIT POUR QUE LE PANIER DE REPAS RÉSISTE AUX REJEUX.
    ///
    /// SANS ELLE, CE SERVICE TOURNAIT SANS AUCUNE GARDE, ET RIEN NE LE DISAIT.
    ///
    /// `IntegrationEventDispatcher` résout `IConsumerInbox` en OPTIONNEL : un
    /// service qui ne l'enregistre pas démarre normalement et consomme
    /// normalement. Le seul signe était un avertissement au premier message. Ce
    /// n'était donc pas une panne à chercher, mais une protection à constater
    /// absente — et personne ne constate une absence.
    ///
    /// CE QUE LE REJEU FAISAIT PASSER, CONCRÈTEMENT, ICI.
    ///
    /// Ce service écoute `MealOrderPlaced` pour clore le panier
    /// (`CloseFoodCartOnMealOrderPlacedHandler`). Kafka livre AU MOINS UNE FOIS :
    /// un rééquilibrage de partitions relivre ce message. Le gestionnaire termine
    /// par une éviction de cache dont la clé est l'ACHETEUR, pas le panier —
    /// `FoodCartCacheKeys.Active(buyerId)`. Un second passage fait donc tomber le
    /// panier actif que le client a commencé à remplir ENTRE-TEMPS, après une
    /// commande déjà partie : ses articles disparaissent de l'écran sans qu'il ait
    /// rien touché.
    ///
    /// ET NON, LE GARDE-FOU DÉJÀ PRÉSENT NE REND PAS CETTE TABLE INUTILE.
    ///
    /// Ce gestionnaire se défend seul : il vérifie `Status == Active` et sort si le
    /// panier est déjà clos. C'est ce test — écrit à la main, dans CE
    /// gestionnaire — qui empêche aujourd'hui l'éviction décrite ci-dessus. Il ne
    /// protège que lui : le gestionnaire suivant qu'on ajoutera ici partira sans
    /// garde, comme les quatre-vingt-dix de l'audit, et personne ne s'en
    /// apercevra. L'inbox déplace la protection du gestionnaire vers le
    /// dispatcher, où elle vaut pour tous, y compris ceux qui n'existent pas
    /// encore.
    ///
    /// POURQUOI L'ATOMICITÉ EST ACQUISE SANS RIEN DEMANDER AU GESTIONNAIRE.
    ///
    /// La trace est ajoutée au contexte AVANT l'appel, sans écriture : elle part
    /// dans le `SaveChangesAsync` du gestionnaire — celui-ci en fait un, sur son
    /// unité de travail. Effet métier et trace sont donc committés ensemble, ou
    /// pas du tout. Voir l'encadré d'`IntegrationEventDispatcher`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(FoodCartDbContext))]
    [Migration("20260825000700_AjoutInboxConsommateur")]
    public partial class AjoutInboxConsommateur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "food_cart",
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
                schema: "food_cart",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "food_cart");
        }
    }
}
