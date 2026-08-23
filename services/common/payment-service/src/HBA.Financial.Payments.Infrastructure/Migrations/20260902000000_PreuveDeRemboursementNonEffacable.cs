using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Payments.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA PREUVE D'UN REMBOURSEMENT NE S'EFFACE PLUS PAR EFFET DE BORD (§8).
    ///
    /// `ON DELETE CASCADE` NE SE VOIT PAS DANS LE CODE : IL VIT DANS LA BASE.
    ///
    /// `FK_payment_refunds_payments_PaymentId` était en cascade. Un
    /// <c>DELETE FROM payments.payments WHERE …</c> mal ciblé — nettoyage de
    /// données de test, reprise, main qui glisse en psql — effaçait silencieusement
    /// les remboursements du paiement. Aucune erreur, aucune trace : la ligne
    /// disparaît, et avec elle la seule preuve que la plateforme a rendu l'argent.
    ///
    /// Le client, lui, garde son relevé Mobile Money. L'asymétrie est totale.
    ///
    /// CE QUE CETTE MIGRATION COÛTE, ET C'EST LE BUT.
    ///
    /// Un paiement portant des remboursements ne peut PLUS être supprimé. La base
    /// refuse, bruyamment, avec le nom de la contrainte. Sur une donnée comptable
    /// c'est le comportement correct : une purge légitime doit être une procédure
    /// délibérée qui dit ce qu'elle efface, pas un effet de bord d'autre chose.
    ///
    /// AUCUNE REPRISE DE DONNÉES, ET AUCUN RISQUE À L'APPLICATION.
    ///
    /// Changer le comportement d'une clé étrangère ne touche aucune ligne : il
    /// remplace une contrainte par une autre. Aucune donnée existante ne peut la
    /// violer, puisque toute ligne fille a déjà son parent — c'est ce que la
    /// contrainte d'origine garantissait déjà.
    ///
    /// CE QUE CETTE MIGRATION NE COUVRE PAS : un `DELETE FROM payment_refunds`
    /// direct. Rien ne protège une table de sa propre suppression ; ce qui est
    /// fermé ici, c'est l'effacement INVISIBLE, celui qu'on déclenche en croyant
    /// n'agir que sur le parent.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Financial.Payments.Infrastructure.Persistence.PaymentsDbContext))]
    [Migration("20260902000000_PreuveDeRemboursementNonEffacable")]
    public partial class PreuveDeRemboursementNonEffacable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL NE SAIT PAS MODIFIER LE `ON DELETE` D'UNE CONTRAINTE
            // EXISTANTE. Il faut la retirer et la reposer — c'est ce que fait EF
            // lui-même, et c'est pour cela que les deux gestes sont ici.
            migrationBuilder.DropForeignKey(
                name: "FK_payment_refunds_payments_PaymentId",
                schema: "payments",
                table: "payment_refunds");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_refunds_payments_PaymentId",
                schema: "payments",
                table: "payment_refunds",
                column: "PaymentId",
                principalSchema: "payments",
                principalTable: "payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_refunds_payments_PaymentId",
                schema: "payments",
                table: "payment_refunds");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_refunds_payments_PaymentId",
                schema: "payments",
                table: "payment_refunds",
                column: "PaymentId",
                principalSchema: "payments",
                principalTable: "payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
