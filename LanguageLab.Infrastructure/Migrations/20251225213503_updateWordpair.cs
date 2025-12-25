using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class updateWordpair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trainings_Dictionary_DictionaryId",
                table: "Trainings");

            migrationBuilder.DropForeignKey(
                name: "FK_Words_Dictionary_DictionaryId",
                table: "Words");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Dictionary",
                table: "Dictionary");

            migrationBuilder.RenameTable(
                name: "Dictionary",
                newName: "Dictionaries");

            migrationBuilder.AlterColumn<long>(
                name: "DictionaryId",
                table: "Words",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Dictionaries",
                table: "Dictionaries",
                column: "Id");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trainings_Dictionaries_DictionaryId",
                table: "Trainings");

            migrationBuilder.DropForeignKey(
                name: "FK_Words_Dictionaries_DictionaryId",
                table: "Words");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Dictionaries",
                table: "Dictionaries");

            migrationBuilder.RenameTable(
                name: "Dictionaries",
                newName: "Dictionary");

            migrationBuilder.AlterColumn<long>(
                name: "DictionaryId",
                table: "Words",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Dictionary",
                table: "Dictionary",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Trainings_Dictionary_DictionaryId",
                table: "Trainings",
                column: "DictionaryId",
                principalTable: "Dictionary",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Words_Dictionary_DictionaryId",
                table: "Words",
                column: "DictionaryId",
                principalTable: "Dictionary",
                principalColumn: "Id");
        }
    }
}
