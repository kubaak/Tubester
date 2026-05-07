using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceAiTemplateBooleansWithBitmask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add new integer column for bitmask with default 0
            migrationBuilder.AddColumn<int>(
                name: "AiOperationsInProgress",
                table: "Videos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Migrate existing data: if IsAiTemplateInProgress was true, set AiOperationsInProgress to 15
            // (which means Title | Description | Tags | PlaylistSuggestion = 1 | 2 | 4 | 8 = 15)
            // Since the old column was a single boolean representing all operations in progress,
            // any video with IsAiTemplateInProgress = true should have all flags set
            migrationBuilder.Sql(@"
                UPDATE ""Videos"" 
                SET ""AiOperationsInProgress"" = 15 
                WHERE ""IsAiTemplateInProgress"" = true;
            ");

            // Drop the old boolean column
            migrationBuilder.DropColumn(
                name: "IsAiTemplateInProgress",
                table: "Videos");

            migrationBuilder.Sql(
                """
                INSERT INTO "ActionCosts" ("ActionType", "Cost", "IsEnabled", "UpdatedAtUtc", "Notes")
                VALUES ('AiPlaylistSuggestionEnqueued', 5, true, NOW() AT TIME ZONE 'UTC', 'Enqueues Playlist suggestion job')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Add back the boolean column
            migrationBuilder.AddColumn<bool>(
                name: "IsAiTemplateInProgress",
                table: "Videos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Migrate data back: if any AI operation flag was set, set IsAiTemplateInProgress to true
            migrationBuilder.Sql(@"
                UPDATE ""Videos"" 
                SET ""IsAiTemplateInProgress"" = true 
                WHERE ""AiOperationsInProgress"" > 0;
            ");

            // Drop the bitmask column
            migrationBuilder.DropColumn(
                name: "AiOperationsInProgress",
                table: "Videos");

            migrationBuilder.Sql(
                $"""
                 DELETE FROM "ActionCosts"
                 WHERE "ActionType" = 'AiPlaylistSuggestionEnqueued';
                 """);
        }
    }
}