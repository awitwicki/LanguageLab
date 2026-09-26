using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSortingVisitAndTrainingChapter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trainings_UserId",
                table: "Trainings");

            migrationBuilder.AddColumn<long>(
                name: "ChapterId",
                table: "Trainings",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SortingVisits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    DictionaryId = table.Column<long>(type: "bigint", nullable: false),
                    ChapterId = table.Column<long>(type: "bigint", nullable: true),
                    LastSortedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SortingVisits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SortingVisits_Chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "Chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SortingVisits_Dictionaries_DictionaryId",
                        column: x => x.DictionaryId,
                        principalTable: "Dictionaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SortingVisits_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_ChapterId",
                table: "Trainings",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_UserId_FinishedAt",
                table: "Trainings",
                columns: new[] { "UserId", "FinishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SortingVisits_ChapterId",
                table: "SortingVisits",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_SortingVisits_DictionaryId",
                table: "SortingVisits",
                column: "DictionaryId");

            migrationBuilder.CreateIndex(
                name: "IX_SortingVisits_UserId_DictionaryId_ChapterId",
                table: "SortingVisits",
                columns: new[] { "UserId", "DictionaryId", "ChapterId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_SortingVisits_UserId_LastSortedAt",
                table: "SortingVisits",
                columns: new[] { "UserId", "LastSortedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Trainings_Chapters_ChapterId",
                table: "Trainings",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trainings_Chapters_ChapterId",
                table: "Trainings");

            migrationBuilder.DropTable(
                name: "SortingVisits");

            migrationBuilder.DropIndex(
                name: "IX_Trainings_ChapterId",
                table: "Trainings");

            migrationBuilder.DropIndex(
                name: "IX_Trainings_UserId_FinishedAt",
                table: "Trainings");

            migrationBuilder.DropColumn(
                name: "ChapterId",
                table: "Trainings");

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_UserId",
                table: "Trainings",
                column: "UserId");
        }
    }
}
