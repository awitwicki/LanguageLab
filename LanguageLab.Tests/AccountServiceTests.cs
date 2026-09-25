using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class AccountServiceTests
{
    private const long AdminId = 1;
    private const long OtherAdminId = 2;
    private const long MemberId = 3;

    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<ApplicationDbContext> SeedAsync(bool secondAdmin = false)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.AddRange(
            new TelegramUser { Id = AdminId, TelegramUserId = 101, Role = UserRole.Admin, FirstName = "Ada", CreatedAt = Now },
            new TelegramUser
            {
                Id = OtherAdminId, TelegramUserId = 102, FirstName = "Bo", CreatedAt = Now.AddDays(1),
                Role = secondAdmin ? UserRole.Admin : UserRole.User,
            },
            new TelegramUser { Id = MemberId, TelegramUserId = 103, Role = UserRole.User, FirstName = "Cy", CreatedAt = Now.AddDays(2) });

        await db.SaveChangesAsync();

        return db;
    }

    [Fact]
    public async Task A_member_can_delete_their_own_account()
    {
        await using var db = await SeedAsync();

        var result = await new AccountService(db).DeleteOwnAsync(MemberId);

        Assert.Equal(AccountDeleteResult.Ok, result);
        Assert.Null(await db.Users.FirstOrDefaultAsync(u => u.Id == MemberId));
        Assert.Equal(2, await db.Users.CountAsync());
    }

    /// <summary>
    /// The one rule that outranks "it is your account": an instance must always keep an
    /// administrator, or nobody can promote anyone ever again.
    /// </summary>
    [Fact]
    public async Task The_last_admin_cannot_delete_themselves()
    {
        await using var db = await SeedAsync();

        var result = await new AccountService(db).DeleteOwnAsync(AdminId);

        Assert.Equal(AccountDeleteResult.LastAdmin, result);
        Assert.NotNull(await db.Users.FirstOrDefaultAsync(u => u.Id == AdminId));
    }

    [Fact]
    public async Task An_admin_can_delete_themselves_while_another_admin_remains()
    {
        await using var db = await SeedAsync(secondAdmin: true);

        var result = await new AccountService(db).DeleteOwnAsync(AdminId);

        Assert.Equal(AccountDeleteResult.Ok, result);
        Assert.Null(await db.Users.FirstOrDefaultAsync(u => u.Id == AdminId));
        Assert.Equal(UserRole.Admin, (await db.Users.FirstAsync(u => u.Id == OtherAdminId)).Role);
    }

    /// <summary>The user's own data goes with the row — nothing is left orphaned.</summary>
    [Fact]
    public async Task Deleting_the_account_takes_its_shelves_along()
    {
        await using var db = await SeedAsync();
        db.Words.Add(new WordPair { Id = 1, Word = "abide", Translation = "дотримуватися" });
        db.KnownWords.Add(new KnownWord { Id = 1, UserId = MemberId, WordPairId = 1, CreatedAt = Now });
        await db.SaveChangesAsync();

        await new AccountService(db).DeleteOwnAsync(MemberId);

        Assert.Equal(0, await db.KnownWords.CountAsync(k => k.UserId == MemberId));
    }

    [Fact]
    public async Task A_missing_user_reports_not_found()
    {
        await using var db = await SeedAsync();

        Assert.Equal(AccountDeleteResult.NotFound, await new AccountService(db).DeleteOwnAsync(999));
    }

    /// <summary>
    /// Imported books outlive their importer (the FK is SetNull), but the personal dictionary is
    /// nothing without its owner and must not linger as an invisible orphan.
    /// </summary>
    [Fact]
    public async Task Deleting_the_account_takes_the_personal_dictionary_but_not_imported_books()
    {
        await using var db = await SeedAsync();
        db.Dictionaries.AddRange(
            new Domain.Entities.Dictionary { Id = 1, Name = "My words", OwnerId = MemberId, PublicationStatus = PublicationStatus.Private, IsPersonal = true },
            new Domain.Entities.Dictionary { Id = 2, Name = "Wool", OwnerId = MemberId, PublicationStatus = PublicationStatus.Published });
        await db.SaveChangesAsync();

        await new AccountService(db).DeleteOwnAsync(MemberId);

        Assert.False(await db.Dictionaries.AnyAsync(d => d.Id == 1));
        Assert.True(await db.Dictionaries.AnyAsync(d => d.Id == 2));
    }
}
