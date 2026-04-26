using Microsoft.EntityFrameworkCore.Migrations;
using Tubester.Abstractions.Credits;
using Tubester.Persistence.Credits;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Videos"
                ALTER COLUMN "Visibility" TYPE character varying(10)
                USING CASE "Visibility"
                    WHEN 0 THEN 'Public'
                    WHEN 1 THEN 'Unlisted'
                    WHEN 2 THEN 'Private'
                    WHEN 3 THEN 'Scheduled'
                    ELSE 'Private'
                END;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "Videos"
                ALTER COLUMN "Visibility" SET NOT NULL;
                """);


            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Playlists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "Playlists",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Private");

            var seedTimestamp = DateTime.UtcNow;
            migrationBuilder.InsertData(
                table: "ActionCosts",
                columns: new[] { "ActionType", "Cost", "IsEnabled", "UpdatedAtUtc", "Notes" },
                values: new object[,]
                {
                    { nameof(CreditActionType.AiTemplateWithPlaylistEnqueued), 2, true, seedTimestamp, "AI suggesting playlists for a video" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Playlists");

            migrationBuilder.Sql(
                """
                ALTER TABLE "Videos"
                ALTER COLUMN "Visibility" TYPE integer
                USING CASE "Visibility"
                      WHEN 'Public' THEN 0
                      WHEN 'Unlisted' THEN 1
                      WHEN 'Private' THEN 2
                      WHEN 'Scheduled' THEN 3
                      WHEN '0' THEN 0
                      WHEN '1' THEN 1
                      WHEN '2' THEN 2
                      WHEN '3' THEN 3
                      ELSE 0
                END;
                """);

            migrationBuilder.Sql(
                $"""
                 DELETE FROM "ActionCosts"
                 WHERE "ActionType" = '{nameof(CreditActionType.AiTemplateWithPlaylistEnqueued)}';
                 """);
        }
    }
}
