using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Food.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersonnelStationsCommandesSaturationEtExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MenuId",
                schema: "food",
                table: "menu_items",
                newName: "MenuCategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_menu_items_RestaurantId_MenuId_DisplayOrder",
                schema: "food",
                table: "menu_items",
                newName: "IX_menu_items_RestaurantId_MenuCategoryId_DisplayOrder");

            migrationBuilder.AddColumn<int>(
                name: "AcceptanceMode",
                schema: "food",
                table: "restaurants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "BlocksOrdersWhenSaturated",
                schema: "food",
                table: "restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaximumActiveOrders",
                schema: "food",
                table: "restaurants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumOrderAmount",
                schema: "food",
                table: "restaurants",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "food",
                table: "restaurants",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AvailableFrom",
                schema: "food",
                table: "menus",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AvailableUntil",
                schema: "food",
                table: "menus",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                schema: "food",
                table: "menus",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                schema: "food",
                table: "menus",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreparationMinutes",
                schema: "food",
                table: "menu_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreparationStationId",
                schema: "food",
                table: "menu_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "food",
                table: "menu_items",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "food_orders",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CustomerNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<int>(type: "integer", nullable: true),
                    RejectionComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadyAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickedUpAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EstimatedPreparationMinutes = table.Column<int>(type: "integer", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    WasAutoAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_food_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "menu_categories",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "preparation_stations",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preparation_stations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_special_hours",
                schema: "food",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_special_hours", x => new { x.RestaurantId, x.Date });
                    table.ForeignKey(
                        name: "FK_restaurant_special_hours_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalSchema: "food",
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_staff",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsFounder = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_staff", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "food_order_items",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameSnapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PreparationStationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreparationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FoodOrderId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_food_order_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_food_order_items_food_orders_FoodOrderId",
                        column: x => x.FoodOrderId,
                        principalSchema: "food",
                        principalTable: "food_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_staff_permissions",
                schema: "food",
                columns: table => new
                {
                    Permission = table.Column<int>(type: "integer", nullable: false),
                    RestaurantStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsGranted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_staff_permissions", x => new { x.RestaurantStaffId, x.Permission });
                    table.ForeignKey(
                        name: "FK_restaurant_staff_permissions_restaurant_staff_RestaurantSta~",
                        column: x => x.RestaurantStaffId,
                        principalSchema: "food",
                        principalTable: "restaurant_staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "food_order_item_options",
                schema: "food",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OptionName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PriceDelta = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    FoodOrderItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_food_order_item_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_food_order_item_options_food_order_items_FoodOrderItemId",
                        column: x => x.FoodOrderItemId,
                        principalSchema: "food",
                        principalTable: "food_order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_food_order_item_options_FoodOrderItemId",
                schema: "food",
                table: "food_order_item_options",
                column: "FoodOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_food_order_items_FoodOrderId",
                schema: "food",
                table: "food_order_items",
                column: "FoodOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_food_order_items_PreparationStationId",
                schema: "food",
                table: "food_order_items",
                column: "PreparationStationId");

            migrationBuilder.CreateIndex(
                name: "IX_food_orders_RestaurantId_Status",
                schema: "food",
                table: "food_orders",
                columns: new[] { "RestaurantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ux_food_orders_order",
                schema: "food",
                table: "food_orders",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_menu_categories_MenuId",
                schema: "food",
                table: "menu_categories",
                column: "MenuId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_categories_RestaurantId_DisplayOrder",
                schema: "food",
                table: "menu_categories",
                columns: new[] { "RestaurantId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "ux_preparation_stations_restaurant_code",
                schema: "food",
                table: "preparation_stations",
                columns: new[] { "RestaurantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_restaurant_staff_user",
                schema: "food",
                table: "restaurant_staff",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ux_restaurant_staff_restaurant_user",
                schema: "food",
                table: "restaurant_staff",
                columns: new[] { "RestaurantId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "food_order_item_options",
                schema: "food");

            migrationBuilder.DropTable(
                name: "menu_categories",
                schema: "food");

            migrationBuilder.DropTable(
                name: "preparation_stations",
                schema: "food");

            migrationBuilder.DropTable(
                name: "restaurant_special_hours",
                schema: "food");

            migrationBuilder.DropTable(
                name: "restaurant_staff_permissions",
                schema: "food");

            migrationBuilder.DropTable(
                name: "food_order_items",
                schema: "food");

            migrationBuilder.DropTable(
                name: "restaurant_staff",
                schema: "food");

            migrationBuilder.DropTable(
                name: "food_orders",
                schema: "food");

            migrationBuilder.DropColumn(
                name: "AcceptanceMode",
                schema: "food",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "BlocksOrdersWhenSaturated",
                schema: "food",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "MaximumActiveOrders",
                schema: "food",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "MinimumOrderAmount",
                schema: "food",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "food",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "AvailableFrom",
                schema: "food",
                table: "menus");

            migrationBuilder.DropColumn(
                name: "AvailableUntil",
                schema: "food",
                table: "menus");

            migrationBuilder.DropColumn(
                name: "EndTime",
                schema: "food",
                table: "menus");

            migrationBuilder.DropColumn(
                name: "StartTime",
                schema: "food",
                table: "menus");

            migrationBuilder.DropColumn(
                name: "PreparationMinutes",
                schema: "food",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "PreparationStationId",
                schema: "food",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "food",
                table: "menu_items");

            migrationBuilder.RenameColumn(
                name: "MenuCategoryId",
                schema: "food",
                table: "menu_items",
                newName: "MenuId");

            migrationBuilder.RenameIndex(
                name: "IX_menu_items_RestaurantId_MenuCategoryId_DisplayOrder",
                schema: "food",
                table: "menu_items",
                newName: "IX_menu_items_RestaurantId_MenuId_DisplayOrder");
        }
    }
}
