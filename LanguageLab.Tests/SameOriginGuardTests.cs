using LanguageLab.Api.Auth;
using Microsoft.AspNetCore.Http;

namespace LanguageLab.Tests;

public class SameOriginGuardTests
{
    private static bool Allowed(string method, string path, string? site, string? origin = null) =>
        SameOriginGuard.IsAllowed(method, new PathString(path), site, origin, "https", new HostString("l.kodzuverse.com"));

    [Theory]
    [InlineData("POST", "same-origin")]
    [InlineData("GET", "same-origin")]
    [InlineData("GET", "none")]
    public void The_spa_itself_gets_through(string method, string site) =>
        Assert.True(Allowed(method, "/api/admin/users/5/ban", site));

    /// <summary>
    /// SameSite=Lax lets a sibling *.kodzuverse.com page post with the cookie, and lets any site
    /// navigate a signed-in user to a GET that does work (a reader lookup can call the model).
    /// </summary>
    [Theory]
    [InlineData("POST", "same-site")]
    [InlineData("POST", "cross-site")]
    [InlineData("GET", "cross-site")]
    [InlineData("GET", "same-site")]
    public void Another_site_or_subdomain_is_refused(string method, string site) =>
        Assert.False(Allowed(method, "/api/reader/words/x", site));

    /// <summary>Telegram redirects back from oauth.telegram.org — the one legitimate cross-site arrival.</summary>
    [Fact]
    public void The_oidc_callback_arrives_cross_site_and_must_pass() =>
        Assert.True(Allowed("GET", "/api/auth/telegram/callback", "cross-site"));

    [Fact]
    public void Spa_files_are_not_guarded() =>
        Assert.True(Allowed("GET", "/", "cross-site"));

    [Theory]
    [InlineData(null, true)]
    [InlineData("https://l.kodzuverse.com", true)]
    [InlineData("https://evil.kodzuverse.com", false)]
    public void Without_fetch_metadata_a_write_falls_back_to_origin(string? origin, bool allowed) =>
        Assert.Equal(allowed, Allowed("POST", "/api/auth/logout", null, origin));

    [Fact]
    public void Without_fetch_metadata_a_read_passes() =>
        Assert.True(Allowed("GET", "/api/auth/me", null, "https://evil.kodzuverse.com"));

    [Theory]
    [InlineData(true, "ll_session")]
    [InlineData(false, "__Host-ll_session")]
    public void The_cookie_is_host_locked_outside_development(bool development, string name) =>
        Assert.Equal(name, SessionCookie.NameFor(development));
}
