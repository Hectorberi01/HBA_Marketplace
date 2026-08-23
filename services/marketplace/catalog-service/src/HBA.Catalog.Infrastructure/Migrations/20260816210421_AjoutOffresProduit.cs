using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AjoutOffresProduit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_offers",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerPriceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ProviderFeeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BuyerPriceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BuyerPriceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PromotionalPriceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PromotionalPriceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PromotionEndsOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Fulfillment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ShipFromLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    HandlingTimeDays = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_offers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_offers_ProductId",
                schema: "catalog",
                table: "product_offers",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_offers_SellerId",
                schema: "catalog",
                table: "product_offers",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_product_offers_VariantId",
                schema: "catalog",
                table: "product_offers",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "ux_product_offers_store_variant",
                schema: "catalog",
                table: "product_offers",
                columns: new[] { "StoreId", "VariantId" },
                unique: true,
                filter: "\"Status\" <> 'Archived'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_offers",
                schema: "catalog");
        }
    }
}
