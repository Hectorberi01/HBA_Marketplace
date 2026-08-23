using System;
using HBA.Merchants.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HBA.Merchants.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL DE QUI A FAIT QUOI — <c>sellers.audit_entries</c> (§37, lot 0c).
    ///
    /// Une ligne par entité mutée, écrite dans la MÊME transaction que la mutation
    /// par <c>ModuleDbContext.SaveChangesAsync</c>. Voir <c>AuditEntry</c> pour le
    /// raisonnement — notamment pourquoi ce n'est pas une colonne
    /// <c>LastModifiedBy</c> sur chaque table, et pourquoi la source est le
    /// <c>ChangeTracker</c> et non les signatures de commande.
    ///
    /// ÉCRITE À LA MAIN, comme les autres migrations de ce dépôt. L'attribut
    /// <c>[Migration]</c> est porté ici plutôt que par un fichier Designer ; le
    /// snapshot est mis à jour dans le même commit.
    ///
    /// <c>ActorUserId</c> EST NULLABLE, ET C'EST UNE INFORMATION.
    ///
    /// Un consommateur Kafka, un appel gRPC interne, un travail de fond : la
    /// mutation n'a pas de personne derrière elle. Une contrainte NOT NULL
    /// forcerait à inventer un identifiant, et le jour où l'on chercherait qui a
    /// annulé mille lignes on trouverait un compte qui n'existe pas.
    ///
    /// AUCUNE REPRISE DE DONNÉES.
    ///
    /// Le journal commence à cette migration. Rien ne permet de reconstituer
    /// rétroactivement qui a fait quoi — l'information n'a jamais été écrite. Une
    /// table pré-remplie avec un acteur inventé serait pire que vide : elle
    /// donnerait des réponses fausses à des questions sérieuses.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(SellersDbContext))]
    [Migration("20260819160000_JournalDAudit")]
    public partial class JournalDAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "sellers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            // « qu'est-il arrivé à CETTE ligne » — la question d'un litige sur une
            // fiche, une offre, une commande précise.
            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_EntityType_EntityId_OccurredOnUtc",
                schema: "sellers",
                table: "audit_entries",
                columns: new[] { "EntityType", "EntityId", "OccurredOnUtc" });

            // « qu'a fait CE membre » — la question d'un vendeur qui découvre des
            // dégâts et cherche lequel de ses employés en est à l'origine.
            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_ActorUserId_OccurredOnUtc",
                schema: "sellers",
                table: "audit_entries",
                columns: new[] { "ActorUserId", "OccurredOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // IRRÉVERSIBLE EN PRATIQUE : le retour arrière efface le journal, et
            // rien ne le reconstitue. C'est acceptable pour un retour immédiat après
            // livraison, jamais pour une réparation à froid.
            migrationBuilder.DropTable(name: "audit_entries", schema: "sellers");
        }
    }
}
