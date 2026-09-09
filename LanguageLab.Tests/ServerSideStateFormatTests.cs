using LanguageLab.Api.Auth;
using Microsoft.AspNetCore.Authentication;

namespace LanguageLab.Tests;

public class ServerSideStateFormatTests
{
    /// <summary>
    /// Telegram's authorize endpoint answers "state too long" above this, measured against
    /// https://oauth.telegram.org/auth: 256 characters pass, 257 do not.
    /// </summary>
    private const int TelegramStateLimit = 256;

    private static AuthenticationProperties SampleProperties()
    {
        var properties = new AuthenticationProperties { RedirectUri = "/" };

        // What the handler really puts in state: the correlation id and the PKCE verifier.
        properties.Items[".xsrf"] = "AtVvpKqbaSVmYyRLL0k0z9NjJhAKPePJmLTuJgtcgAA";
        properties.Items["code_verifier"] = "3vJgAtVvpKqbaSVmYyRLL0k0z9NjJhAKPePJmLTuJgs";

        return properties;
    }

    [Fact]
    public void Protect_returns_a_handle_short_enough_for_Telegram()
    {
        var format = new ServerSideStateFormat();

        var state = format.Protect(SampleProperties());

        Assert.InRange(state.Length, 1, TelegramStateLimit);
    }

    [Fact]
    public void Protect_then_Unprotect_round_trips_the_properties()
    {
        var format = new ServerSideStateFormat();
        var properties = SampleProperties();

        var restored = format.Unprotect(format.Protect(properties));

        Assert.Equal("/", restored!.RedirectUri);
        Assert.Equal(properties.Items[".xsrf"], restored.Items[".xsrf"]);
        Assert.Equal(properties.Items["code_verifier"], restored.Items["code_verifier"]);
    }

    [Fact]
    public void Protect_returns_a_different_handle_every_time()
    {
        var format = new ServerSideStateFormat();

        Assert.NotEqual(format.Protect(SampleProperties()), format.Protect(SampleProperties()));
    }

    [Fact]
    public void Unprotect_returns_null_for_a_handle_it_never_issued()
    {
        var format = new ServerSideStateFormat();

        Assert.Null(format.Unprotect("not-a-handle"));
        Assert.Null(format.Unprotect(null));
        Assert.Null(format.Unprotect(string.Empty));
    }

    /// <summary>One callback per handshake: a replayed callback must not find its state again.</summary>
    [Fact]
    public void Unprotect_consumes_the_handle()
    {
        var format = new ServerSideStateFormat();
        var state = format.Protect(SampleProperties());

        Assert.NotNull(format.Unprotect(state));
        Assert.Null(format.Unprotect(state));
    }

    [Fact]
    public void Unprotect_returns_null_once_the_handle_has_expired()
    {
        var time = new TestTimeProvider(DateTimeOffset.UnixEpoch);
        var format = new ServerSideStateFormat(time);
        var state = format.Protect(SampleProperties());

        time.UtcNow += ServerSideStateFormat.Lifetime + TimeSpan.FromSeconds(1);

        Assert.Null(format.Unprotect(state));
    }

    /// <summary>
    /// /api/auth/telegram/start needs no session, so anyone can ask for handles. Expired ones
    /// must not pile up in memory while nobody comes back to redeem them.
    /// </summary>
    [Fact]
    public void Expired_handles_are_swept_as_new_ones_are_issued()
    {
        var time = new TestTimeProvider(DateTimeOffset.UnixEpoch);
        var format = new ServerSideStateFormat(time);

        for (var i = 0; i < 50; i++)
        {
            format.Protect(SampleProperties());
        }

        Assert.Equal(50, format.PendingCount);

        time.UtcNow += ServerSideStateFormat.Lifetime + TimeSpan.FromSeconds(1);
        format.Protect(SampleProperties());

        Assert.Equal(1, format.PendingCount);
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
