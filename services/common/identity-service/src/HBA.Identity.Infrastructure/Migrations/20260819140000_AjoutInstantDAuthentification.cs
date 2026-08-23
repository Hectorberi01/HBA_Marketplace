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
    /// <c>identity.refresh_tokens</c> RETIENT DÉSORMAIS QUAND ET COMMENT LE
    /// TITULAIRE S'EST AUTHENTIFIÉ (§37, lot 0b).
    ///
    /// Deux colonnes, et la première est tout le mécanisme : <c>AuthenticatedAtUtc</c>
    /// porte l'instant de l'authentification EFFECTIVE, pas celui de l'émission du
    /// jeton. Elle traverse les rotations sans bouger, et alimente le claim OIDC
    /// <c>auth_time</c>. Sans elle, le claim se dériverait de la date d'émission —
    /// et un client qui rafraîchit toutes les quatre minutes resterait
    /// éternellement « fraîchement authentifié », ce qui viderait de son contenu
    /// l'exigence de mot de passe récent avant un virement.
    ///
    /// ÉCRITE À LA MAIN, comme les autres migrations de ce dépôt. L'attribut
    /// <c>[Migration]</c> est porté ici plutôt que par un fichier Designer ; le
    /// snapshot est mis à jour dans le même commit.
    ///
    /// LES LIGNES EXISTANTES SONT REMPLIES AVANT LE NOT NULL, PAS APRÈS.
    ///
    /// Les colonnes sont créées avec une valeur par défaut, puis la valeur par
    /// défaut est retirée. Ajouter directement une colonne NOT NULL sans défaut
    /// échouerait sur toute base portant au moins une session ouverte — c'est-à-dire
    /// sur la production. Le défaut retenu pour <c>AuthenticatedAtUtc</c> est
    /// <c>CreatedOnUtc</c> : c'est la meilleure approximation disponible, et elle
    /// est PESSIMISTE au bon sens du terme — une session vieille de trois jours
    /// hérite d'un instant vieux de trois jours, donc échouera le step-up et
    /// demandera une ressaisie. L'inverse (<c>now()</c>) aurait offert cinq minutes
    /// de fraîcheur imméritée à toutes les sessions en cours au moment du
    /// déploiement, virements compris.
    ///
    /// ET <c>AuthMethods</c> VAUT <c>pwd</c>, PAS <c>pwd otp</c>.
    ///
    /// On ne sait pas, rétroactivement, si le second facteur a joué pour une
    /// session donnée. Écrire <c>otp</c> par défaut ferait mentir le claim <c>amr</c>
    /// sur des sessions qui n'ont peut-être vu qu'un mot de passe — et un appelant
    /// qui exigerait <c>mfa</c> se croirait protégé par un facteur imaginaire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(IdentityDbContext))]
    [Migration("20260819140000_AjoutInstantDAuthentification")]
    public partial class AjoutInstantDAuthentification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AuthenticatedAtUtc",
                schema: "identity",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "AuthMethods",
                schema: "identity",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "pwd");

            // LA REPRISE EST PLUS FINE QUE LE DÉFAUT DE COLONNE.
            //
            // `defaultValueSql: now()` ne sert qu'à rendre l'ajout possible sur une
            // table peuplée. La vraie valeur est `CreatedOnUtc` : elle date la
            // session, là où `now()` daterait le DÉPLOIEMENT. Sans cette reprise,
            // toutes les sessions ouvertes au moment de la livraison passeraient le
            // step-up pendant cinq minutes sans que personne n'ait rien prouvé.
            migrationBuilder.Sql("""
                UPDATE identity.refresh_tokens
                   SET "AuthenticatedAtUtc" = "CreatedOnUtc";
                """);

            // LES DÉFAUTS SONT RETIRÉS : ils n'existaient que pour la reprise.
            //
            // Les laisser ferait qu'un INSERT oublieux — code futur, script de
            // maintenance — produirait silencieusement une session « authentifiée
            // maintenant, par mot de passe » qu'aucune preuve n'appuie. Le refus
            // de PostgreSQL est ici une information utile.
            migrationBuilder.Sql("""
                ALTER TABLE identity.refresh_tokens ALTER COLUMN "AuthenticatedAtUtc" DROP DEFAULT;
                ALTER TABLE identity.refresh_tokens ALTER COLUMN "AuthMethods" DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthMethods",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "AuthenticatedAtUtc",
                schema: "identity",
                table: "refresh_tokens");
        }
    }
}
