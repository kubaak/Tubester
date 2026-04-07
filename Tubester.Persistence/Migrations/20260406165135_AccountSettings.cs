using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountSettings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PreferredTheme = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PreferredLanguage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AccountSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountSettings");
        }
    }
}
