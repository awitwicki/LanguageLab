using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IrregularVerbProgram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IrregularVerbFormProgresses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Verb = table.Column<string>(type: "text", nullable: false),
                    Form = table.Column<int>(type: "integer", nullable: false),
                    Streak = table.Column<int>(type: "integer", nullable: false),
                    Correct = table.Column<int>(type: "integer", nullable: false),
                    Wrong = table.Column<int>(type: "integer", nullable: false),
                    LastAnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IrregularVerbFormProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IrregularVerbFormProgresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IrregularVerbFormProgresses_UserId_Verb_Form",
                table: "IrregularVerbFormProgresses",
                columns: new[] { "UserId", "Verb", "Form" },
                unique: true);

            // The seeded "Irregular verbs" dictionary (commit a9b273d) is replaced by the
            // program: its 68 "v1 – v2 – v3" triplet words were only ever in that dictionary.
            // Trainings reference a dictionary without a cascade, so they go first; their
            // questions cascade. Deleting the words cascades their shelf rows, WordProgresses,
            // TrainingQuestions and join rows; deleting the dictionary cascades its chapters.
            migrationBuilder.Sql("""
                DELETE FROM "Trainings"
                WHERE "DictionaryId" IN (
                    SELECT "Id" FROM "Dictionaries" WHERE "Name" = 'Irregular verbs' AND "OwnerId" IS NULL);

                DELETE FROM "Words"
                WHERE "Word" LIKE '% – % – %'
                  AND "Id" IN (
                    SELECT dw."WordPairId"
                    FROM "DictionaryWords" dw
                    JOIN "Dictionaries" d ON d."Id" = dw."DictionaryId"
                    WHERE d."Name" = 'Irregular verbs' AND d."OwnerId" IS NULL);

                DELETE FROM "Dictionaries" WHERE "Name" = 'Irregular verbs' AND "OwnerId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IrregularVerbFormProgresses");
        }
    }
}
