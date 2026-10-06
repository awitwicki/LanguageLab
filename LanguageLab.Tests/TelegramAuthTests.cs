using System.Globalization;
using System.Security.Claims;
using LanguageLab.Api.Auth;
using LanguageLab.Api.Endpoints;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Tests.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LanguageLab.Tests;

public class PrincipalFactoryTests
{
    [Fact]
    public void Create_then_Read_round_trips_id_and_role()
    {
        var principal = PrincipalFactory.Create(42, UserRole.Admin, 0, DateTimeOffset.UtcNow);
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

    /// <summary>A refused sign-in is visible in the logs, with the reason and never the code or token.</summary>
    [Fact]
    public async Task A_failed_telegram_sign_in_is_logged_with_its_reason()
    {
        var logger = new ListLogger<object>();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(new SingleLoggerProvider(logger)));
        await using var provider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var context = new RemoteFailureContext(
            httpContext,
            new AuthenticationScheme(TelegramAuth.Scheme, null, typeof(OpenIdConnectHandler)),
            new OpenIdConnectOptions(),
            new InvalidOperationException("Correlation failed."));

        await TelegramAuth.OnRemoteFailureAsync(context);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Correlation failed."));
        Assert.Equal("/?error=login", httpContext.Response.Headers.Location);
    }

    private sealed class SingleLoggerProvider(ILogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose()
        {
        }
    }

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

        var principal = PrincipalFactory.Create(UserId, UserRole.User, 0, DateTimeOffset.UtcNow);

        var context = await ValidateAsync(db, principal);

        // RejectPrincipal() (called from inside SessionValidator.ValidateAsync) sets Principal
        // to null — that is the only externally observable effect of a rejection.
        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Missing_user_is_rejected()
    {
        await using var db = NewDb();

        var principal = PrincipalFactory.Create(UserId, UserRole.User, 0, DateTimeOffset.UtcNow);

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
        var principal = PrincipalFactory.Create(UserId, UserRole.User, 0, DateTimeOffset.UtcNow);

        var context = await ValidateAsync(db, principal);

        var updated = PrincipalFactory.Read(context.Principal);
        Assert.NotNull(updated);
        Assert.Equal(UserId, updated!.Id);
        Assert.Equal(UserRole.Admin, updated.Role);
        Assert.True(context.ShouldRenew);
    }

    /// <summary>
    /// The re-issued cookie keeps the session's version and original issue time: a new version
    /// would log the user out, a new issue time would reset the absolute lifetime on every promotion.
    /// </summary>
    [Fact]
    public async Task Role_change_keeps_the_session_stamp()
    {
        await using var db = NewDb();
        db.Users.Add(new TelegramUser
        {
            Id = UserId, TelegramUserId = 111, Role = UserRole.Admin, SessionVersion = 3, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var issued = DateTimeOffset.UtcNow.AddDays(-5);

        var context = await ValidateAsync(db, PrincipalFactory.Create(UserId, UserRole.User, 3, issued));

        Assert.Equal(new SessionStamp(3, DateTimeOffset.FromUnixTimeSeconds(issued.ToUnixTimeSeconds())),
            PrincipalFactory.ReadStamp(context.Principal));
    }

    /// <summary>Logout bumps the version: a copied cookie dies with the one the browser dropped.</summary>
    [Fact]
    public async Task A_session_from_before_a_logout_is_rejected()
    {
        await using var db = NewDb();
        db.Users.Add(new TelegramUser
        {
            Id = UserId, TelegramUserId = 111, Role = UserRole.User, SessionVersion = 4, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var context = await ValidateAsync(db, PrincipalFactory.Create(UserId, UserRole.User, 3, DateTimeOffset.UtcNow));

        Assert.Null(context.Principal);
    }

    /// <summary>The cookie slides while used; this caps it however active it stays.</summary>
    [Fact]
    public async Task A_session_past_its_absolute_lifetime_is_rejected()
    {
        await using var db = NewDb();
        db.Users.Add(new TelegramUser
        {
            Id = UserId, TelegramUserId = 111, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var issued = DateTimeOffset.UtcNow - SessionValidator.AbsoluteLifetime - TimeSpan.FromMinutes(1);

        var context = await ValidateAsync(db, PrincipalFactory.Create(UserId, UserRole.User, 0, issued));

        Assert.Null(context.Principal);
    }

    /// <summary>A cookie minted before session stamps existed is not a session.</summary>
    [Fact]
    public async Task A_cookie_without_a_stamp_is_rejected()
    {
        await using var db = NewDb();
        db.Users.Add(new TelegramUser
        {
            Id = UserId, TelegramUserId = 111, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var legacy = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, UserId.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Role, nameof(UserRole.User)),
            ],
            PrincipalFactory.Scheme));

        var context = await ValidateAsync(db, legacy);

        Assert.Null(context.Principal);
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

        var principal = PrincipalFactory.Create(UserId, UserRole.User, 0, DateTimeOffset.UtcNow);

        var context = await ValidateAsync(db, principal);

        Assert.Same(principal, context.Principal);
        Assert.False(context.ShouldRenew);
    }
}

public class TelegramLoginOptionsTests
{
    /// <summary>
    /// Program.cs skips registering the OIDC handler entirely when this is false. That is not
    /// a tidiness measure: AuthenticationMiddleware initialises every remote scheme on every
    /// request so it can claim its callback path, and OpenIdConnectOptions.Validate() throws
    /// on a blank ClientId — a handler registered without credentials turns every request in
    /// the app into a 500, not just the login one.
    /// </summary>
    [Theory]
    [InlineData("id", "secret", true)]
    [InlineData("", "secret", false)]
    [InlineData("id", "", false)]
    [InlineData("   ", "secret", false)]
    [InlineData("id", "   ", false)]
    [InlineData("", "", false)]
    public void IsConfigured_requires_both_halves(string clientId, string clientSecret, bool expected)
    {
        Assert.Equal(expected, new TelegramLoginOptions(clientId, clientSecret).IsConfigured);
    }
}
