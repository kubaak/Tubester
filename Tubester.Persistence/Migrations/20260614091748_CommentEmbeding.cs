using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Tubester.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommentEmbeding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<Vector>(
                name: "CommentEmbedding",
                table: "Replies",
                type: "vector(768)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CommentEmbeddingGeneratedAt",
                table: "Replies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommentEmbeddingModel",
                table: "Replies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommentEmbedding",
                table: "Replies");

            migrationBuilder.DropColumn(
                name: "CommentEmbeddingGeneratedAt",
                table: "Replies");

            migrationBuilder.DropColumn(
                name: "CommentEmbeddingModel",
                table: "Replies");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
