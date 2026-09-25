using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChaptersAndExcludedWords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "UnknownWords",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "KnownWords",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())");

            migrationBuilder.AddColumn<int>(
                name: "Frequency",
                table: "DictionaryWords",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Chapters",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DictionaryId = table.Column<long>(type: "bigint", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    WordsCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chapters_Dictionaries_DictionaryId",
                        column: x => x.DictionaryId,
                        principalTable: "Dictionaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExcludedWords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    WordPairId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcludedWords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExcludedWords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExcludedWords_Words_WordPairId",
                        column: x => x.WordPairId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChapterWords",
                columns: table => new
                {
                    ChapterId = table.Column<long>(type: "bigint", nullable: false),
                    WordPairId = table.Column<long>(type: "bigint", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChapterWords", x => new { x.ChapterId, x.WordPairId });
                    table.ForeignKey(
                        name: "FK_ChapterWords_Chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "Chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChapterWords_Words_WordPairId",
                        column: x => x.WordPairId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnknownWords_UserId_CreatedAt",
                table: "UnknownWords",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KnownWords_UserId_CreatedAt",
                table: "KnownWords",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DictionaryWords_DictionaryId_Frequency",
                table: "DictionaryWords",
                columns: new[] { "DictionaryId", "Frequency" });

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_DictionaryId_Order",
                table: "Chapters",
                columns: new[] { "DictionaryId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChapterWords_WordPairId",
                table: "ChapterWords",
                column: "WordPairId");

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedWords_UserId_CreatedAt",
                table: "ExcludedWords",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedWords_UserId_WordPairId",
                table: "ExcludedWords",
                columns: new[] { "UserId", "WordPairId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedWords_WordPairId",
                table: "ExcludedWords",
                column: "WordPairId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChapterWords");

            migrationBuilder.DropTable(
                name: "ExcludedWords");

            migrationBuilder.DropTable(
                name: "Chapters");

            migrationBuilder.DropIndex(
                name: "IX_UnknownWords_UserId_CreatedAt",
                table: "UnknownWords");

            migrationBuilder.DropIndex(
                name: "IX_KnownWords_UserId_CreatedAt",
                table: "KnownWords");

            migrationBuilder.DropIndex(
                name: "IX_DictionaryWords_DictionaryId_Frequency",
                table: "DictionaryWords");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "UnknownWords");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "KnownWords");

            migrationBuilder.DropColumn(
                name: "Frequency",
                table: "DictionaryWords");
        }
    }
}
