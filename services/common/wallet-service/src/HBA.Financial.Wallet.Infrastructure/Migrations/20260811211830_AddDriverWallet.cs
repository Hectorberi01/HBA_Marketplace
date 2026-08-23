using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "driver_wallets",
                schema: "settlement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AvailableBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    LifetimeEarned = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_wallets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_wallet_transactions_driver_earning",
                schema: "settlement",
                table: "wallet_transactions",
                columns: new[] { "ReferenceType", "ReferenceId" },
                unique: true,
                filter: "\"ReferenceType\" = 'driver_earning'");

            migrationBuilder.CreateIndex(
                name: "IX_driver_wallets_DriverId",
                schema: "settlement",
                table: "driver_wallets",
                column: "DriverId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "driver_wallets",
                schema: "settlement");

            migrationBuilder.DropIndex(
                name: "ux_wallet_transactions_driver_earning",
                schema: "settlement",
                table: "wallet_transactions");
        }
    }
}
