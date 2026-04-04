using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelSettings",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "text", nullable: false),
                    IsCommentAssistantEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsSuggestRepliesForTopLevelCommentsOnly = table.Column<bool>(type: "boolean", nullable: false),
                    MaxSuggestedRepliesPerSync = table.Column<int>(type: "integer", nullable: false),
                    MaxCommentAgeDays = table.Column<int>(type: "integer", nullable: false),
                    ReplyLanguage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResponseForNonTextualComments = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelSettings", x => x.ChannelId);
                    table.ForeignKey(
                        name: "FK_ChannelSettings_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "ChannelId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelSettings_ChannelId",
                table: "ChannelSettings",
                column: "ChannelId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelSettings");
        }
    }
}
