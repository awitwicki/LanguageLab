namespace LanguageLab.Api;

/// <summary>
/// Browser hardening on every response, the SPA's and the API's alike. The policy lists exactly
/// what the SPA loads: its own bundle, worker, lexicon and audio; Telegram's Mini App bridge
/// (index.html); Telegram avatars over https, from hosts that vary. Inline styles stay allowed —
/// the bridge injects some. Framing is limited to Telegram Web, the one host that embeds the app.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' https://telegram.org; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; " +
        "media-src 'self'; " +
        "connect-src 'self'; " +
        "worker-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'none'; " +
        "frame-ancestors 'self' https://web.telegram.org https://webk.telegram.org https://webz.telegram.org";

    public static void Apply(IHeaderDictionary headers)
    {
        headers["Content-Security-Policy"] = ContentSecurityPolicy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "same-origin";
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                Apply(context.Response.Headers);
                return Task.CompletedTask;
            });

            return next(context);
        });
}
