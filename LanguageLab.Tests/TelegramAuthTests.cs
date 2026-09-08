using System.Security.Claims;
using LanguageLab.Api.Auth;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LanguageLab.Tests;

public class PrincipalFactoryTests
{
    [Fact]
    public void Create_then_Read_round_trips_id_and_role()
    {
        var principal = PrincipalFactory.Create(42, UserRole.Admin);
        var result = PrincipalFactory.Read(principal);

        Assert.Equal(42, result!.Id);
        Assert.Equal(UserRole.Admin, result.Role);
    }

    [Fact]
    public void Read_returns_null_for_an_anonymous_or_malformed_principal()
    {
        Assert.Null(PrincipalFactory.Read(null));
        Assert.Null(PrincipalFactory.Read(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}

public class TelegramAuthReadIdentityTests
{
    /// <summary>
    /// Telegram's login documentation (https://core.telegram.org/bots/telegram-login) publishes
    /// this exact sample decoded id_token. Claim types here are the literal short OIDC names —
    /// "id", "sub", "given_name" — the way the handler presents them with MapInboundClaims = false,
    /// not the long ClaimTypes.* URIs.
    /// </summary>
    private static ClaimsPrincipal TelegramSampleIdToken() => new(new ClaimsIdentity(
        [
            new Claim("iss", "https://oauth.telegram.org"),
            new Claim("aud", "123456789"),
            new Claim("sub", "1234123412341234123"),
            new Claim("iat", "1700000000"),
            new Claim("exp", "1700003600"),
            new Claim("id", "987654321"),
            new Claim("name", "John Doe"),
            new Claim("given_name", "John"),
            new Claim("family_name", "Doe"),
            new Claim("preferred_username", "johndoe"),
            new Claim("picture", "https://cdn4.telesco.pe/file..."),
            new Claim("phone_number", "971577777777"),
            new Claim("phone_number_verified", "true"),
        ]));

    [Fact]
    public void ReadIdentity_maps_the_id_claim_not_sub_and_the_rest_of_the_profile()
    {
        var identity = TelegramAuth.ReadIdentity(TelegramSampleIdToken());

        Assert.NotNull(identity);
        Assert.Equal(987654321, identity!.TelegramUserId);
        Assert.Equal("John", identity.FirstName);
        Assert.Equal("Doe", identity.LastName);
        Assert.Equal("johndoe", identity.Username);
        Assert.Equal("https://cdn4.telesco.pe/file...", identity.PhotoUrl);
    }

    [Fact]
    public void ReadIdentity_returns_null_when_the_id_claim_is_missing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "1234123412341234123")]));
        Assert.Null(TelegramAuth.ReadIdentity(principal));
    }

    [Fact]
    public void ReadIdentity_returns_null_when_the_id_claim_is_not_numeric()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("id", "not-a-number")]));
        Assert.Null(TelegramAuth.ReadIdentity(principal));
    }

    [Fact]
    public void ReadIdentity_returns_null_for_a_null_principal()
    {
        Assert.Null(TelegramAuth.ReadIdentity(null));
    }
}

public class SessionValidatorTests
{
    private const long UserId = 7;

    private static async Task<CookieValidatePrincipalContext> ValidateAsync(
        ApplicationDbContext db, ClaimsPrincipal principal)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(PrincipalFactory.Scheme).AddCookie(PrincipalFactory.Scheme);
        services.AddSingleton(db);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var scheme = new AuthenticationScheme(PrincipalFactory.Scheme, PrincipalFactory.Scheme, typeof(CookieAuthenticationHandler));
        var options = new CookieAuthenticationOptions();
        var ticket = new AuthenticationTicket(principal, PrincipalFactory.Scheme);

        var context = new CookieValidatePrincipalContext(httpContext, scheme, options, ticket);

        await SessionValidator.ValidateAsync(context);

        return context;
    }

    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Banned_user_is_rejected()
    {
        await using var db = NewDb();
        db.Users.Add(new TelegramUser
        {
            Id = UserId, TelegramUserId = 111, Role = UserRole.User, IsBanned = true, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var principal = PrincipalFactory.Create(UserId, UserRole.User);

        var context = await ValidateAsync(db, principal);

        // RejectPrincipal() (called from inside SessionValidator.ValidateAsync) sets Principal
        // to null — that is the only externally observable effect of a rejection.
        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Missing_user_is_rejected()
    {
        await using var db = NewDb();

        var principal = PrincipalFactory.Create(UserId, UserRole.User);

        var context = await ValidateAsync(db, principal);

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Role_change_in_the_database_replaces_the_principal_and_forces_a_renewal()
    {
        await using var db = NewDb();
        db.Users.Add(new TelegramUser
        {
            Id = UserId, TelegramUserId = 111, Role = UserRole.Admin, IsBanned = false, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        // The cookie still claims "User" — stale relative to a promotion that happened since sign-in.
        var principal = PrincipalFactory.Create(UserId, UserRole.User);

        var context = await ValidateAsync(db, principal);

        var updated = PrincipalFactory.Read(context.Principal);
        Assert.NotNull(updated);
        Assert.Equal(UserId, updated!.Id);
        Assert.Equal(UserRole.Admin, updated.Role);
        Assert.True(context.ShouldRenew);
    }

    [Fact]
    public async Task Unchanged_role_leaves_the_principal_alone()
    {
        await using var db = NewDb();
        db.Users.Add(new TelegramUser
        {
            Id = UserId, TelegramUserId = 111, Role = UserRole.User, IsBanned = false, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var principal = PrincipalFactory.Create(UserId, UserRole.User);

        var context = await ValidateAsync(db, principal);

        Assert.Same(principal, context.Principal);
        Assert.False(context.ShouldRenew);
    }
}
