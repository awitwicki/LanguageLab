using Microsoft.AspNetCore.StaticFiles;

namespace LanguageLab.Api;

/// <summary>
/// Serves the SPA out of <c>wwwroot</c>. Two things matter beyond handing out files:
/// <c>index.html</c> must never outlive a release, and a request for an API route that does not
/// exist must fail as an API request rather than come back as the page.
/// </summary>
/// <remarks>
/// <c>index.html</c> used to go out with a <c>Last-Modified</c> and no <c>Cache-Control</c>, which
/// lets a browser cache it heuristically — and the Telegram WebView on iOS did, for days. An old
/// page loads the old hashed bundle, which calls endpoints the new API has dropped; the fallback
/// answered those with the page itself, and the client failed on <c>&lt;!doctype</c> as JSON.
/// </remarks>
public static class SpaHosting
{
    private const string Page = "index.html";

    /// <summary>A year: Vite puts a content hash in every bundle file name, so one never changes.</summary>
    private const string Immutable = "public, max-age=31536000, immutable";

    public static void UseSpaFiles(this WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles(Options);
    }

    /// <summary>Maps last: whatever no endpoint and no file took.</summary>
    public static void MapSpaFallback(this WebApplication app)
    {
        // Lower precedence than every real /api route, higher than the page fallback below.
        app.Map("/api/{**rest}", () => Results.NotFound());

        // The SPA has its own routing: anything that is not /api and not a file gets the page.
        app.MapFallbackToFile(Page, Options);
    }

    /// <summary>The <c>Cache-Control</c> a served file goes out with, or null for the default.</summary>
    internal static string? CacheControlFor(string requestPath, string fileName)
    {
        if (fileName == Page)
        {
            // Stored, but checked against the server before every use — a 304 costs nothing.
            return "no-cache";
        }

        return requestPath.StartsWith("/assets/", StringComparison.Ordinal) ? Immutable : null;
    }

    private static StaticFileOptions Options { get; } = new()
    {
        OnPrepareResponse = context =>
        {
            var cacheControl = CacheControlFor(context.Context.Request.Path.Value ?? "", context.File.Name);

            if (cacheControl is not null)
            {
                context.Context.Response.Headers.CacheControl = cacheControl;
            }
        },
    };
}
