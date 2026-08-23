using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Merchants.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKybRejectionReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KybRejectionReason",
                schema: "sellers",
                table: "sellers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KybRejectionReason",
                schema: "sellers",
                table: "sellers");
        }
    }
}
