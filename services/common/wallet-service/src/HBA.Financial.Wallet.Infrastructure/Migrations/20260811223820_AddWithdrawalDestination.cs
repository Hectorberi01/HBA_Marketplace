using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayoutAccountName",
                schema: "settlement",
                table: "withdrawals",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutAccountNumber",
                schema: "settlement",
                table: "withdrawals",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutProvider",
                schema: "settlement",
                table: "withdrawals",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayoutAccountName",
                schema: "settlement",
                table: "withdrawals");

            migrationBuilder.DropColumn(
                name: "PayoutAccountNumber",
                schema: "settlement",
                table: "withdrawals");

            migrationBuilder.DropColumn(
                name: "PayoutProvider",
                schema: "settlement",
                table: "withdrawals");
        }
    }
}
