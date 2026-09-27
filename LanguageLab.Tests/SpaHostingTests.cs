using LanguageLab.Api;

namespace LanguageLab.Tests;

/// <summary>
/// How long a browser may keep what the SPA is served as. <c>index.html</c> names the hashed
/// bundle of one release; a WebView that keeps an old copy runs an old client against a newer
/// API — the Telegram Mini App on iOS did exactly that, and asked for an endpoint that was gone.
/// </summary>
public class SpaHostingTests
{
    [Theory]
    [InlineData("/index.html", "index.html")]
    // The fallback serves it under the SPA's own paths too.
    [InlineData("/some/spa/route", "index.html")]
    public void The_page_is_revalidated_on_every_load(string path, string file)
    {
        Assert.Equal("no-cache", SpaHosting.CacheControlFor(path, file));
    }

    [Fact]
    public void A_hashed_bundle_is_kept_for_good()
    {
        Assert.Equal(
            "public, max-age=31536000, immutable",
            SpaHosting.CacheControlFor("/assets/index-CzqrAbmK.js", "index-CzqrAbmK.js"));
    }

    [Theory]
    [InlineData("/favicon.svg", "favicon.svg")]
    [InlineData("/pronunciation-audio/th.mp3", "th.mp3")]
    public void Any_other_file_is_left_to_the_defaults(string path, string file)
    {
        Assert.Null(SpaHosting.CacheControlFor(path, file));
    }
}
