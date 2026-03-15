using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Channels_UploadsPlaylistId",
                table: "Channels",
                column: "UploadsPlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_Videos_UploadsPlaylistId",
                table: "Videos",
                column: "UploadsPlaylistId");

            migrationBuilder.AddForeignKey(
                name: "FK_Videos_Channels_UploadsPlaylistId",
                table: "Videos",
                column: "UploadsPlaylistId",
                principalTable: "Channels",
                principalColumn: "UploadsPlaylistId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Videos_Channels_UploadsPlaylistId",
                table: "Videos");

            migrationBuilder.DropIndex(
                name: "IX_Videos_UploadsPlaylistId",
                table: "Videos");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Channels_UploadsPlaylistId",
                table: "Channels");
        }
    }
}
