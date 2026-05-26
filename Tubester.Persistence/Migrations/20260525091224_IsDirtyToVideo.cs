using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IsDirtyToVideo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationDescription",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "Location_Latitude",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "Location_Longitude",
                table: "Videos");

            migrationBuilder.AddColumn<bool>(
                name: "IsDirty",
                table: "Videos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDirty",
                table: "Videos");

            migrationBuilder.AddColumn<string>(
                name: "LocationDescription",
                table: "Videos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Location_Latitude",
                table: "Videos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Location_Longitude",
                table: "Videos",
                type: "double precision",
                nullable: true);
        }
    }
}
