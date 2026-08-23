using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Identity.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE CARNET D'ADRESSES QUITTE IDENTITY.
    ///
    /// Identity répond à « qui peut se connecter ? ». Une adresse de livraison ne
    /// participe à aucune décision d'accès : elle décrit la personne, pas son droit
    /// d'entrer. Elle vit désormais dans le module User, schéma « users ».
    ///
    /// CETTE MIGRATION DÉTRUIT DES DONNÉES. ELLE SUPPOSE QUE LA REPRISE A EU LIEU.
    ///
    /// La copie est faite par <c>InitialUsers</c>, côté module User. Cette migration-ci
    /// doit donc être appliquée APRÈS elle. <c>Program.cs</c> migre Users avant
    /// Identity pour cette seule raison ; une application manuelle des migrations doit
    /// respecter le même ordre.
    ///
    /// La garde ci-dessous refuse de supprimer une table dont le contenu n'a pas été
    /// repris : mieux vaut un déploiement qui échoue bruyamment qu'un carnet d'adresses
    /// effacé en silence.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class MoveAddressesToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─────────────────────────────────────────────────────────────────────
            // FILET DE SÉCURITÉ : ON NE SUPPRIME QUE CE QUI A ÉTÉ REPRIS.
            //
            // Trois cas, trois comportements :
            //
            //  • users.addresses absente et identity.addresses non vide → ARRÊT. La
            //    migration Users n'a pas tourné ; poursuivre effacerait les carnets.
            //  • des lignes manquent à l'arrivée → ARRÊT, avec leur nombre.
            //  • tout est repris (ou il n'y avait rien) → on supprime.
            //
            // Une base neuve passe par le troisième cas : les deux tables sont vides
            // ou absentes, et le compte des manquantes vaut zéro.
            // ─────────────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    manquantes bigint;
                BEGIN
                    IF to_regclass('identity.addresses') IS NULL THEN
                        RETURN;
                    END IF;

                    IF to_regclass('users.addresses') IS NULL THEN
                        IF EXISTS (SELECT 1 FROM identity.addresses) THEN
                            RAISE EXCEPTION
                                'users.addresses est absente alors que identity.addresses contient des données : appliquez d''abord la migration InitialUsers du module User.';
                        END IF;
                        RETURN;
                    END IF;

                    SELECT count(*) INTO manquantes
                    FROM identity.addresses a
                    WHERE NOT EXISTS (SELECT 1 FROM users.addresses u WHERE u."Id" = a."Id");

                    IF manquantes > 0 THEN
                        RAISE EXCEPTION
                            '% adresse(s) absente(s) de users.addresses : la reprise est incomplète, suppression annulée.', manquantes;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "addresses",
                schema: "identity");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Le retour arrière recrée la table ET rapatrie les lignes depuis
        /// <c>users.addresses</c> : sans cela, revenir en arrière rendrait aux clients
        /// un carnet d'adresses vide, ce qui est pire qu'un échec de déploiement.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "addresses",
                schema: "identity",
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

            migrationBuilder.CreateIndex(
                name: "IX_addresses_CommuneCode",
                schema: "identity",
                table: "addresses",
                column: "CommuneCode");

            migrationBuilder.CreateIndex(
                name: "IX_addresses_UserId",
                schema: "identity",
                table: "addresses",
                column: "UserId");

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('users.addresses') IS NOT NULL THEN
                        INSERT INTO identity.addresses (
                            "Id", "UserId", "Label", "Recipient", "Phone", "CommuneCode",
                            "Quartier", "Landmark", "Line1", "CountryCode", "Latitude",
                            "Longitude", "IsDefault", "CreatedOnUtc")
                        SELECT
                            "Id", "UserId", "Label", "Recipient", "Phone", "CommuneCode",
                            "Quartier", "Landmark", "Line1", "CountryCode", "Latitude",
                            "Longitude", "IsDefault", "CreatedOnUtc"
                        FROM users.addresses
                        ON CONFLICT ("Id") DO NOTHING;
                    END IF;
                END $$;
                """);
        }
    }
}
