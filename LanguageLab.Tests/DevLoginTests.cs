#if DEBUG
using LanguageLab.Api.Auth;
using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace LanguageLab.Tests;

/// <summary>
/// This whole file is <c>#if DEBUG</c> because <see cref="DevLogin"/> is: in a Release build —
/// the one the Dockerfile publishes — the type does not exist to be tested at all, and that
/// absence is exactly the guarantee these tests protect.
/// </summary>
public class DevLoginTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "LanguageLab.Api";
        public string ContentRootPath { get; set; } = string.Empty;

        // Never read: IsDevelopment() only compares EnvironmentName.
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// The second of the three fences. Production is what a container gets by default —
    /// compose.yaml sets no ASPNETCORE_ENVIRONMENT — so this is the case that matters most.
    /// </summary>
    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("production", false)]
    [InlineData("", false)]
    [InlineData("Development", true)]
    public void Enabled_only_in_the_development_environment(string environmentName, bool expected)
    {
        var environment = new Environment { EnvironmentName = environmentName };

        Assert.Equal(expected, DevLogin.IsEnabled(environment));
    }

    /// <summary>
    /// Real Telegram ids are nine or ten digits, so 1 cannot collide with an account anyone
    /// signs into for real — the dev account is always its own row.
    /// </summary>
    [Fact]
    public void Dev_identity_uses_a_telegram_id_no_real_account_can_have()
    {
        Assert.Equal(1, DevLogin.Identity.TelegramUserId);
    }

    /// <summary>
    /// The point of the feature: a checkout with an empty database signs in and gets a
    /// working account, without Telegram credentials and without a first real login.
    /// </summary>
    [Fact]
    public async Task Dev_login_on_an_empty_database_creates_admin_user_1()
    {
        await using var db = NewContext();

        var result = await new UserLoginService(db).LoginAsync(DevLogin.Identity, Now);

        Assert.Equal(LoginOutcome.SignedIn, result.Outcome);
        Assert.Equal(1, result.User.Id);
        Assert.Equal(UserRole.Admin, result.User.Role);
        Assert.Equal("Local Developer", result.User.DisplayName);
    }

    /// <summary>Signing in again must land on the same account, not pile up rows.</summary>
    [Fact]
    public async Task Repeated_dev_logins_reuse_the_same_account()
    {
        await using var db = NewContext();
        var service = new UserLoginService(db);

        var first = await service.LoginAsync(DevLogin.Identity, Now);
        var second = await service.LoginAsync(DevLogin.Identity, Now.AddDays(1));

        Assert.Equal(first.User.Id, second.User.Id);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    /// <summary>
    /// The dev path goes through the same UserLoginService as Telegram, so a ban applies to
    /// it too — it is a shortcut past the handshake, not past authorisation.
    /// </summary>
    [Fact]
    public async Task Banned_dev_account_is_refused_like_any_other()
    {
        await using var db = NewContext();
        db.Users.Add(new TelegramUser
        {
            Id = 1, TelegramUserId = DevLogin.Identity.TelegramUserId, CreatedAt = Now, IsBanned = true,
        });
        await db.SaveChangesAsync();

        var result = await new UserLoginService(db).LoginAsync(DevLogin.Identity, Now);

        Assert.Equal(LoginOutcome.Banned, result.Outcome);
    }
}
#endif
