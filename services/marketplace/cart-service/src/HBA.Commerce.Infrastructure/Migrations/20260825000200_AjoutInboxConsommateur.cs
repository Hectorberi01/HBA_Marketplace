using HBA.Commerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Commerce.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TRACE DE CONSOMMATION KAFKA POUR LE SCHÉMA `cart`.
    ///
    /// CE QUE SON ABSENCE LAISSAIT PASSER, ICI, ET IL FAUT LE DIRE EXACTEMENT.
    ///
    /// cart-service ne consomme qu'un seul événement d'intégration :
    /// `OrderPlaced`, qui clôt le panier d'où la commande est sortie. Kafka livre
    /// AU MOINS UNE FOIS, donc ce message est rejouable — et pourtant le rejeu ne
    /// vide pas un second panier aujourd'hui, parce que
    /// `CloseCartOnOrderPlacedHandler` commence par
    ///
    ///     if (cart is null || cart.Status != CartStatus.Active) return;
    ///
    /// Au second passage le panier est déjà `CheckedOut` : le gestionnaire rend la
    /// main sans rien écrire.
    ///
    /// CE N'EST PAS UNE PROTECTION, C'EST UNE COÏNCIDENCE HEUREUSE.
    ///
    /// Elle ne tient qu'à ce `if`, dans ce gestionnaire, tant que le panier reste
    /// un aggregate à sens unique. Elle tombe le jour où l'on autorise la
    /// réouverture d'un panier après annulation de commande — un rejeu tardif de
    /// `OrderPlaced` reviderait alors le panier que l'acheteur vient de remplir,
    /// et il découvrirait sa perte à l'écran de paiement. Elle ne dit rien non
    /// plus du PROCHAIN consommateur qu'on branchera ici : celui-là naîtrait sans
    /// garde, exactement comme les quatre-vingt-dix de l'audit.
    ///
    /// Le rejeu coûte aujourd'hui une lecture du panier par message dupliqué, et
    /// rien d'autre. Avec la trace, il ne coûte plus qu'une lecture de clé.
    ///
    /// SANS CETTE TABLE, LE SERVICE N'AURAIT PAS ÉCHOUÉ — IL SE SERAIT TU.
    ///
    /// `IConsumerInbox` est résolu en OPTIONNEL par `IntegrationEventDispatcher` :
    /// un service qui ne l'enregistre pas consomme sans garde, avec un simple
    /// avertissement au premier message. La garde est centrale précisément pour
    /// qu'aucun gestionnaire n'ait à se souvenir de la demander ; encore faut-il
    /// que le schéma sache où poser la trace. C'est ce que fait cette migration.
    ///
    /// La clé est composite `(EventId, ConsumerName)` : deux gestionnaires
    /// distincts doivent pouvoir traiter le MÊME message, chacun une fois.
    /// L'index sur `ProcessedAtUtc` ne sert qu'à la purge — la table n'est jamais
    /// lue autrement que par sa clé.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(CartDbContext))]
    [Migration("20260825000200_AjoutInboxConsommateur")]
    public partial class AjoutInboxConsommateur : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "cart",
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
                schema: "cart",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "cart");
        }
    }
}
