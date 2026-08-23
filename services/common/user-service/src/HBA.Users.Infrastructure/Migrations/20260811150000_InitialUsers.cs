using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Users.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// NAISSANCE DU SCHÉMA « users » — ET REPRISE DU CARNET D'ADRESSES.
    ///
    /// Le cahier d'architecture sépare Identity (« qui peut se connecter ? ») de User
    /// (« qui est la personne ? »). Les adresses appartiennent au second. Cette
    /// migration crée leur nouvelle table, puis RECOPIE les lignes existantes depuis
    /// <c>identity.addresses</c>.
    ///
    /// L'ORDRE DE DÉPLOIEMENT N'EST PAS INDIFFÉRENT.
    ///
    /// Une migration jumelle, côté Identity, SUPPRIME l'ancienne table. Elle doit être
    /// appliquée APRÈS celle-ci, sinon la copie ne trouve plus rien à copier et les
    /// carnets d'adresses partent avec. <c>Program.cs</c> migre donc Users avant
    /// Identity ; une application manuelle des migrations doit respecter le même ordre.
    ///
    /// POURQUOI UNE COPIE PLUTÔT QU'UN « ALTER TABLE … SET SCHEMA »
    ///
    /// Deux modules, deux historiques de migrations EF distincts. Déplacer la table
    /// d'un schéma à l'autre en une instruction laisserait chacun des deux historiques
    /// convaincu de posséder l'objet — et le premier « rollback » venu supprimerait une
    /// table que l'autre croit sienne. La copie coûte un balayage de table ; elle laisse
    /// deux historiques cohérents.
    ///
    /// SUR UNE BASE NEUVE, la copie ne fait rien : <c>identity.addresses</c> n'existe
    /// pas, la garde le voit, et la migration se termine sans erreur.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class InitialUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "users");

            migrationBuilder.CreateTable(
                name: "addresses",
                schema: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CommuneCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Quartier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_addresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_addresses_CommuneCode",
                schema: "users",
                table: "addresses",
                column: "CommuneCode");

            migrationBuilder.CreateIndex(
                name: "IX_addresses_UserId",
                schema: "users",
                table: "addresses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "users",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "users",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");

            // ─────────────────────────────────────────────────────────────────────
            // REPRISE DES ADRESSES EXISTANTES.
            //
            // Colonne par colonne, sans transformation : le modèle est identique, seul
            // le schéma change. Les adresses héritées incomplètes le restent — les
            // compléter ici reviendrait à inventer un point de repère, c'est-à-dire à
            // envoyer un coursier à une adresse fabriquée.
            //
            // « ON CONFLICT DO NOTHING » rend la reprise REJOUABLE : si la migration
            // est appliquée deux fois (restauration partielle, environnement recréé
            // à partir d'un dump intermédiaire), les lignes déjà présentes sont
            // laissées telles quelles au lieu de faire échouer tout le déploiement.
            //
            // Les identifiants sont CONSERVÉS. Une application mobile qui garde en
            // mémoire l'adresse choisie au dernier passage en caisse la retrouve ;
            // régénérer les clés aurait cassé chaque panier en cours.
            // ─────────────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('identity.addresses') IS NOT NULL THEN
                        INSERT INTO users.addresses (
                            "Id", "UserId", "Label", "Recipient", "Phone", "CommuneCode",
                            "Quartier", "Landmark", "Line1", "CountryCode", "Latitude",
                            "Longitude", "IsDefault", "CreatedOnUtc")
                        SELECT
                            "Id", "UserId", "Label", "Recipient", "Phone", "CommuneCode",
                            "Quartier", "Landmark", "Line1", "CountryCode", "Latitude",
                            "Longitude", "IsDefault", "CreatedOnUtc"
                        FROM identity.addresses
                        ON CONFLICT ("Id") DO NOTHING;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Le retour arrière SUPPRIME les adresses reprises. Il n'a de sens que si la
        /// migration jumelle côté Identity — celle qui supprime l'ancienne table — n'a
        /// pas encore été appliquée : les données sont alors encore dans
        /// <c>identity.addresses</c>. Une fois les deux passées, revenir en arrière
        /// exige une restauration de sauvegarde, pas un « down ».
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "addresses",
                schema: "users");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "users");
        }
    }
}
