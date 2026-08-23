using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel20260818183338 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BalanceAfter",
                schema: "settlement",
                table: "wallet_transactions",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TransactionId",
                schema: "settlement",
                table: "wallet_transactions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.Sql(@"ALTER TABLE settlement.outbox_messages ADD COLUMN IF NOT EXISTS ""TraceParent"" character varying(64);");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_transaction",
                schema: "settlement",
                table: "wallet_transactions",
                column: "TransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wallet_transactions_transaction",
                schema: "settlement",
                table: "wallet_transactions");

            migrationBuilder.DropColumn(
                name: "BalanceAfter",
                schema: "settlement",
                table: "wallet_transactions");

            migrationBuilder.DropColumn(
                name: "TransactionId",
                schema: "settlement",
                table: "wallet_transactions");

            migrationBuilder.Sql(@"ALTER TABLE settlement.outbox_messages DROP COLUMN IF EXISTS ""TraceParent"";");
        }
    }
}
