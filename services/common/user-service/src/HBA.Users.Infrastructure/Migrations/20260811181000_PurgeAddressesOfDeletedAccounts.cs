using HBA.Users.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Users.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// RATTRAPAGE : LES ADRESSES DES COMPTES DÉJÀ SUPPRIMÉS.
    ///
    /// CETTE MIGRATION RÉPARE UNE ERREUR DE LA MIGRATION <c>InitialUsers</c>.
    ///
    /// La reprise des adresses recopiait <c>identity.addresses</c> en entier, sans
    /// filtre. La migration jumelle du profil, écrite le même jour, excluait
    /// pourtant explicitement les comptes anonymisés
    /// (<c>WHERE "DeletedOnUtc" IS NULL</c>). Le filtre a été posé d'un côté et
    /// oublié de l'autre.
    ///
    /// Identity n'avait jamais effacé les adresses à l'anonymisation — c'est le
    /// trou qu'a comblé <c>PurgeUserDataOnAccountAnonymizedHandler</c>, mais
    /// celui-ci ne traite que les suppressions FUTURES : l'événement des comptes
    /// déjà supprimés a été consommé il y a longtemps.
    ///
    /// Conséquence : <c>users.addresses</c> contient aujourd'hui la commune, le
    /// quartier, le point de repère, le téléphone et les coordonnées GPS du
    /// domicile de personnes qui ont demandé la suppression de leur compte — dans
    /// un schéma tout neuf, où plus rien ne signale qu'elles auraient dû partir.
    ///
    /// ON SUPPRIME, ON N'ANONYMISE PAS.
    ///
    /// Identity conserve la ligne du compte parce que des commandes la
    /// référencent. Rien ne référence une adresse du carnet : la commande a figé
    /// sa propre adresse de livraison au moment de l'achat, et supprimer le carnet
    /// ne réécrit aucun bon de livraison.
    ///
    /// CETTE MIGRATION EST IRRÉVERSIBLE, ET C'EST NORMAL.
    ///
    /// Un <c>Down</c> qui restaurerait ces adresses irait rechercher des données
    /// personnelles que leur titulaire a demandé d'effacer. Le retour arrière ne
    /// fait donc rien — dit explicitement, plutôt que laissé vide par omission.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(UsersDbContext))]
    [Migration("20260811181000_PurgeAddressesOfDeletedAccounts")]
    public partial class PurgeAddressesOfDeletedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Le garde sur l'existence d'identity.users n'est pas décoratif : sur
            // une base neuve, le module User migre AVANT Identity. Il n'y a alors
            // ni compte supprimé ni adresse à purger — et c'est correct.
            //
            // La lecture cross-schéma est assumée ICI et nulle part ailleurs. Une
            // migration de rattrapage a besoin de la source pour savoir QUOI
            // rattraper ; le code applicatif, lui, n'a pas le droit de faire ce
            // qu'on fait dans ces trois lignes.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('identity.users') IS NOT NULL THEN
                        DELETE FROM users.addresses a
                        USING identity.users u
                        WHERE u."Id" = a."UserId"
                          AND u."DeletedOnUtc" IS NOT NULL;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// VOLONTAIREMENT VIDE. Restaurer ces adresses reviendrait à réintroduire
        /// des données personnelles dont l'effacement a été demandé — l'inverse
        /// exact de ce que cette migration accomplit.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
