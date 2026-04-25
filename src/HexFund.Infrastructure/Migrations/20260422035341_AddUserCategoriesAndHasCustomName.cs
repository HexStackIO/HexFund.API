using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HexFund.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCategoriesAndHasCustomName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasCustomName",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "UserCategories",
                columns: table => new
                {
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCategories", x => x.CategoryId);
                    table.ForeignKey(
                        name: "FK_UserCategories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCategories_UserId_Name",
                table: "UserCategories",
                columns: new[] { "UserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCategories");

            migrationBuilder.DropColumn(
                name: "HasCustomName",
                table: "Users");
        }
    }
}
