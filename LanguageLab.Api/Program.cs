using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageLab.Api;
using LanguageLab.Api.Auth;
using LanguageLab.Api.Endpoints;
using LanguageLab.Application.Services;
using LanguageLab.Application.Translation;
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

// Outside Development, Telegram is the only way in, so missing credentials are fatal:
// failing at startup beats failing at someone's first login. Development has DevLogin as a
// second way in, so there it is a warning — a local run should not need @BotFather secrets.
if (!telegram.IsConfigured)
{
    const string problem =
        "Telegram:ClientId and Telegram:ClientSecret are not set (Telegram__ClientId / " +
        "Telegram__ClientSecret in Docker). Get them from @BotFather → your bot → Login Widget.";

    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(problem);
    }

    // The logging pipeline is not built yet at this point in startup.
    Console.WriteLine($"warn: {problem} Telegram sign-in is disabled; use the local dev sign-in.");
}

builder.Services.AddSingleton(telegram);

var webApp = new TelegramWebAppOptions(builder.Configuration["Telegram:BotToken"] ?? string.Empty);

// The same rule as the OIDC credentials above: outside Development a Mini App sign-in that
// cannot work is a deployment mistake to fail on now, not at somebody's first open.
if (!webApp.IsConfigured)
{
    const string problem =
        "Telegram:BotToken is not set (Telegram__BotToken in Docker). It is the bot's token " +
        "from @BotFather, and the Mini App sign-in validates Telegram's launch parameters with it.";

    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(problem);
    }

    Console.WriteLine($"warn: {problem} Signing in from inside Telegram is disabled.");
}

builder.Services.AddSingleton(webApp);
builder.Services.AddSingleton<ServerSideStateFormat>();

var authentication = builder.Services
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
    });

// Registering this handler without credentials is not merely useless, it breaks the whole
// app: AuthenticationMiddleware initialises every remote scheme on every request so that it
// can claim its callback path, and OpenIdConnectOptions.Validate() throws on an empty
// ClientId — turning each request into a 500. Absent credentials mean absent handler, and
// /api/auth/telegram/start answers 503 instead of challenging a scheme that is not there.
if (telegram.IsConfigured)
{
    authentication.AddOpenIdConnect(TelegramAuth.Scheme, options =>
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

    // Telegram rejects a `state` longer than 256 characters, and the handler's default format
    // packs the whole AuthenticationProperties into ~410 — see ServerSideStateFormat. It is wired
    // up out here rather than inside AddOpenIdConnect so the store comes from DI as a singleton:
    // an options reload rebuilds the options object, and that must not drop handshakes in flight.
    builder.Services.AddOptions<OpenIdConnectOptions>(TelegramAuth.Scheme)
        .Configure<ServerSideStateFormat>((options, state) => options.StateDataFormat = state);
}

builder.Services.AddAuthorization(options =>
    options.AddPolicy("Admin", policy => policy.RequireClaim(ClaimTypes.Role, nameof(UserRole.Admin))));

builder.Services.AddScoped<BookImportService>();
builder.Services.AddScoped<WordSortingService>();
builder.Services.AddScoped<WordSelectionService>();
builder.Services.AddScoped<TrainingSessionService>();
builder.Services.AddScoped<DictionaryStatsService>();
builder.Services.AddScoped<LearningProgressService>();
builder.Services.AddScoped<DictionaryAccessService>();
builder.Services.AddScoped<ChapterStatsService>();
builder.Services.AddScoped<StarredChapterService>();
builder.Services.AddScoped<AdminUserService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<VerbProgressService>();
builder.Services.AddScoped<PronunciationProgressService>();
builder.Services.AddScoped<VerbSessionService>();

// MyMemory is keyless; the optional contact email only raises its daily quota.
builder.Services.Configure<TranslationOptions>(builder.Configuration.GetSection(TranslationOptions.SectionName));
builder.Services.AddHttpClient<ITranslator, MyMemoryTranslator>(client =>
{
    client.BaseAddress = new Uri(MyMemoryTranslator.BaseUrl);
    client.Timeout = MyMemoryTranslator.Timeout;
});
builder.Services.AddScoped<TranslationService>();
builder.Services.AddScoped<PersonalDictionaryService>();

builder.Services.AddRequestDecompression();

// SortStatus travels as a string ("known"), not a number: the JSON should be readable by eye.
// CamelCase is mandatory: without a naming policy serialization would produce "Known",
// while the client is typed against 'known' | 'unknown' | 'excluded'.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// A 6k-word book is 1-2 MB of JSON; Kestrel's default 30 MB leaves plenty of headroom,
// but an explicit limit beats a surprise on a big book.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64L * 1024 * 1024);

var app = builder.Build();

// The web app owns the schema: MigrateAsync was removed from the bot so that two
// entry points never try to migrate the same database at once.
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
app.MapChapterEndpoints();
app.MapSortingEndpoints();
app.MapTrainingEndpoints();
app.MapAdminEndpoints();
app.MapIrregularVerbEndpoints();
app.MapPronunciationEndpoints();
app.MapTranslationEndpoints();

// The SPA has its own routing: anything that is not /api and not a file gets index.html.
app.MapFallbackToFile("index.html");

app.Run();
