using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DictionaryPublicationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's default scaffold drops IsPublic before adding PublicationStatus, which would
            // lose every existing row's visibility. Reordered by hand: add the new column, move
            // the data across while both columns still exist, only then drop the old one.
            migrationBuilder.AddColumn<int>(
                name: "PublicationStatus",
                table: "Dictionaries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "Dictionaries" SET "PublicationStatus" = 2 WHERE "IsPublic";
                """);

            migrationBuilder.DropIndex(
                name: "IX_Dictionaries_IsPublic_OwnerId",
                table: "Dictionaries");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Dictionaries");

            migrationBuilder.CreateIndex(
                name: "IX_Dictionaries_PublicationStatus_OwnerId",
                table: "Dictionaries",
                columns: new[] { "PublicationStatus", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Dictionaries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE "Dictionaries" SET "IsPublic" = ("PublicationStatus" = 2);
                """);

            migrationBuilder.DropIndex(
                name: "IX_Dictionaries_PublicationStatus_OwnerId",
                table: "Dictionaries");

            migrationBuilder.DropColumn(
                name: "PublicationStatus",
                table: "Dictionaries");

            migrationBuilder.CreateIndex(
                name: "IX_Dictionaries_IsPublic_OwnerId",
                table: "Dictionaries",
                columns: new[] { "IsPublic", "OwnerId" });
        }
    }
}
