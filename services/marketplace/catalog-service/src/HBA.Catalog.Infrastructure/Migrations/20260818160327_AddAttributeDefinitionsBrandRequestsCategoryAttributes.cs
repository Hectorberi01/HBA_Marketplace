using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttributeDefinitionsBrandRequestsCategoryAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attribute_definitions",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    options = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attribute_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "brand_requests",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brand_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "category_attributes",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    Variant = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category_attributes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attribute_definitions_Code",
                schema: "catalog",
                table: "attribute_definitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brand_requests_Status_RequestedAtUtc",
                schema: "catalog",
                table: "brand_requests",
                columns: new[] { "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_brand_requests_pending",
                schema: "catalog",
                table: "brand_requests",
                columns: new[] { "SellerId", "Name" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_category_attributes_CategoryId",
                schema: "catalog",
                table: "category_attributes",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_category_attributes_CategoryId_AttributeDefinitionId",
                schema: "catalog",
                table: "category_attributes",
                columns: new[] { "CategoryId", "AttributeDefinitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attribute_definitions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "brand_requests",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "category_attributes",
                schema: "catalog");
        }
    }
}
