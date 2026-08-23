using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HBA.Food.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialFood : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "food");

            migrationBuilder.CreateTable(
                name: "menu_items",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BasePriceAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BasePriceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AvailabilityIsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    AvailabilityUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "menus",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "restaurants",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FulfillmentLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreparationMinutes = table.Column<int>(type: "integer", nullable: false),
                    PausedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "menu_option_groups",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    MinSelections = table.Column<int>(type: "integer", nullable: false),
                    MaxSelections = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_option_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_option_groups_menu_items_MenuItemId",
                        column: x => x.MenuItemId,
                        principalSchema: "food",
                        principalTable: "menu_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_service_hours",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Day = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_service_hours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_restaurant_service_hours_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalSchema: "food",
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_options",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PriceDelta = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AvailabilityIsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    AvailabilityUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OptionGroupId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_options_menu_option_groups_OptionGroupId",
                        column: x => x.OptionGroupId,
                        principalSchema: "food",
                        principalTable: "menu_option_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_RestaurantId_MenuId_DisplayOrder",
                schema: "food",
                table: "menu_items",
                columns: new[] { "RestaurantId", "MenuId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_option_groups_MenuItemId",
                schema: "food",
                table: "menu_option_groups",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_options_OptionGroupId",
                schema: "food",
                table: "menu_options",
                column: "OptionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_menus_RestaurantId_DisplayOrder",
                schema: "food",
                table: "menus",
                columns: new[] { "RestaurantId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "food",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "food",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_service_hours_RestaurantId_Day",
                schema: "food",
                table: "restaurant_service_hours",
                columns: new[] { "RestaurantId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_restaurants_OwnerUserId",
                schema: "food",
                table: "restaurants",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_restaurants_Status",
                schema: "food",
                table: "restaurants",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "menu_options",
                schema: "food");

            migrationBuilder.DropTable(
                name: "menus",
                schema: "food");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "food");

            migrationBuilder.DropTable(
                name: "restaurant_service_hours",
                schema: "food");

            migrationBuilder.DropTable(
                name: "menu_option_groups",
                schema: "food");

            migrationBuilder.DropTable(
                name: "restaurants",
                schema: "food");

            migrationBuilder.DropTable(
                name: "menu_items",
                schema: "food");
        }
    }
}
