using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Plans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionCosts",
                columns: table => new
                {
                    ActionType = table.Column<string>(type: "text", nullable: false),
                    Cost = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionCosts", x => x.ActionType);
                });

            var seedTimestamp = DateTime.UtcNow;

            migrationBuilder.InsertData(
                table: "ActionCosts",
                columns: new[] { "ActionType", "Cost", "IsEnabled", "UpdatedAtUtc", "Notes" },
                values: new object[,]
                {
                    { "CopyTemplateExecuted", 1, true, seedTimestamp, "Copies template metadata between videos via YouTube API" },
                    { "AiTemplateEnqueued", 5, true, seedTimestamp, "Enqueues AI template generation job" },
                    { "AiTemplateSubmitted", 2, true, seedTimestamp, "Submits AI-generated template changes to YouTube" },
                    { "AiReplyGenerated", 3, true, seedTimestamp, "Generates an AI reply for a comment" },
                    { "ReplyPostedToYouTube", 1, true, seedTimestamp, "Posts a reply to YouTube" }
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActionType = table.Column<string>(type: "text", nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    ReferenceId = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    MonthlyCredits = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[] { "Id", "Code", "Name", "MonthlyCredits", "IsActive", "CreatedAtUtc", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 1, "free", "Free", 50, true, seedTimestamp, seedTimestamp },
                    { 2, "pro", "Pro", 999_999, true, seedTimestamp, seedTimestamp }
                });

            migrationBuilder.Sql("SELECT setval('\"Plans_Id_seq\"', (SELECT MAX(\"Id\") FROM \"Plans\"));");

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Balance = table.Column<int>(type: "integer", nullable: false),
                    PeriodStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Wallets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    PeriodStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionCosts_IsEnabled",
                table: "ActionCosts",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ActionType_OccurredAtUtc_Desc",
                table: "LedgerEntries",
                columns: new[] { "ActionType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_UserId_IdempotencyKey",
                table: "LedgerEntries",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_UserId_OccurredAtUtc_Desc",
                table: "LedgerEntries",
                columns: new[] { "UserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Code",
                table: "Plans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PlanId",
                table: "Subscriptions",
                column: "PlanId");

            migrationBuilder.Sql($@"
                INSERT INTO ""Subscriptions"" (""UserId"", ""PlanId"", ""PeriodStartUtc"", ""PeriodEndUtc"", ""Status"")
                SELECT ""Id"", 1, '{seedTimestamp:o}', '{seedTimestamp.AddDays(30):o}', 'Active'
                FROM ""Users""
                WHERE ""Id"" NOT IN (SELECT ""UserId"" FROM ""Subscriptions"");

                INSERT INTO ""Wallets"" (""UserId"", ""Balance"", ""PeriodStartUtc"", ""PeriodEndUtc"", ""UpdatedAtUtc"")
                SELECT ""Id"", 50, '{seedTimestamp:o}', '{seedTimestamp.AddDays(30):o}', '{seedTimestamp:o}'
                FROM ""Users""
                WHERE ""Id"" NOT IN (SELECT ""UserId"" FROM ""Wallets"");

                INSERT INTO ""LedgerEntries"" (""UserId"", ""OccurredAtUtc"", ""ActionType"", ""Delta"", ""IdempotencyKey"", ""ReferenceId"", ""MetadataJson"")
                SELECT ""Id"", '{seedTimestamp:o}', 'PeriodGrant', 50,
                       'migration_seed_free_plan_' || ""Id"", NULL,
                       json_build_object('type', 'period_grant', 'periodStartUtc', '{seedTimestamp:o}', 'periodEndUtc', '{seedTimestamp.AddDays(30):o}', 'periodCredits', 50)::text
                FROM ""Users""
                WHERE ""Id"" NOT IN (
                    SELECT ""UserId"" FROM ""LedgerEntries"" WHERE ""IdempotencyKey"" LIKE 'migration_seed_free_plan_%'
                );
            ");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Channels",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_UserId",
                table: "Channels",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_Users_UserId",
                table: "Channels",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Channels_Users_UserId",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Channels_UserId",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Channels");
            migrationBuilder.DropTable(
                name: "ActionCosts");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Wallets");

            migrationBuilder.DropTable(
                name: "Plans");
        }
    }
}
