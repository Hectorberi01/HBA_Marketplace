using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ZZVerificationRealignement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_reservations_InventoryItemId",
                schema: "inventory",
                table: "stock_reservations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_InventoryItemId",
                schema: "inventory",
                table: "stock_reservations",
                column: "InventoryItemId");
        }
    }
}
