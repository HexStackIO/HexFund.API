using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HexFund.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEntraObjectIdToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EntraObjectId",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EntraObjectId",
                table: "Users");
        }
    }
}
