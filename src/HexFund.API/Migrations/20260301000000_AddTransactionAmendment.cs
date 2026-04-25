using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HexFund.API.Migrations;

/// <inheritdoc />
public partial class AddTransactionAmendment : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Nullable because most rows are never amended.
        migrationBuilder.AddColumn<Guid>(
            name: "PredecessorTransactionId",
            table: "Transactions",
            type: "uuid",
            nullable: true,
            defaultValue: null);

        // Index covers the history query and FK lookup.
        migrationBuilder.CreateIndex(
            name: "IX_Transactions_PredecessorTransactionId",
            table: "Transactions",
            column: "PredecessorTransactionId");

        // SetNull so deleting a predecessor doesn't cascade to successors.
        migrationBuilder.AddForeignKey(
            name: "FK_Transactions_Transactions_PredecessorTransactionId",
            table: "Transactions",
            column: "PredecessorTransactionId",
            principalTable: "Transactions",
            principalColumn: "TransactionId",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Transactions_Transactions_PredecessorTransactionId",
            table: "Transactions");

        migrationBuilder.DropIndex(
            name: "IX_Transactions_PredecessorTransactionId",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "PredecessorTransactionId",
            table: "Transactions");
    }
}
