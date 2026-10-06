using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using LanguageLab.Api.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace LanguageLab.Api;

/// <summary>
/// Per-user daily caps on the endpoints a single account can make expensive for everybody:
/// importing books (which now costs a parse and background translation), asking the translation
/// provider, and bulk-adding personal words. Counted in requests, which is what the middleware
/// counts. Held in memory like ImportQuota and UncachedTranslationLimiter: a restart forgives everybody,
/// and no storage is introduced.
/// </summary>
public static class UserRateLimits
{
    public const string Translate = "translate";
    public const string BulkWords = "bulk-words";
    /// <summary>
    /// A loose ceiling on import *attempts*, independent of ImportQuota's 1-success/day: a wrong
    /// file, DRM or a non-English book is immediately retryable, but this is what stops a script
    /// from hammering the endpoint with garbage forever (final review finding, C4) — generous
    /// enough that a real user retrying a few times never sees it.
    /// </summary>
    public const string ImportAttempts = "import-attempts";

    /// <summary>
    /// The two anonymous ways into a session. There is no user to partition by yet, so the client
    /// address is the key — after UseForwardedHeaders, the real client behind the proxy.
    /// </summary>
    public const string AnonymousAuth = "anonymous-auth";

    /// <summary>
    /// Single-word writes: adding or correcting a personal word, and the reader's learn / known /
    /// ignore (known and ignore may create an untranslated shared row). Generous for a person,
    /// a ceiling for a script filling the database.
    /// </summary>
    public const string WordWrites = "word-writes";

    public const int TranslationsPerDay = 500;
    public const int BulkRequestsPerDay = 20;
    public const int ImportAttemptsPerDay = 20;
    public const int AnonymousAuthPerMinute = 30;
    public const int WordWritesPerDay = 2000;

    /// <summary>
    /// The signed-in user's id, or one shared anonymous bucket. A request that reaches a limited
    /// endpoint unauthenticated is on its way to a 401 anyway; it must not land in somebody
    /// else's partition and must not throw. PrincipalFactory.Read is the one place that knows
    /// how the cookie spells a user id — this does not parse claims itself.
    /// </summary>
    public static string PartitionKey(ClaimsPrincipal? user) =>
        PrincipalFactory.Read(user) is { } context ? $"user:{context.Id}" : "anonymous";

    /// <summary>
    /// The client's address, or its /64 for IPv6 — one subscriber is handed a whole /64, so a
    /// per-address bucket would give them as many buckets as they care to use.
    /// </summary>
    public static string IpPartitionKey(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;

        if (address is { IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }

        if (address is not { AddressFamily: System.Net.Sockets.AddressFamily.InterNetworkV6 })
        {
            return $"ip:{address}";
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"ip:{new System.Net.IPAddress(bytes)}/64";
    }

    /// <summary>
    /// Names the wait; without it a 429 is "try again when?". SlidingWindowRateLimiter's rejected
    /// lease lists "RETRY_AFTER" in MetadataNames but never actually attaches a value for it
    /// (probed against .NET 10, unlike TokenBucketRateLimiter, which does) — so this falls back to
    /// the full shared window. A segment-sized fallback undersells the wait badly for a low-permit
    /// policy like Import: with one permit, the slot it holds does not leave the window — and free
    /// up capacity — until nearly the whole day has passed, not one segment (1/24) into it. The
    /// full window is always a safe upper bound; it never tells a caller to retry too early.
    /// </summary>
    public static bool TryGetRetryAfter(RateLimitLease lease, out TimeSpan retryAfter)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out retryAfter))
        {
            return true;
        }

        retryAfter = TimeSpan.FromDays(1);
        return true;
    }

    /// <summary>Used by the policies below and, unchanged, by their tests — one window, one definition.</summary>
    public static PartitionedRateLimiter<string> CreateLimiter(int permits) =>
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetSlidingWindowLimiter(key, _ => Window(permits)));

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = (context, _) =>
        {
            if (TryGetRetryAfter(context.Lease, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            return ValueTask.CompletedTask;
        };

        Add(options, Translate, TranslationsPerDay);
        Add(options, BulkWords, BulkRequestsPerDay);
        Add(options, ImportAttempts, ImportAttemptsPerDay);
        Add(options, WordWrites, WordWritesPerDay);

        options.AddPolicy(AnonymousAuth, context =>
            RateLimitPartition.GetFixedWindowLimiter(IpPartitionKey(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AnonymousAuthPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    private static SlidingWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromDays(1),
        SegmentsPerWindow = 24,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static void Add(RateLimiterOptions options, string name, int permits) =>
        options.AddPolicy(name, context =>
            RateLimitPartition.GetSlidingWindowLimiter(PartitionKey(context.User), _ => Window(permits)));
}
