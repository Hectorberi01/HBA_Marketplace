using System;
using HBA.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Identity.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SUPPRESSION DÉFINITIVE DE <c>identity.payment_methods</c>.
    ///
    /// La table a été reprise dans <c>payments.payment_methods</c> par la
    /// migration <c>AdoptPaymentMethods</c>. Plus aucun code ne la lit : l'entité
    /// a quitté le modèle d'Identity, le DbSet et le dépôt ont été supprimés.
    ///
    /// CETTE MIGRATION EST ÉCRITE À LA MAIN, ET C'EST DÉLIBÉRÉ.
    ///
    /// EF l'avait générée toute seule, agrafée à l'ajout d'une colonne de
    /// sécurité, dans une migration nommée « AddPasswordResetAttempts ». La
    /// suppression d'une table de moyens de paiement serait ainsi partie dans un
    /// correctif urgent, sous un nom qui ne l'annonçait pas. Elle est extraite
    /// pour qu'on la lise avant de l'appliquer, et pour qu'on puisse l'appliquer
    /// SÉPARÉMENT — le correctif de sécurité ne doit pas attendre une vérification
    /// de reprise, et la reprise ne doit pas être bousculée par une urgence.
    ///
    /// L'attribut <c>[Migration]</c> est porté ici plutôt que par un fichier
    /// Designer : le modèle cible est identique à celui de la migration
    /// précédente, un second snapshot n'apporterait rien.
    ///
    /// IRRÉVERSIBLE EN PRATIQUE. Le retour arrière recrée la table VIDE : les
    /// lignes, elles, ne reviennent pas. C'est pourquoi le garde-fou ci-dessous
    /// existe.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(IdentityDbContext))]
    [Migration("20260811180000_DropIdentityPaymentMethods")]
    public partial class DropIdentityPaymentMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─────────────────────────────────────────────────────────────────
            // GARDE-FOU : ON NE SUPPRIME PAS AVANT D'AVOIR VÉRIFIÉ LA REPRISE.
            //
            // Un DROP TABLE nu ferait confiance à une migration passée. Si
            // AdoptPaymentMethods avait été appliquée sur une base où
            // identity.payment_methods n'existait pas encore — ordre de
            // déploiement inhabituel, restauration partielle — la copie aurait
            // été vide, et cette suppression effacerait les moyens de paiement
            // de tous les acheteurs. Ils s'en apercevraient au passage en caisse
            // suivant, et la donnée serait irrécupérable.
            //
            // PostgreSQL exécute les migrations dans une transaction : si
            // l'exception est levée, RIEN n'est appliqué. Le déploiement échoue
            // bruyamment, ce qui est exactement ce qu'on veut.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    source_count bigint;
                    target_count bigint;
                BEGIN
                    IF to_regclass('identity.payment_methods') IS NULL THEN
                        -- Déjà supprimée : rien à faire.
                        RETURN;
                    END IF;

                    EXECUTE 'SELECT count(*) FROM identity.payment_methods' INTO source_count;

                    -- LA TABLE VIDE EST LE CAS DE LA BASE NEUVE, ET IL EST NORMAL.
                    --
                    -- Identity migre AVANT Payments : sur une base fraîche, la
                    -- table source vient d'être créée par InitialIdentity et
                    -- payments.payment_methods n'existe pas encore. Interroger la
                    -- cible échouait alors avec « relation does not exist » — et
                    -- faisait échouer TOUT le déploiement initial.
                    --
                    -- Une source vide n'a rien à reprendre : la suppression est
                    -- sans risque, et il n'y a aucune cible à comparer.
                    IF source_count = 0 THEN
                        RETURN;
                    END IF;

                    -- À partir d'ici la source contient des lignes : la cible DOIT
                    -- exister et les contenir. Son absence signifie que la reprise
                    -- n'a pas eu lieu.
                    IF to_regclass('payments.payment_methods') IS NULL THEN
                        RAISE EXCEPTION
                            'Reprise non effectuée : identity.payment_methods contient % ligne(s) et payments.payment_methods n''existe pas. Appliquez d''abord la migration AdoptPaymentMethods du module Payments.',
                            source_count;
                    END IF;

                    EXECUTE 'SELECT count(*) FROM payments.payment_methods' INTO target_count;

                    IF target_count < source_count THEN
                        RAISE EXCEPTION
                            'Reprise incomplète : identity.payment_methods contient % ligne(s), payments.payment_methods en contient %. Suppression annulée.',
                            source_count, target_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "identity");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Recrée la structure, PAS les données. Le retour arrière rend la base
        /// compatible avec l'ancien code ; il ne restaure pas les lignes, qui
        /// vivent désormais dans <c>payments.payment_methods</c> et y font
        /// autorité.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountRef = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiryMonth = table.Column<int>(type: "integer", nullable: true),
                    ExpiryYear = table.Column<int>(type: "integer", nullable: true),
                    HolderName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_methods", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_methods_UserId",
                schema: "identity",
                table: "payment_methods",
                column: "UserId");
        }
    }
}
