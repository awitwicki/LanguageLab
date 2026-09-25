using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LanguageLab.Infrastructure.Migrations
{
    /// <summary>
    /// UserRole.Uploader (2) is gone: importing a book is open to every signed-in user, and the
    /// publication queue — not a role — is what keeps an unreviewed import private. Nothing in
    /// the schema changes, only the rows that still carry the dropped value; left behind, they
    /// would deserialize into an undefined enum value.
    /// </summary>
    public partial class DropUploaderRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE ""Users"" SET ""Role"" = 0 WHERE ""Role"" = 2;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Which accounts were uploaders is not recorded anywhere else, so the demotion
            // cannot be undone. Re-adding the enum value is all a revert can do.
        }
    }
}
