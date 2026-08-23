using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Billing.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DÉTAIL D'UNE FACTURE NE S'EFFACE PLUS PAR EFFET DE BORD (§8).
    ///
    /// `FK_invoice_lines_invoices_InvoiceId` était en cascade : supprimer une
    /// facture emportait les lignes qui disent ce qui a été facturé et à quel titre.
    /// L'en-tête aurait survécu — mais une facture dont on ne peut plus expliquer le
    /// montant n'est plus une facture.
    ///
    /// VÉRIFIÉ AVANT DE TOUCHER. La configuration prévenait explicitement que
    /// « retirer le OnDelete, geste anodin en apparence, ferait RÉELLEMENT basculer
    /// en sévérance » — c'est-à-dire qu'une ligne retirée de la collection serait
    /// mise à NULL au lieu d'être supprimée. C'est vrai SANS `IsRequired()` ; il est
    /// posé, et le NOT NULL est en base, donc EF lève au lieu de sévrer. Et rien
    /// dans le dépôt ne retire une ligne d'une facture.
    ///
    /// AUCUNE REPRISE DE DONNÉES : changer le comportement d'une clé étrangère
    /// ne touche aucune ligne. Aucune donnée existante ne peut violer la nouvelle
    /// contrainte, puisque toute ligne fille a déjà son parent.
    ///
    /// LE JETON DE CONCURRENCE POSÉ SUR `invoices` DANS LE MÊME LOT N'EST PAS
    /// ICI, et ce n'est pas un oubli : il s'adosse à `xmin`, colonne SYSTÈME que
    /// chaque ligne PostgreSQL porte déjà. Aucune colonne n'est créée, donc il n'y a
    /// aucune DDL à écrire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Financial.Billing.Infrastructure.Persistence.BillingDbContext))]
    [Migration("20260902000300_DetailDeFactureNonEffacable")]
    public partial class DetailDeFactureNonEffacable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invoice_lines_invoices_InvoiceId",
                schema: "billing",
                table: "invoice_lines");

            migrationBuilder.AddForeignKey(
                name: "FK_invoice_lines_invoices_InvoiceId",
                schema: "billing",
                table: "invoice_lines",
                column: "InvoiceId",
                principalSchema: "billing",
                principalTable: "invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invoice_lines_invoices_InvoiceId",
                schema: "billing",
                table: "invoice_lines");

            migrationBuilder.AddForeignKey(
                name: "FK_invoice_lines_invoices_InvoiceId",
                schema: "billing",
                table: "invoice_lines",
                column: "InvoiceId",
                principalSchema: "billing",
                principalTable: "invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
