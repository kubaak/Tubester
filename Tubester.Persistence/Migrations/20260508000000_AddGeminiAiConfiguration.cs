using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGeminiAiConfiguration : Migration
    {
        private static readonly string[] columns =
            ["Key", "Value", "ValueType", "Description", "IsSystem", "UpdatedAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var seedTimestamp = DateTime.UtcNow;

            migrationBuilder.UpdateData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai.Provider",
                column: "Key",
                value: "Ai:Provider");
            
            migrationBuilder.UpdateData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:Provider",
                column: "Value",
                value: "Gemini");

            migrationBuilder.UpdateData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai.Model",
                column: "Key",
                value: "Ai:Model");
            
            migrationBuilder.UpdateData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:Model",
                column: "Value",
                value: "gemini-2.5-flash");

            migrationBuilder.InsertData(
                table: "ApplicationConfigurations",
                columns: columns,
                values: new object[,]
                {
                    { "Ai:PlaylistModel", "gemini-2.5-flash-lite", "String", "AI model used specifically for playlist suggestions.", true, seedTimestamp },
                    { "Ai:Temperature", "0.4", "Decimal", "Default AI generation temperature.", true, seedTimestamp },
                    { "Ai:PlaylistTemperature", "0.1", "Decimal", "AI generation temperature used for playlist suggestions.", true, seedTimestamp },
                    { "Ai:PlaylistNumCtx", "4096", "Integer", "Context window size used for playlist suggestions by local AI providers.", true, seedTimestamp },
                    { "Ai:DetailsMaxOutputTokens", "1200", "Integer", "Maximum output tokens for AI metadata generation.", true, seedTimestamp },
                    { "Ai:ReplyMaxOutputTokens", "300", "Integer", "Maximum output tokens for AI reply generation.", true, seedTimestamp },
                    { "Ai:PlaylistMaxOutputTokens", "600", "Integer", "Maximum output tokens for AI playlist suggestions.", true, seedTimestamp },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:Provider",
                column: "Key",
                value: "Ai.Provider");
            
            migrationBuilder.UpdateData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai.Provider",
                column: "Value",
                value: "Ollama");

            migrationBuilder.UpdateData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:Model",
                column: "Key",
                value: "Ai.Model");
            
            migrationBuilder.UpdateData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai.Model",
                column: "Value",
                value: "qwen3:8b");

            migrationBuilder.DeleteData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:PlaylistModel");

            migrationBuilder.DeleteData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:Temperature");

            migrationBuilder.DeleteData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:PlaylistTemperature");

            migrationBuilder.DeleteData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:PlaylistNumCtx");

            migrationBuilder.DeleteData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:DetailsMaxOutputTokens");

            migrationBuilder.DeleteData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:ReplyMaxOutputTokens");

            migrationBuilder.DeleteData(
                table: "ApplicationConfigurations",
                keyColumn: "Key",
                keyValue: "Ai:PlaylistMaxOutputTokens");
        }
    }
}