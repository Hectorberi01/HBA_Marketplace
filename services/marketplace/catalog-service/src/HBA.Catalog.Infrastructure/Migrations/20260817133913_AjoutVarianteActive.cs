using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AjoutVarianteActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "catalog",
                table: "product_variants",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "catalog",
                table: "product_variants");
        }
    }
}
