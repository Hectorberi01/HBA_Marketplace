using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN DOSSIER DE LITIGE NE S'EFFACE PLUS PAR EFFET DE BORD (§8).
    ///
    /// SEPT CLÉS ÉTRANGÈRES EN CASCADE, SUR DEUX NIVEAUX.
    ///
    /// Un <c>DELETE FROM return_refund.return_requests WHERE …</c> mal ciblé emportait,
    /// sans une erreur ni une trace : les remboursements ET leurs tentatives PSP,
    /// les photos versées par le client, le rapport d'inspection, les expéditions,
    /// et l'historique des transitions. C'est-à-dire exactement ce qu'on relit
    /// quand quelqu'un conteste — et la seule version des faits que la plateforme
    /// détient, le client ayant la sienne.
    ///
    /// LES SEPT, ALORS QUE L'AUDIT N'EN NOMMAIT QUE DEUX.
    ///
    /// N'en protéger que deux produirait le pire des états : l'effacement
    /// échouerait sur `refunds` APRÈS avoir effacé photos, inspections et
    /// expéditions. La transaction serait annulée, certes — mais une protection
    /// par moitié ne tient que tant que l'effacement est transactionnel, et ce
    /// n'est pas une hypothèse à prendre sur une donnée de litige. Un dossier se
    /// protège entier ou pas du tout.
    ///
    /// AUCUNE REPRISE DE DONNÉES, AUCUN RISQUE.
    ///
    /// Changer le comportement d'une clé étrangère ne touche aucune ligne. Aucune
    /// donnée existante ne peut violer la nouvelle contrainte : toute ligne fille
    /// a déjà son parent, c'est ce que l'ancienne garantissait.
    ///
    /// CE QUE CETTE MIGRATION NE COUVRE PAS : un `DELETE` visant directement
    /// une table fille. Ce qui est fermé, c'est l'effacement INVISIBLE.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.ReturnRefundDbContext))]
    [Migration("20260902000100_DossierDeLitigeNonEffacable")]
    public partial class DossierDeLitigeNonEffacable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL NE SAIT PAS MODIFIER LE `ON DELETE` D'UNE CONTRAINTE
            // EXISTANTE : il faut la retirer et la reposer. C'est ce que fait EF
            // lui-même, et c'est pourquoi chaque lien apparaît deux fois.
            //
            // `refund_attempts` EN DERNIER, ET CE N'EST PAS DE L'ESTHÉTIQUE :
            // il pend à `refunds`, pas à `return_requests`. C'est le second niveau
            // de la chaîne, celui qui porte la référence PSP — le seul point de
            // rapprochement possible avec le relevé de l'opérateur.
            migrationBuilder.DropForeignKey(
                name: "FK_return_items_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_items");

            migrationBuilder.AddForeignKey(
                name: "FK_return_items_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_items",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_return_evidence_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_evidence");

            migrationBuilder.AddForeignKey(
                name: "FK_return_evidence_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_evidence",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_return_shipments_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_shipments");

            migrationBuilder.AddForeignKey(
                name: "FK_return_shipments_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_shipments",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_return_inspections_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_inspections");

            migrationBuilder.AddForeignKey(
                name: "FK_return_inspections_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_inspections",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_refunds_return_requests_ReturnId",
                schema: "return_refund",
                table: "refunds");

            migrationBuilder.AddForeignKey(
                name: "FK_refunds_return_requests_ReturnId",
                schema: "return_refund",
                table: "refunds",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_return_status_history_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_status_history");

            migrationBuilder.AddForeignKey(
                name: "FK_return_status_history_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_status_history",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_refund_attempts_refunds_RefundId",
                schema: "return_refund",
                table: "refund_attempts");

            migrationBuilder.AddForeignKey(
                name: "FK_refund_attempts_refunds_RefundId",
                schema: "return_refund",
                table: "refund_attempts",
                column: "RefundId",
                principalSchema: "return_refund",
                principalTable: "refunds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_return_items_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_items");

            migrationBuilder.AddForeignKey(
                name: "FK_return_items_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_items",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_return_evidence_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_evidence");

            migrationBuilder.AddForeignKey(
                name: "FK_return_evidence_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_evidence",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_return_shipments_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_shipments");

            migrationBuilder.AddForeignKey(
                name: "FK_return_shipments_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_shipments",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_return_inspections_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_inspections");

            migrationBuilder.AddForeignKey(
                name: "FK_return_inspections_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_inspections",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_refunds_return_requests_ReturnId",
                schema: "return_refund",
                table: "refunds");

            migrationBuilder.AddForeignKey(
                name: "FK_refunds_return_requests_ReturnId",
                schema: "return_refund",
                table: "refunds",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_return_status_history_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_status_history");

            migrationBuilder.AddForeignKey(
                name: "FK_return_status_history_return_requests_ReturnId",
                schema: "return_refund",
                table: "return_status_history",
                column: "ReturnId",
                principalSchema: "return_refund",
                principalTable: "return_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_refund_attempts_refunds_RefundId",
                schema: "return_refund",
                table: "refund_attempts");

            migrationBuilder.AddForeignKey(
                name: "FK_refund_attempts_refunds_RefundId",
                schema: "return_refund",
                table: "refund_attempts",
                column: "RefundId",
                principalSchema: "return_refund",
                principalTable: "refunds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
