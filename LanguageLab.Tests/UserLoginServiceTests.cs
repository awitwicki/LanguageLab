using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class UserLoginServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TelegramIdentity Identity(long id, string? username = "andrew") =>
        new(id, "Andrew", null, username, "https://t.me/photo.jpg");

    /// <summary>The rule the whole admin story rests on.</summary>
    [Fact]
    public async Task First_login_becomes_the_admin()
    {
        await using var db = NewContext();
        var service = new UserLoginService(db);

        var result = await service.LoginAsync(Identity(777), Now);

        Assert.Equal(LoginOutcome.SignedIn, result.Outcome);
        Assert.Equal(UserRole.Admin, result.User.Role);
    }

    [Fact]
    public async Task Second_user_is_a_regular_user()
    {
        await using var db = NewContext();
        var service = new UserLoginService(db);

        await service.LoginAsync(Identity(777), Now);
        var second = await service.LoginAsync(Identity(888, "bob"), Now);

        Assert.Equal(UserRole.User, second.User.Role);
        Assert.Equal(2, await db.Users.CountAsync());
    }

    /// <summary>
    /// The row seeded by the old config-user path never logged in, so it must not
    /// consume the admin slot — the first person who actually signs in gets it.
    /// </summary>
    [Fact]
    public async Task A_pre_existing_row_that_never_logged_in_does_not_consume_the_admin_slot()
    {
        await using var db = NewContext();
        db.Users.Add(new TelegramUser { Id = 1, TelegramUserId = 777, CreatedAt = Now });
        await db.SaveChangesAsync();

        var result = await new UserLoginService(db).LoginAsync(Identity(777), Now);

        Assert.Equal(UserRole.Admin, result.User.Role);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    /// <summary>Signing in with the same Telegram id must land on the same account, shelves included.</summary>
    [Fact]
    public async Task Existing_telegram_id_reuses_the_row_and_keeps_its_data()
    {
        await using var db = NewContext();
        db.Users.Add(new TelegramUser { Id = 1, TelegramUserId = 777, CreatedAt = Now });
        db.Words.Add(new WordPair { Id = 1, Word = "abide", Translation = "дотримуватися" });
        db.KnownWords.Add(new KnownWord { Id = 1, UserId = 1, WordPairId = 1, CreatedAt = Now });
        await db.SaveChangesAsync();

        var result = await new UserLoginService(db).LoginAsync(Identity(777), Now);

        Assert.Equal(1, result.User.Id);
        Assert.Equal(1, await db.KnownWords.CountAsync(k => k.UserId == 1));
    }

    [Fact]
    public async Task Banned_user_is_rejected_and_not_touched()
    {
        await using var db = NewContext();
        db.Users.Add(new TelegramUser
        {
            Id = 1, TelegramUserId = 777, CreatedAt = Now, IsBanned = true, Username = "old",
        });
        await db.SaveChangesAsync();

        var result = await new UserLoginService(db).LoginAsync(Identity(777, "new"), Now);

        Assert.Equal(LoginOutcome.Banned, result.Outcome);
        Assert.Equal("old", (await db.Users.FirstAsync()).Username);
        Assert.Null((await db.Users.FirstAsync()).LastLoginAt);
    }

    [Fact]
    public async Task Profile_and_last_login_refresh_on_every_login()
    {
        await using var db = NewContext();
        var service = new UserLoginService(db);

        await service.LoginAsync(Identity(777, "old"), Now);
        var later = Now.AddDays(3);
        await service.LoginAsync(Identity(777, "new"), later);

        var user = await db.Users.FirstAsync();

        Assert.Equal("new", user.Username);
        Assert.Equal(later, user.LastLoginAt);
        Assert.Equal(Now, user.CreatedAt);
    }
}
