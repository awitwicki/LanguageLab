using System.Security.Claims;
using System.Threading.RateLimiting;
using LanguageLab.Api.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace LanguageLab.Api;

/// <summary>
/// Per-user daily caps on the endpoints a single account can make expensive for everybody:
/// importing books, asking the translation provider, and bulk-adding personal words. Counted
/// in requests, which is what the middleware counts — characters are counted separately by
/// SentenceQuota and the MyMemory budgets. Held in memory like SentenceQuota: a restart
/// forgives everybody, and no storage is introduced.
/// </summary>
public static class UserRateLimits
{
    public const string Import = "import";
    public const string Translate = "translate";
    public const string BulkWords = "bulk-words";

    public const int ImportsPerDay = 20;
    public const int TranslationsPerDay = 500;
    public const int BulkRequestsPerDay = 20;

    /// <summary>
    /// The signed-in user's id, or one shared anonymous bucket. A request that reaches a limited
    /// endpoint unauthenticated is on its way to a 401 anyway; it must not land in somebody
    /// else's partition and must not throw. PrincipalFactory.Read is the one place that knows
    /// how the cookie spells a user id — this does not parse claims itself.
    /// </summary>
    public static string PartitionKey(ClaimsPrincipal? user) =>
        PrincipalFactory.Read(user) is { } context ? $"user:{context.Id}" : "anonymous";

    /// <summary>Used by the policies below and, unchanged, by their tests — one window, one definition.</summary>
    public static PartitionedRateLimiter<string> CreateLimiter(int permits) =>
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetSlidingWindowLimiter(key, _ => Window(permits)));

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        Add(options, Import, ImportsPerDay);
        Add(options, Translate, TranslationsPerDay);
        Add(options, BulkWords, BulkRequestsPerDay);
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
