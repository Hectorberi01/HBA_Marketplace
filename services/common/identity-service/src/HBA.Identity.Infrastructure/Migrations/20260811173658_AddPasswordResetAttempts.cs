using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Identity.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// COMPTEUR D'ESSAIS SUR LA RÉINITIALISATION DE MOT DE PASSE.
    ///
    /// Le jeton est un code numérique à six chiffres valable une heure. Sans
    /// compteur persisté, un code faux ne coûtait rien : le compte de n'importe
    /// qui — administrateur compris — était atteignable par force brute depuis
    /// quelques centaines d'adresses IP.
    ///
    /// La colonne est PERSISTÉE et non tenue en mémoire : un compteur en mémoire
    /// repart à zéro à chaque redémarrage et n'est pas partagé entre les cinq
    /// hôtes, qu'un attaquant alternerait pour multiplier son quota.
    ///
    /// UN « DROP TABLE identity.payment_methods » A ÉTÉ RETIRÉ D'ICI.
    ///
    /// EF l'avait ajouté de lui-même : le snapshot déclarait encore l'entité que
    /// le modèle avait perdue lors du déplacement vers Payments. La suppression
    /// serait donc partie sous le nom « AddPasswordResetAttempts », dans un
    /// déploiement de sécurité urgent, et personne n'aurait relu.
    ///
    /// Elle vit désormais dans sa propre migration, avec un garde-fou qui vérifie
    /// que la reprise a bien eu lieu. Voir DropIdentityPaymentMethods.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class AddPasswordResetAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PasswordResetAttempts",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Le retour arrière perd les compteurs en cours. C'est sans
            // conséquence : ils portent sur des jetons valables une heure.
            migrationBuilder.DropColumn(
                name: "PasswordResetAttempts",
                schema: "identity",
                table: "users");
        }
    }
}
