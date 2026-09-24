using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReaderBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TranslationOrigin",
                table: "Words",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "Dictionaries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReaderBooks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "text", nullable: false),
                    ChaptersCount = table.Column<int>(type: "integer", nullable: false),
                    ChapterIndex = table.Column<int>(type: "integer", nullable: false),
                    ParagraphIndex = table.Column<int>(type: "integer", nullable: false),
                    SentenceIndex = table.Column<int>(type: "integer", nullable: false),
                    Progress = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReaderBooks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dictionaries_FileHash",
                table: "Dictionaries",
                column: "FileHash");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderBooks_UserId_FileHash",
                table: "ReaderBooks",
                columns: new[] { "UserId", "FileHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReaderBooks");

            migrationBuilder.DropIndex(
                name: "IX_Dictionaries_FileHash",
                table: "Dictionaries");

            migrationBuilder.DropColumn(
                name: "TranslationOrigin",
                table: "Words");

            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "Dictionaries");
        }
    }
}
