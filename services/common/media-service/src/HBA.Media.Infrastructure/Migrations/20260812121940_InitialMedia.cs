using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Media.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media");

            migrationBuilder.CreateTable(
                name: "media_assets",
                schema: "media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Bucket = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Extension = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Visibility = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "media",
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
                name: "media_variants",
                schema: "media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantType = table.Column<int>(type: "integer", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_media_variants_media_assets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalSchema: "media",
                        principalTable: "media_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_OwnerType_OwnerId",
                schema: "media",
                table: "media_assets",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_checksum",
                schema: "media",
                table: "media_assets",
                columns: new[] { "OwnerType", "OwnerId", "Checksum" });

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_deleted",
                schema: "media",
                table: "media_assets",
                column: "DeletedOnUtc",
                filter: "\"DeletedOnUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_assets_object_key",
                schema: "media",
                table: "media_assets",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_variants_MediaAssetId",
                schema: "media",
                table: "media_variants",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "media",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "media",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_variants",
                schema: "media");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "media");

            migrationBuilder.DropTable(
                name: "media_assets",
                schema: "media");
        }
    }
}
