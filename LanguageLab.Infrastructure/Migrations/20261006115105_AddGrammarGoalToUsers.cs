using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGrammarGoalToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GrammarGoal",
                table: "Users",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GrammarGoal",
                table: "Users");
        }
    }
}
