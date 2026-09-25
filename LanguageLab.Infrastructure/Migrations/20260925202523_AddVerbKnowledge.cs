using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVerbKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VerbAnswers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Verb = table.Column<string>(type: "text", nullable: false),
                    PromptForm = table.Column<int>(type: "integer", nullable: false),
                    Known = table.Column<bool>(type: "boolean", nullable: false),
                    ResponseMs = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Group = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerbAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerbAnswers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerbKnowledges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Verb = table.Column<string>(type: "text", nullable: false),
                    Mastery = table.Column<double>(type: "double precision", nullable: false),
                    Streak = table.Column<int>(type: "integer", nullable: false),
                    Answers = table.Column<int>(type: "integer", nullable: false),
                    Knows = table.Column<int>(type: "integer", nullable: false),
                    LastAnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerbKnowledges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerbKnowledges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerbAnswers_UserId_CreatedAt",
                table: "VerbAnswers",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VerbKnowledges_UserId_Verb",
                table: "VerbKnowledges",
                columns: new[] { "UserId", "Verb" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VerbAnswers");

            migrationBuilder.DropTable(
                name: "VerbKnowledges");
        }
    }
}
