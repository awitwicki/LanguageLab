using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageLab.Api;
using LanguageLab.Api.Auth;
using LanguageLab.Api.Endpoints;
using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ClaimsCurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ClaimsCurrentUser>());
builder.Services.AddScoped<ICurrentUserContext>(sp => sp.GetRequiredService<ClaimsCurrentUser>());
builder.Services.AddScoped<UserLoginService>();

var telegram = new TelegramLoginOptions(
    builder.Configuration["Telegram:ClientId"] ?? string.Empty,
    builder.Configuration["Telegram:ClientSecret"] ?? string.Empty);

// Development signs in through the real provider too, so credentials are required
// everywhere. Failing at startup beats failing at someone's first login.
if (string.IsNullOrWhiteSpace(telegram.ClientId) || string.IsNullOrWhiteSpace(telegram.ClientSecret))
{
    throw new InvalidOperationException(
        "Telegram:ClientId and Telegram:ClientSecret must be set (Telegram__ClientId / " +
        "Telegram__ClientSecret in Docker). Get them from @BotFather → your bot → Login Widget.");
}

builder.Services.AddSingleton(telegram);

builder.Services
    .AddAuthentication(options =>
    {
        // Only DefaultScheme is set: an unauthenticated request to a RequireAuthorization
        // endpoint must fall back to the cookie handler and get a 401, not a redirect to
        // Telegram. /api/auth/telegram/start names the Telegram scheme explicitly instead.
        options.DefaultScheme = PrincipalFactory.Scheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "ll_session";
        options.Cookie.HttpOnly = true;

        // Lax is required, not merely chosen: the OIDC callback arrives as a cross-site
        // redirect, and Strict would withhold the cookie on it.
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;

        // This is an API, not a server-rendered site: answer with status codes instead of
        // redirecting to a login page that does not exist on the server.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        options.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;
    })
    .AddOpenIdConnect(TelegramAuth.Scheme, options =>
    {
        options.Authority = "https://oauth.telegram.org";
        options.ClientId = telegram.ClientId;
        options.ClientSecret = telegram.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;

        // The handler defaults to form_post, which makes the callback a cross-site POST —
        // and a cookie with an explicit SameSite=Lax is not sent on one, so the correlation
        // and nonce cookies below would never arrive. A query-mode callback is a top-level
        // GET, which Lax does allow.
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        options.CallbackPath = "/api/auth/telegram/callback";

        // The handshake's only job is to produce a principal for the cookie scheme.
        options.SignInScheme = PrincipalFactory.Scheme;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;

        // Keep Telegram's claim names as sent. Without this the framework renames them to
        // the long ClaimTypes URIs and TelegramAuth.ReadIdentity finds nothing.
        options.MapInboundClaims = false;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");

        // The handler's defaults for these are SameSite=None, which browsers only accept on
        // Secure cookies — that breaks the whole flow on http://localhost.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SameSite = SameSiteMode.Lax;

        if (builder.Environment.IsDevelopment())
        {
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        }

        options.Events.OnTokenValidated = TelegramAuth.OnTokenValidatedAsync;
        options.Events.OnRemoteFailure = TelegramAuth.OnRemoteFailureAsync;
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy("Admin", policy => policy.RequireClaim(ClaimTypes.Role, nameof(UserRole.Admin))));

builder.Services.AddScoped<BookImportService>();
builder.Services.AddScoped<WordSortingService>();
builder.Services.AddScoped<WordSelectionService>();
builder.Services.AddScoped<TrainingSessionService>();
builder.Services.AddScoped<DictionaryStatsService>();
builder.Services.AddScoped<LearningProgressService>();
builder.Services.AddScoped<DictionaryAccessService>();
builder.Services.AddScoped<AdminUserService>();

builder.Services.AddRequestDecompression();

// SortStatus їздить рядком («known»), а не числом: JSON має читатись очима.
// CamelCase обов'язковий: без політики іменування серіалізація дала б «Known»,
// а клієнт типізований під 'known' | 'unknown' | 'excluded'.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// Книжка на 6k слів — це 1-2 МБ JSON, дефолтні 30 МБ Kestrel лишаємо з запасом,
// але явний ліміт краще, ніж сюрприз на великій книжці.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64L * 1024 * 1024);

var app = builder.Build();

// Схему веде веб: MigrateAsync прибрано з бота, щоб дві точки входу
// не намагались мігрувати одну базу одночасно.
await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
}

// l.kodzuverse.com terminates TLS at a reverse proxy. Without this the app thinks every
// request is plain http, refuses to issue the Secure cookie, and builds an http redirect_uri
// that will not match the registered Allowed URL.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};

// KnownIPNetworks and KnownProxies default to loopback only, and a header from anywhere else
// is dropped in silence. kodzuverse_network is created outside this compose file
// (external: true) and this container publishes no port, so the proxy reaching it is always
// a sibling container at some bridge address — never loopback. Clearing both trusts anything
// on that Docker network to set these headers, which is acceptable only because nothing can
// reach this container except over that one internal network. Narrow this to the proxy's
// subnet if anything less trusted ever joins kodzuverse_network.
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeaders);

app.UseRequestDecompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapDictionaryEndpoints();
app.MapSortingEndpoints();
app.MapTrainingEndpoints();
app.MapAdminEndpoints();

// SPA має власний роутинг: усе, що не /api і не файл, віддаємо index.html.
app.MapFallbackToFile("index.html");

app.Run();
