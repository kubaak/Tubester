using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationConfiguration : Migration
    {
        private static readonly string[] columns = new[] { "Key", "Value", "ValueType", "Description", "IsSystem", "UpdatedAtUtc" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationConfigurations",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    ValueType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationConfigurations", x => x.Key);
                });

            // Seed default configurations
            var seedTimestamp = DateTime.UtcNow;

            migrationBuilder.InsertData(
                table: "ApplicationConfigurations",
                columns: columns,
                values: new object[,]
                {
                    { "Ai.Provider", "Ollama", "String", "Current AI provider used by Tubester", true, seedTimestamp },
                    { "Ai.Model", "qwen3:8b", "String", "Default AI model used by Tubester", true, seedTimestamp }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationConfigurations");
        }
    }
}
