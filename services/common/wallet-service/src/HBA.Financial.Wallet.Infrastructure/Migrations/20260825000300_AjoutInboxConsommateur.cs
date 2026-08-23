using HBA.Financial.Wallet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Wallet.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TABLE `consumer_inbox` DU SCHÉMA `settlement`.
    ///
    /// SANS ELLE, UN REJEU KAFKA PAYAIT DEUX FOIS.
    ///
    /// Ce module ne fait presque rien d'autre qu'écrire de l'argent, et il le fait
    /// sur réception d'événements. Six gestionnaires, aucun protégé :
    ///
    ///   • `AccrueEarningsOnOrderConfirmedHandler` — un `OrderConfirmed` relivré
    ///     comptabilise une SECONDE fois le gain du vendeur sur la même commande ;
    ///   • `CreditDriverOnDeliveryCompletedHandler` — un `DeliveryCompleted`
    ///     relivré crédite le portefeuille du livreur deux fois pour une course,
    ///     et débite d'autant le solde « livraison » de la plateforme ;
    ///   • `ReleaseEarningsOnOrderDeliveredHandler` et
    ///     `ReleaseSellerEarningsOnShipmentDeliveredHandler` — l'escrow est levé
    ///     deux fois, donc payable deux fois ;
    ///   • `ReverseEarningsOnReturnRefundedHandler` et
    ///     `ReverseEarningsOnOrderCancelledHandler` — la contre-passation est
    ///     appliquée deux fois, et le vendeur est débité du double de ce qu'il
    ///     avait gagné.
    ///
    /// Kafka livre AU MOINS UNE FOIS : ce n'est pas un scénario de panne, c'est le
    /// contrat du transport. Un rééquilibrage de partitions suffit.
    ///
    /// POURQUOI UNE TABLE ICI PLUTÔT QU'UNE INBOX PARTAGÉE.
    ///
    /// Elle doit être écrite par la MÊME transaction que le crédit qu'elle
    /// protège — c'est toute la valeur du dispositif. Une inbox commune à
    /// plusieurs services vivrait dans une autre base et rendrait cette atomicité
    /// impossible, en plus de recréer la base partagée que le §9 interdit.
    ///
    /// LA CLÉ EST LE COUPLE (EventId, ConsumerName), PAS `EventId` SEUL.
    ///
    /// Sinon le premier gestionnaire servi ferait taire tous les autres : un
    /// `OrderDelivered` traité par la libération d'escrow serait considéré comme
    /// déjà traité par la comptabilisation. Voir `ConsumerInboxEntry`.
    ///
    /// ATTRIBUTS SUR LA CLASSE, PAS DE FICHIER `.Designer.cs`.
    ///
    /// Convention du dépôt pour les migrations écrites à la main. `[DbContext]`
    /// et `[Migration]` doivent être TOUS LES DEUX présents : s'il en manque un,
    /// EF ignore la migration EN SILENCE — la table n'est jamais créée, rien ne
    /// le signale, et la garde d'idempotence lèverait au premier message.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(WalletDbContext))]
    [Migration("20260825000300_AjoutInboxConsommateur")]
    public partial class AjoutInboxConsommateur : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "settlement",
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

            // Sert UNIQUEMENT la purge : la table n'est jamais lue autrement que par
            // sa clé primaire. La purge doit conserver au moins la fenêtre de
            // rétention Kafka du topic — effacer une trace avant le message qu'elle
            // protège rouvrirait la porte au double crédit.
            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_processed_at",
                schema: "settlement",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "settlement");
        }
    }
}
