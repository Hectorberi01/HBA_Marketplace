using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Users.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE PROFIL QUITTE IDENTITY.
    ///
    /// Le cahier d'architecture sépare « qui peut se connecter ? » (Identity) de
    /// « qui est la personne ? » (User). Le prénom et le nom relèvent de la
    /// seconde : ils ne participent à aucune décision d'accès.
    ///
    /// LA CLÉ PRIMAIRE EST LE UserId D'IDENTITY, PAS UN NOUVEL IDENTIFIANT.
    ///
    /// C'est ce qui rend « deux profils pour un compte » impossible par
    /// construction, et permet à tout appelant tenant un UserId de lire sans
    /// jointure. La reprise ci-dessous en dépend directement.
    ///
    /// RIEN N'EST SUPPRIMÉ D'IDENTITY ICI.
    ///
    /// <c>identity.users</c> garde ses colonnes <c>FirstName</c> et
    /// <c>LastName</c> : dix-sept appelants lisent encore <c>UserSummary</c>. Tant
    /// qu'ils n'ont pas basculé, Identity fait autorité et User tient une copie
    /// tenue à jour par l'événement <c>UserProfileUpdated</c>. La suppression des
    /// colonnes fera l'objet d'une migration séparée, une fois la bascule faite.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class AddUserProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_profiles",
                schema: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profiles", x => x.Id);
                });

            // ─────────────────────────────────────────────────────────────────
            // REPRISE DES COMPTES EXISTANTS.
            //
            // SANS CE BLOC, AUCUN COMPTE DÉJÀ INSCRIT N'A DE PROFIL.
            //
            // Le profil n'est créé automatiquement qu'à l'INSCRIPTION, par le
            // handler qui écoute UserRegistered. Tous ceux qui existaient avant
            // ce déploiement n'ont jamais émis cet événement : sans reprise, la
            // table naîtrait vide et chaque lecture de profil répondrait
            // « introuvable » pour la totalité de la base.
            //
            // LE GARDE `to_regclass` N'EST PAS DE LA PRUDENCE DÉCORATIVE : sur une
            // base neuve, le module User migre AVANT Identity (l'ordre a été fixé
            // ainsi pour la reprise des adresses), donc identity.users n'existe pas
            // encore. Il n'y a alors rien à reprendre — et c'est correct.
            //
            // LES COMPTES SUPPRIMÉS SONT EXCLUS. Leur nom a été remplacé par
            // « Compte supprimé » lors de l'anonymisation ; les reprendre
            // ressusciterait des profils que la suppression avait effacés, et les
            // ferait réapparaître dans les listes d'administration.
            //
            // ON NE FIXE PAS UpdatedOnUtc. Cette colonne répond à « depuis quand ce
            // nom est-il celui-là ? ». Y écrire la date de la migration
            // prétendrait que chaque titulaire a changé de nom aujourd'hui.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('identity.users') IS NOT NULL THEN
                        INSERT INTO users.user_profiles ("Id", "FirstName", "LastName", "CreatedOnUtc")
                        SELECT "Id", "FirstName", "LastName", "CreatedOnUtc"
                        FROM identity.users
                        WHERE "DeletedOnUtc" IS NULL
                        ON CONFLICT ("Id") DO NOTHING;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_profiles",
                schema: "users");
        }
    }
}
