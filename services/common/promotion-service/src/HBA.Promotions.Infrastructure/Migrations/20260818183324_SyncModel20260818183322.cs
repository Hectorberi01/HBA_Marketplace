using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Promotions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel20260818183322 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE promotions.outbox_messages ADD COLUMN IF NOT EXISTS ""TraceParent"" character varying(64);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE promotions.outbox_messages DROP COLUMN IF EXISTS ""TraceParent"";");
        }
    }
}
