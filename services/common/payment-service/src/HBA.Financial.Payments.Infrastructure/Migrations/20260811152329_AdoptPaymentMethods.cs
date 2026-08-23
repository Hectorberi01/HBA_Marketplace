using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Payments.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES MOYENS DE PAIEMENT REJOIGNENT PAYMENTS.
    ///
    /// Ils vivaient dans Identity. Un numéro Mobile Money ou une carte enregistrée
    /// ne participe à aucune décision d'accès : ce n'est pas de l'identité
    /// technique.
    ///
    /// POURQUOI PAYMENTS ET NON USER
    ///
    /// User aurait été le voisin naturel des adresses, et le déplacement aurait
    /// suivi le même patron. Mais le cahier écrit que le profil de User contient
    /// les « informations personnelles NON SENSIBLES » — un identifiant de compte
    /// Mobile Money n'en est pas une. Payments manipule déjà des secrets PSP et
    /// porte l'intégration qui débitera ces moyens : c'est là qu'ils sont chez eux.
    ///
    /// AUCUNE MIGRATION NE SUPPRIME <c>identity.payment_methods</c> POUR L'INSTANT.
    ///
    /// C'est une décision, pas un oubli — et elle vient d'une vérification qui a
    /// contredit ma première intention.
    ///
    /// Pour les adresses, la reprise exigeait que le module User migre AVANT
    /// Identity, et Program.cs avait été réordonné pour cela. Ici, l'inverse est
    /// vrai : Identity migre avant Payments dans la séquence existante. Réordonner
    /// pour reproduire le même schéma toucherait l'ordre de vingt-sept modules,
    /// pour un gain nul — une COPIE n'a pas besoin que la source vienne après elle,
    /// elle a besoin que la source EXISTE. Elle existe.
    ///
    /// La suppression fera donc l'objet d'une migration séparée côté Identity, à
    /// appliquer une fois la reprise vérifiée en production. Entre les deux, la
    /// donnée vit aux deux endroits : Payments fait autorité, Identity ne lit plus
    /// la sienne. C'est réversible à tout moment, et c'est le seul état dans lequel
    /// on peut s'arrêter sans risque.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class AdoptPaymentMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AccountRef = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExpiryMonth = table.Column<int>(type: "integer", nullable: true),
                    ExpiryYear = table.Column<int>(type: "integer", nullable: true),
                    HolderName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_methods", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_methods_UserId",
                schema: "payments",
                table: "payment_methods",
                column: "UserId");

            // ─────────────────────────────────────────────────────────────────
            // REPRISE.
            //
            // SANS CE BLOC, CHAQUE ACHETEUR RETROUVE UN CARNET DE PAIEMENT VIDE
            // au premier passage en caisse suivant le déploiement, et doit ressaisir
            // son numéro Mobile Money.
            //
            // LES IDENTIFIANTS SONT CONSERVÉS. Une application mobile qui garde en
            // mémoire le moyen de paiement choisi au dernier passage le retrouve.
            // Régénérer les clés aurait cassé chaque panier en cours.
            //
            // ON CONFLICT DO NOTHING rend le rejeu inoffensif : une migration
            // relancée sur une base déjà reprise ne duplique rien.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('identity.payment_methods') IS NOT NULL THEN
                        INSERT INTO payments.payment_methods (
                            "Id", "UserId", "Type", "Label", "Provider", "AccountRef",
                            "HolderName", "ExpiryMonth", "ExpiryYear", "IsDefault", "CreatedOnUtc")
                        SELECT
                            "Id", "UserId", "Type", "Label", "Provider", "AccountRef",
                            "HolderName", "ExpiryMonth", "ExpiryYear", "IsDefault", "CreatedOnUtc"
                        FROM identity.payment_methods
                        ON CONFLICT ("Id") DO NOTHING;
                    END IF;
                END $$;
                """);
        }

        /// <remarks>
        /// Le retour arrière supprime la table reprise. Il est SANS PERTE tant que
        /// la migration jumelle côté Identity — celle qui supprime l'ancienne table
        /// — n'a pas été appliquée : les données sont alors encore dans
        /// <c>identity.payment_methods</c>.
        ///
        /// Une fois les deux passées, revenir en arrière exige de défaire Identity
        /// EN PREMIER, qui rapatrie les lignes.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "payments");
        }
    }
}
