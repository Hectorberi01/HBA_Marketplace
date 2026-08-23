using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Merchants.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel20260818192151 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuspendedFromStatus",
                schema: "sellers",
                table: "sellers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuspendedFromStatus",
                schema: "sellers",
                table: "sellers");
        }
    }
}
