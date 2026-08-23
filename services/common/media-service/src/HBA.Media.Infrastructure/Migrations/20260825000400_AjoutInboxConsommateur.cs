using HBA.Media.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Media.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TABLE `consumer_inbox` DU SCHÉMA `media`.
    ///
    /// CE QUE SON ABSENCE LAISSAIT PASSER, ICI, CONCRÈTEMENT.
    ///
    /// media-service consomme un événement : `KybDocumentRemoved`. Son
    /// gestionnaire SUPPRIME un fichier — une pièce d'identité, un extrait de
    /// registre — et une suppression ne se défait pas. Kafka livre AU MOINS UNE
    /// FOIS, donc ce gestionnaire s'exécutait deux fois pour un retrait.
    ///
    /// Le second passage ne détruit rien aujourd'hui, mais par chance et non par
    /// construction : `DeleteMediaOnKybDocumentRemovedHandler` relit le média,
    /// ne le trouve plus, et sort en Debug. Trois choses en dépendent — que le
    /// média ait bien disparu, que la relecture ne renvoie pas une ligne encore
    /// marquée supprimée, et que ce gestionnaire reste le seul. Aucune n'est
    /// garantie par le compilateur, et la troisième cessera d'être vraie dès
    /// qu'on branchera `media.ready` ou une reprise d'images.
    ///
    /// Il y a aussi un coût immédiat : chaque rejeu relit le média, refait
    /// l'appel de suppression, et journalise. Sur un rééquilibrage de partitions,
    /// c'est tout un lot de retraits KYB qui repasse.
    ///
    /// MEDIA NE CONNAÎT AUCUN AUTRE MODULE, ET CETTE TABLE NE CHANGE RIEN.
    ///
    /// Elle ne retient qu'un identifiant d'événement et un nom de consommateur —
    /// aucune clé étrangère, aucune jointure, rien qui rattache ce schéma à
    /// Sellers. C'est la même neutralité que `outbox_messages`.
    ///
    /// ATTRIBUTS SUR LA CLASSE, PAS DE FICHIER `.Designer.cs`.
    ///
    /// Convention du dépôt pour les migrations écrites à la main. `[DbContext]`
    /// et `[Migration]` doivent être TOUS LES DEUX présents : s'il en manque un,
    /// EF ignore la migration EN SILENCE — la table n'est jamais créée, rien ne
    /// le signale, et la garde d'idempotence lèverait au premier message.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(MediaDbContext))]
    [Migration("20260825000400_AjoutInboxConsommateur")]
    public partial class AjoutInboxConsommateur : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "media",
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
            // protège rendrait le retrait KYB rejouable à nouveau.
            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_processed_at",
                schema: "media",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox",
                schema: "media");
        }
    }
}
