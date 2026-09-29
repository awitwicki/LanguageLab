using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VerbChoiceDrill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The drill changed from self-assessment to picking a form; standings built from
            // "I know" clicks do not mean the same thing, so everyone starts over.
            migrationBuilder.Sql("DELETE FROM \"VerbAnswers\";");
            migrationBuilder.Sql("DELETE FROM \"VerbKnowledges\";");

            migrationBuilder.AddColumn<string>(
                name: "Chosen",
                table: "VerbAnswers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only drops the column; the progress this migration deleted in Up is not restored.
            migrationBuilder.DropColumn(
                name: "Chosen",
                table: "VerbAnswers");
        }
    }
}
