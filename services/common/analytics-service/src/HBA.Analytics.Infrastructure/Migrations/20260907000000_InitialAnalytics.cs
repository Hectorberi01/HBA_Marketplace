using System;
using HBA.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Analytics.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE SCHÉMA `analytics` : TROIS ROLL-UPS ET UNE INBOX.
    ///
    /// ÉCRITE À LA MAIN, ET IL FAUT LE SAVOIR EN LA RELISANT.
    ///
    /// Aucun SDK .NET n'était disponible sur le poste où ce service a été écrit :
    /// cette migration et l'instantané qui l'accompagne n'ont pas été engendrés
    /// par `dotnet ef migrations add`. Ils suivent la forme des vingt-quatre
    /// autres, mais ils n'ont pas été vérifiés par l'outil.
    ///
    /// LA VÉRIFICATION EXISTE, ET ELLE EST À FAIRE AVANT TOUT DÉPLOIEMENT :
    ///
    ///     ./scripts/verifier-migrations.sh --contexte=AnalyticsDbContext
    ///
    /// Elle demande une migration de plus et vérifie que son diff est VIDE. Un
    /// diff non vide dit exactement ce que cette migration a manqué. Tant qu'elle
    /// n'a pas tourné, le seul fait établi est que le contrôle `migrations`
    /// retrouve un `CreateTable` pour chaque `.ToTable(...)` — ce qui ne dit rien
    /// des colonnes.
    ///
    /// PAS DE `.Designer.cs`, ET C'EST UN CHOIX SUIVI DANS LE DÉPÔT.
    ///
    /// Les attributs `[DbContext]` et `[Migration]` sont posés ici même, comme
    /// dans `AjoutInboxConsommateur`, `AjoutTraceParentOutbox` et
    /// `UnSeulPanierActifParAcheteur`. La conséquence à connaître : `dotnet ef
    /// migrations remove` ne peut pas restaurer l'instantané précédent depuis un
    /// Designer qui n'existe pas. Pour cette migration-ci c'est sans objet — elle
    /// est la première, et il n'y a pas d'état antérieur à restaurer.
    ///
    /// LES QUATRE TABLES SONT DANS LE SCHÉMA DU SERVICE, ET AUCUNE CLÉ ÉTRANGÈRE
    /// N'EN SORT.
    ///
    /// `SellerId` désigne un vendeur de seller-service, dans une AUTRE base :
    /// une contrainte vers lui est impossible, et ne serait pas souhaitable. Ces
    /// tables ne contraignent rien, elles comptent — c'est aussi ce qui les rend
    /// reconstructibles à partir des événements.
    ///
    /// `consumer_inbox` N'EST PAS OPTIONNELLE ICI. Les trois gestionnaires de ce
    /// service INCRÉMENTENT : sans cette table, `IConsumerInbox` n'est pas résolu,
    /// le socle consomme quand même avec un simple avertissement, et le premier
    /// rebalancement de partition gonfle des chiffres que personne ne saura
    /// corriger.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(AnalyticsDbContext))]
    [Migration("20260907000000_InitialAnalytics")]
    public partial class InitialAnalytics : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "analytics");

            migrationBuilder.CreateTable(
                name: "platform_daily",
                schema: "analytics",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OrdersCount = table.Column<int>(type: "integer", nullable: false),
                    ItemsCount = table.Column<int>(type: "integer", nullable: false),
                    Gmv = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_daily", x => new { x.Day, x.Kind, x.Currency });
                });

            migrationBuilder.CreateTable(
                name: "seller_daily",
                schema: "analytics",
                columns: table => new
                {
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OrdersCount = table.Column<int>(type: "integer", nullable: false),
                    ItemsCount = table.Column<int>(type: "integer", nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_daily", x => new { x.SellerId, x.Day, x.Currency, x.Kind });
                });

            migrationBuilder.CreateTable(
                name: "signup_daily",
                schema: "analytics",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signup_daily", x => new { x.Day, x.Kind });
                });

            // LA CLÉ EST COMPOSITE `(EventId, ConsumerName)` : deux gestionnaires
            // distincts doivent pouvoir traiter le MÊME message, chacun une fois.
            // L'index sur `ProcessedAtUtc` ne sert qu'à la purge — la table n'est
            // jamais lue autrement que par sa clé.
            migrationBuilder.CreateTable(
                name: "consumer_inbox",
                schema: "analytics",
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
                schema: "analytics",
                table: "consumer_inbox",
                column: "ProcessedAtUtc");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "consumer_inbox", schema: "analytics");
            migrationBuilder.DropTable(name: "platform_daily", schema: "analytics");
            migrationBuilder.DropTable(name: "seller_daily", schema: "analytics");
            migrationBuilder.DropTable(name: "signup_daily", schema: "analytics");
        }
    }
}
