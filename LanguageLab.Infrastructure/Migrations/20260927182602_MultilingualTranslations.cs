using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultilingualTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WordTranslations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WordPairId = table.Column<long>(type: "bigint", nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Origin = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WordTranslations_Words_WordPairId",
                        column: x => x.WordPairId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WordTranslations_WordPairId_Language",
                table: "WordTranslations",
                columns: new[] { "WordPairId", "Language" },
                unique: true);

            // Every translation that predates languages is Ukrainian — shared and personal alike.
            migrationBuilder.Sql("""
                INSERT INTO "WordTranslations" ("WordPairId", "Language", "Text", "Origin")
                SELECT "Id", 'uk', "Translation", "TranslationOrigin" FROM "Words" WHERE "Translation" <> ''
                """);

            migrationBuilder.DropColumn(
                name: "Translation",
                table: "Words");

            migrationBuilder.DropColumn(
                name: "TranslationOrigin",
                table: "Words");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramLanguageCode",
                table: "Users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Trainings",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "uk");

            // Everyone who signed up before languages existed was learning Ukrainian.
            migrationBuilder.Sql("""UPDATE "Users" SET "Language" = 'uk'""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Translation",
                table: "Words",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TranslationOrigin",
                table: "Words",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "Words" w SET "Translation" = t."Text", "TranslationOrigin" = t."Origin"
                FROM "WordTranslations" t WHERE t."WordPairId" = w."Id" AND t."Language" = 'uk'
                """);

            migrationBuilder.DropTable(
                name: "WordTranslations");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TelegramLanguageCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Trainings");
        }
    }
}
