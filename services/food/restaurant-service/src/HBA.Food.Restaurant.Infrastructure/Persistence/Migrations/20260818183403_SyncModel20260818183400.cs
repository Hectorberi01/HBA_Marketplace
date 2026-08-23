using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Food.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel20260818183400 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoPublicUrl",
                schema: "food",
                table: "restaurants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql(@"ALTER TABLE food.outbox_messages ADD COLUMN IF NOT EXISTS ""TraceParent"" character varying(64);");

            migrationBuilder.AddColumn<string>(
                name: "ImagePublicUrl",
                schema: "food",
                table: "menu_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoPublicUrl",
                schema: "food",
                table: "restaurants");

            migrationBuilder.Sql(@"ALTER TABLE food.outbox_messages DROP COLUMN IF EXISTS ""TraceParent"";");

            migrationBuilder.DropColumn(
                name: "ImagePublicUrl",
                schema: "food",
                table: "menu_items");
        }
    }
}
