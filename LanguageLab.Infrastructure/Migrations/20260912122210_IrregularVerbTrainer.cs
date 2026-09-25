using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IrregularVerbTrainer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IrregularVerbFormProgresses");

            migrationBuilder.CreateTable(
                name: "VerbProgresses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Verb = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Streak = table.Column<int>(type: "integer", nullable: false),
                    CleanSessions = table.Column<int>(type: "integer", nullable: false),
                    ErrorsV2 = table.Column<int>(type: "integer", nullable: false),
                    ErrorsV3 = table.Column<int>(type: "integer", nullable: false),
                    Ease = table.Column<double>(type: "double precision", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    NextReviewAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ManuallyFlaggedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerbProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerbProgresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerbSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Group = table.Column<int>(type: "integer", nullable: true),
                    Family = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Correct = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerbSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerbSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerbTasks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Verb = table.Column<string>(type: "text", nullable: false),
                    FormAsked = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    IsReturn = table.Column<bool>(type: "boolean", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    AnswerGiven = table.Column<string>(type: "text", nullable: true),
                    ErrorKind = table.Column<int>(type: "integer", nullable: true),
                    NeutralCount = table.Column<int>(type: "integer", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerbTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerbTasks_VerbSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "VerbSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerbAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Verb = table.Column<string>(type: "text", nullable: false),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    TaskId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    FormAsked = table.Column<int>(type: "integer", nullable: false),
                    AnswerGiven = table.Column<string>(type: "text", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    ErrorKind = table.Column<int>(type: "integer", nullable: true),
                    ResponseMs = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerbAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerbAttempts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VerbAttempts_VerbSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "VerbSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VerbAttempts_VerbTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "VerbTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerbAttempts_SessionId",
                table: "VerbAttempts",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_VerbAttempts_TaskId",
                table: "VerbAttempts",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_VerbAttempts_UserId_CreatedAt",
                table: "VerbAttempts",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VerbProgresses_UserId_Verb",
                table: "VerbProgresses",
                columns: new[] { "UserId", "Verb" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerbSessions_UserId_FinishedAt",
                table: "VerbSessions",
                columns: new[] { "UserId", "FinishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VerbTasks_SessionId_Order",
                table: "VerbTasks",
                columns: new[] { "SessionId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VerbAttempts");

            migrationBuilder.DropTable(
                name: "VerbProgresses");

            migrationBuilder.DropTable(
                name: "VerbTasks");

            migrationBuilder.DropTable(
                name: "VerbSessions");

            migrationBuilder.CreateTable(
                name: "IrregularVerbFormProgresses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Correct = table.Column<int>(type: "integer", nullable: false),
                    Form = table.Column<int>(type: "integer", nullable: false),
                    LastAnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Streak = table.Column<int>(type: "integer", nullable: false),
                    Verb = table.Column<string>(type: "text", nullable: false),
                    Wrong = table.Column<int>(type: "integer", nullable: false)
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
        }
    }
}
