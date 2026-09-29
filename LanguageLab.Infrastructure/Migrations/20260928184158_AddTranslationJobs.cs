using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TranslationJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DictionaryId = table.Column<long>(type: "bigint", nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Done = table.Column<int>(type: "integer", nullable: false),
                    CursorFrequency = table.Column<int>(type: "integer", nullable: true),
                    CursorWordPairId = table.Column<long>(type: "bigint", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TranslationJobs_Dictionaries_DictionaryId",
                        column: x => x.DictionaryId,
                        principalTable: "Dictionaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranslationJobs_DictionaryId_Language",
                table: "TranslationJobs",
                columns: new[] { "DictionaryId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TranslationJobs_Status_LastProcessedAt",
                table: "TranslationJobs",
                columns: new[] { "Status", "LastProcessedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TranslationJobs");
        }
    }
}
