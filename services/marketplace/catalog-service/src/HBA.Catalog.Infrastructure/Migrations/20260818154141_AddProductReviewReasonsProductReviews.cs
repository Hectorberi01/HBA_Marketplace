using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductReviewReasonsProductReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_reviews",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionVersion = table.Column<int>(type: "integer", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_reviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_review_reasons",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_review_reasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_review_reasons_product_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "catalog",
                        principalTable: "product_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_review_reasons_ReviewId",
                schema: "catalog",
                table: "product_review_reasons",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_product_reviews_ProductId_ReviewedAtUtc",
                schema: "catalog",
                table: "product_reviews",
                columns: new[] { "ProductId", "ReviewedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_product_reviews_ReviewedBy",
                schema: "catalog",
                table: "product_reviews",
                column: "ReviewedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_review_reasons",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_reviews",
                schema: "catalog");
        }
    }
}
