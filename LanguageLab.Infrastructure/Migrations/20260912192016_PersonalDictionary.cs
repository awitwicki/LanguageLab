using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersonalDictionary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Words_Word",
                table: "Words");

            migrationBuilder.DropIndex(
                name: "IX_Dictionaries_OwnerId",
                table: "Dictionaries");

            migrationBuilder.AddColumn<long>(
                name: "OwnerId",
                table: "Words",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPersonal",
                table: "Dictionaries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Words_OwnerId",
                table: "Words",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Words_Word_OwnerId",
                table: "Words",
                columns: new[] { "Word", "OwnerId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Dictionaries_OwnerId_Personal",
                table: "Dictionaries",
                column: "OwnerId",
                unique: true,
                filter: "\"IsPersonal\"");

            migrationBuilder.AddForeignKey(
                name: "FK_Words_Users_OwnerId",
                table: "Words",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Words_Users_OwnerId",
                table: "Words");

            migrationBuilder.DropIndex(
                name: "IX_Words_OwnerId",
                table: "Words");

            migrationBuilder.DropIndex(
                name: "IX_Words_Word_OwnerId",
                table: "Words");

            migrationBuilder.DropIndex(
                name: "IX_Dictionaries_OwnerId_Personal",
                table: "Dictionaries");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Words");

            migrationBuilder.DropColumn(
                name: "IsPersonal",
                table: "Dictionaries");

            migrationBuilder.CreateIndex(
                name: "IX_Words_Word",
                table: "Words",
                column: "Word",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dictionaries_OwnerId",
                table: "Dictionaries",
                column: "OwnerId");
        }
    }
}
