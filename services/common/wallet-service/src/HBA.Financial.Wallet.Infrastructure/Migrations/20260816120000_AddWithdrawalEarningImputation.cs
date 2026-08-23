using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalEarningImputation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Le retrait qui a soldé un gain. NULLABLE, et pas seulement pour la
            // reprise : un gain soldé par un LOT n'a pas de retrait, et
            // réciproquement. Les gains existants prennent NULL — ils n'ont jamais
            // été imputés à un retrait, puisque le canal ne savait pas le faire.
            migrationBuilder.AddColumn<Guid>(
                name: "SettledByWithdrawalId",
                schema: "settlement",
                table: "seller_earnings",
                type: "uuid",
                nullable: true);

            // Sert la règle d'imputation : gains payables d'un vendeur, du plus
            // ancien au plus récent.
            migrationBuilder.CreateIndex(
                name: "IX_seller_earnings_SellerId_Status_ReleasedAtUtc",
                schema: "settlement",
                table: "seller_earnings",
                columns: new[] { "SellerId", "Status", "ReleasedAtUtc" });

            // Nom de colonne en PascalCase entre guillemets doubles : ce projet
            // n'applique AUCUNE convention snake_case. Écrire `settled_by_...`
            // produirait un index invalide, et l'erreur ne surgirait qu'ici.
            migrationBuilder.CreateIndex(
                name: "ix_seller_earnings_withdrawal",
                schema: "settlement",
                table: "seller_earnings",
                column: "SettledByWithdrawalId",
                filter: "\"SettledByWithdrawalId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_seller_earnings_SellerId_Status_ReleasedAtUtc",
                schema: "settlement",
                table: "seller_earnings");

            migrationBuilder.DropIndex(
                name: "ix_seller_earnings_withdrawal",
                schema: "settlement",
                table: "seller_earnings");

            migrationBuilder.DropColumn(
                name: "SettledByWithdrawalId",
                schema: "settlement",
                table: "seller_earnings");
        }
    }
}
