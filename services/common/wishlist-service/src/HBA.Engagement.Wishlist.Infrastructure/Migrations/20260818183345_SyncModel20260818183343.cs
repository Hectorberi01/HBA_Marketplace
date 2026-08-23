using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Engagement.Wishlist.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel20260818183343 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE wishlist.outbox_messages ADD COLUMN IF NOT EXISTS ""TraceParent"" character varying(64);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE wishlist.outbox_messages DROP COLUMN IF EXISTS ""TraceParent"";");
        }
    }
}
