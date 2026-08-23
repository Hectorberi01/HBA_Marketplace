using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSpecificationGroupsProductSpecifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_specification_groups",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_specification_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_specification_groups_product_revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalSchema: "catalog",
                        principalTable: "product_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_specifications",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_specifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_specifications_product_specification_groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "catalog",
                        principalTable: "product_specification_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_specification_groups_RevisionId",
                schema: "catalog",
                table: "product_specification_groups",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_product_specifications_GroupId",
                schema: "catalog",
                table: "product_specifications",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_specifications",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_specification_groups",
                schema: "catalog");
        }
    }
}
