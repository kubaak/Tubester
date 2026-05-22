using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGranularAiActionCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Insert granular action costs for individual AI operations
            migrationBuilder.Sql(
                """
                INSERT INTO "ActionCosts" ("ActionType", "Cost", "IsEnabled", "UpdatedAtUtc", "Notes")
                VALUES 
                    ('AiTitleEnqueued', 2, true, NOW() AT TIME ZONE 'UTC', 'Enqueues AI title generation job'),
                    ('AiDescriptionEnqueued', 3, true, NOW() AT TIME ZONE 'UTC', 'Enqueues AI description generation job'),
                    ('AiTagsEnqueued', 2, true, NOW() AT TIME ZONE 'UTC', 'Enqueues AI tags generation job')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "ActionCosts"
                WHERE "ActionType" IN ('AiTitleEnqueued', 'AiDescriptionEnqueued', 'AiTagsEnqueued', 'AiPlaylistEnqueued');
                """);
        }
    }
}