using System;
using HBA.Drivers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Drivers.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// MIGRATION INITIALE — driver-service N'AVAIT AUCUNE BASE (ISSUE-030).
    ///
    /// Ni `DbContext`, ni dossier `Migrations`, ni schéma. Tout l'état du service
    /// vivait dans un `ConcurrentDictionary` peuplé au démarrage d'un unique
    /// livreur déjà « VERIFIED », dont l'identifiant était codé en dur
    /// (`DefaultDriverId`). Les six routes `/api/v1/drivers/me*` opéraient toutes
    /// sur lui : TOUS LES LIVREURS ÉTAIENT LE MÊME LIVREUR (ISSUE-029).
    ///
    /// CE SCHÉMA NE REPREND RIEN. Il n'y a aucune reprise de données parce
    /// qu'il n'y a aucune donnée : rien n'a jamais été écrit sur disque par ce
    /// service. C'est la seule migration initiale de ce dépôt dont on puisse le
    /// dire avec certitude.
    ///
    /// IL NE DÉPLACE PAS `deliveries.drivers`, ET C'EST DÉLIBÉRÉ (D34).
    ///
    /// La table de delivery-service porte la PROJECTION DISPATCHABLE — disponibilité,
    /// dernière position, compteur de courses — que le dispatch lit à chaud.
    /// Celle-ci porte le DOSSIER : inscription, pièces, décision de vérification.
    /// Deux propriétaires, deux rythmes d'écriture, aucune clé étrangère entre les
    /// deux. Le lien est l'événement `driver.dossier-verified`.
    ///
    /// MÊME BASE PHYSIQUE QUE `deliveries`, SCHÉMA DIFFÉRENT.
    /// `docker-compose.dev.yml` donne déjà `hba_delivery` à ce service. Ce n'est
    /// pas un partage de données : la règle de `ModuleDbContext` tient, et aucune
    /// jointure ne traverse les deux schémas.
    ///
    /// <para>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE — le schéma `drivers`
    /// n'existe alors dans aucune base, et la première lecture rend « relation
    /// drivers.driver_accounts does not exist » au démarrage, après déploiement.
    /// </para>
    /// </summary>
    [DbContext(typeof(DriverDbContext))]
    [Migration("20260905000100_InitialDrivers")]
    public partial class InitialDrivers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "drivers");

            // ─────────────────────────────────────────────────────────────────
            // Outbox du module. Forme COMPLÈTE dès la création — `CorrelationId`
            // et `TraceParent` compris. Les modules historiques les ont reçues par
            // des migrations séparées ; un module neuf n'a aucune raison de
            // rejouer cette dette.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "drivers",
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
                    DeadLetteredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TraceParent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "driver_accounts",
                schema: "drivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VerificationStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RegisteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "driver_documents",
                schema: "drivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_driver_documents_driver_accounts_DriverId",
                        column: x => x.DriverId,
                        principalSchema: "drivers",
                        principalTable: "driver_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "driver_vehicles",
                schema: "drivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Make = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Plate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CapacityKg = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    DeclaredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_driver_vehicles_driver_accounts_DriverId",
                        column: x => x.DriverId,
                        principalSchema: "drivers",
                        principalTable: "driver_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // C'EST CET INDEX QUI CORRIGE RÉELLEMENT ISSUE-029.
            //
            // Le contrôle applicatif d'unicité laisse passer deux inscriptions
            // concurrentes du même compte. Ici, la seconde échoue, et
            // `ServiceExceptionMiddleware` la traduit en 409.
            migrationBuilder.CreateIndex(
                name: "ux_driver_accounts_user",
                schema: "drivers",
                table: "driver_accounts",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_driver_accounts_status",
                schema: "drivers",
                table: "driver_accounts",
                column: "VerificationStatus");

            // Une seule pièce par type et par dossier : redéposer REMPLACE.
            migrationBuilder.CreateIndex(
                name: "ux_driver_documents_type",
                schema: "drivers",
                table: "driver_documents",
                columns: new[] { "DriverId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_driver_vehicles_driver",
                schema: "drivers",
                table: "driver_vehicles",
                column: "DriverId");

            // Les deux index partiels de l'outbox, identiques à ceux des vingt-trois
            // autres modules : le lot en attente d'un côté, les lettres mortes de
            // l'autre.
            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "drivers",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "drivers",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");
        }

        /// <summary>
        /// La descente supprime les dossiers, donc les pièces déposées par les
        /// livreurs. Ce n'est réversible sur aucune base exploitée : rien ailleurs
        /// ne conserve la trace de qui a été vérifié, ni quand.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "driver_documents", schema: "drivers");
            migrationBuilder.DropTable(name: "driver_vehicles", schema: "drivers");
            migrationBuilder.DropTable(name: "driver_accounts", schema: "drivers");
            migrationBuilder.DropTable(name: "outbox_messages", schema: "drivers");
        }
    }
}
