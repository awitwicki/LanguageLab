using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrainingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trainings_Dictionaries_DictionaryId",
                table: "Trainings");

            migrationBuilder.DropForeignKey(
                name: "FK_Words_Dictionaries_DictionaryId",
                table: "Words");

            migrationBuilder.DropTable(
                name: "TrainingEvents");

            migrationBuilder.DropIndex(
                name: "IX_Words_DictionaryId",
                table: "Words");

            migrationBuilder.DropIndex(
                name: "IX_UnknownWords_UserId",
                table: "UnknownWords");

            migrationBuilder.DropIndex(
                name: "IX_KnownWords_UserId",
                table: "KnownWords");

            migrationBuilder.AlterColumn<long>(
                name: "DictionaryId",
                table: "Trainings",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<DateTime>(
                name: "FinishedAt",
                table: "Trainings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "Trainings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DictionaryWords",
                columns: table => new
                {
                    DictionaryId = table.Column<long>(type: "bigint", nullable: false),
                    WordPairId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DictionaryWords", x => new { x.DictionaryId, x.WordPairId });
                    table.ForeignKey(
                        name: "FK_DictionaryWords_Dictionaries_DictionaryId",
                        column: x => x.DictionaryId,
                        principalTable: "Dictionaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DictionaryWords_Words_WordPairId",
                        column: x => x.WordPairId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Переносимо наявні зв'язки словник→слово перед тим, як колонка зникне.
            // На порожній базі це no-op; на будь-якій іншій — рятує всі членства.
            migrationBuilder.Sql(
                """
                INSERT INTO "DictionaryWords" ("DictionaryId", "WordPairId")
                SELECT "DictionaryId", "Id" FROM "Words" WHERE "DictionaryId" IS NOT NULL
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.DropColumn(
                name: "DictionaryId",
                table: "Words");

            migrationBuilder.CreateTable(
                name: "TrainingQuestions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    WordPairId = table.Column<long>(type: "bigint", nullable: false),
                    TrainingId = table.Column<long>(type: "bigint", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    OptionIds = table.Column<List<long>>(type: "bigint[]", nullable: false),
                    PickedWordPairId = table.Column<long>(type: "bigint", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingQuestions_Trainings_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Trainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingQuestions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingQuestions_Words_WordPairId",
                        column: x => x.WordPairId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WordProgresses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    WordPairId = table.Column<long>(type: "bigint", nullable: false),
                    Box = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsLearned = table.Column<bool>(type: "boolean", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    WrongCount = table.Column<int>(type: "integer", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WordProgresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WordProgresses_Words_WordPairId",
                        column: x => x.WordPairId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnknownWords_UserId_WordPairId",
                table: "UnknownWords",
                columns: new[] { "UserId", "WordPairId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnownWords_UserId_WordPairId",
                table: "KnownWords",
                columns: new[] { "UserId", "WordPairId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DictionaryWords_WordPairId",
                table: "DictionaryWords",
                column: "WordPairId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingQuestions_TrainingId_Order",
                table: "TrainingQuestions",
                columns: new[] { "TrainingId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingQuestions_UserId",
                table: "TrainingQuestions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingQuestions_WordPairId",
                table: "TrainingQuestions",
                column: "WordPairId");

            migrationBuilder.CreateIndex(
                name: "IX_WordProgresses_UserId_IsLearned_DueAt",
                table: "WordProgresses",
                columns: new[] { "UserId", "IsLearned", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WordProgresses_UserId_WordPairId",
                table: "WordProgresses",
                columns: new[] { "UserId", "WordPairId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WordProgresses_WordPairId",
                table: "WordProgresses",
                column: "WordPairId");

            migrationBuilder.AddForeignKey(
                name: "FK_Trainings_Dictionaries_DictionaryId",
                table: "Trainings",
                column: "DictionaryId",
                principalTable: "Dictionaries",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trainings_Dictionaries_DictionaryId",
                table: "Trainings");

            migrationBuilder.DropTable(
                name: "DictionaryWords");

            migrationBuilder.DropTable(
                name: "TrainingQuestions");

            migrationBuilder.DropTable(
                name: "WordProgresses");

            migrationBuilder.DropIndex(
                name: "IX_UnknownWords_UserId_WordPairId",
                table: "UnknownWords");

            migrationBuilder.DropIndex(
                name: "IX_KnownWords_UserId_WordPairId",
                table: "KnownWords");

            migrationBuilder.DropColumn(
                name: "FinishedAt",
                table: "Trainings");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Trainings");

            migrationBuilder.AddColumn<long>(
                name: "DictionaryId",
                table: "Words",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<long>(
                name: "DictionaryId",
                table: "Trainings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "TrainingEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainingId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    WordPairId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingEvents_Trainings_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Trainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingEvents_Words_WordPairId",
                        column: x => x.WordPairId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Words_DictionaryId",
                table: "Words",
                column: "DictionaryId");

            migrationBuilder.CreateIndex(
                name: "IX_UnknownWords_UserId",
                table: "UnknownWords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_KnownWords_UserId",
                table: "KnownWords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingEvents_TrainingId",
                table: "TrainingEvents",
                column: "TrainingId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingEvents_UserId",
                table: "TrainingEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingEvents_WordPairId",
                table: "TrainingEvents",
                column: "WordPairId");

            migrationBuilder.AddForeignKey(
                name: "FK_Trainings_Dictionaries_DictionaryId",
                table: "Trainings",
                column: "DictionaryId",
                principalTable: "Dictionaries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Words_Dictionaries_DictionaryId",
                table: "Words",
                column: "DictionaryId",
                principalTable: "Dictionaries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
