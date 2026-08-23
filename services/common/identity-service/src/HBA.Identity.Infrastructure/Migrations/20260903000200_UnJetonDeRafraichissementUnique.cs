using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Identity.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX SESSIONS NE PEUVENT PLUS PARTAGER UN JETON (§5).
    ///
    /// `IX_refresh_tokens_TokenHash` existait, sans unicité. Rien n'empêchait donc
    /// deux lignes de même `TokenHash` : présenter ce jeton aurait rendu DEUX
    /// sessions, et la rotation n'en aurait révoqué qu'une — l'autre survivant à la
    /// déconnexion, et à la révocation qui suit un changement de mot de passe.
    ///
    /// CE N'EST PAS UNE PROTECTION CONTRE LE HASARD, ET C'EST POUR CELA QU'ELLE
    ///     NE COURT AUCUN RISQUE DE REPRISE.
    ///
    /// Le jeton vient de `RandomNumberGenerator.GetBytes(32)`, haché en SHA-256 :
    /// deux tirages identiques n'arriveront pas. Cette contrainte se pose contre un
    /// BUG — une régression du générateur, une insertion rejouée, une reprise de
    /// données maladroite. Ce sont exactement les cas où l'on veut que la base
    /// refuse au lieu de créer une session fantôme.
    ///
    /// S'il existait des doublons aujourd'hui, ce serait déjà l'incident, et cette
    /// migration serait le moyen de l'apprendre.
    ///
    /// L'INDEX EST REMPLACÉ, PAS COMPLÉTÉ. PostgreSQL ne sait pas rendre unique
    /// un index existant : il faut le retirer et le reposer.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Identity.Infrastructure.Persistence.IdentityDbContext))]
    [Migration("20260903000200_UnJetonDeRafraichissementUnique")]
    public partial class UnJetonDeRafraichissementUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_TokenHash",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                schema: "identity",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_TokenHash",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                schema: "identity",
                table: "refresh_tokens",
                column: "TokenHash");
        }
    }
}
