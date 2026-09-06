using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Order.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ZZVerificationRealignement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_PaymentId",
                schema: "ordering",
                table: "orders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_orders_PaymentId",
                schema: "ordering",
                table: "orders",
                column: "PaymentId");
        }
    }
}
