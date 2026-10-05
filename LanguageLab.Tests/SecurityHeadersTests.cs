using LanguageLab.Api;
using Microsoft.AspNetCore.Http;

namespace LanguageLab.Tests;

public class SecurityHeadersTests
{
    [Fact]
    public void Every_response_gets_the_browser_hardening_headers()
    {
        var headers = new HeaderDictionary();

        SecurityHeaders.Apply(headers);

        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("same-origin", headers["Referrer-Policy"]);
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, headers["Content-Security-Policy"]);
    }

    /// <summary>What the SPA loads: Telegram's Mini App bridge, and nothing framed but by Telegram Web.</summary>
    [Theory]
    [InlineData("script-src 'self' https://telegram.org;")]
    [InlineData("object-src 'none';")]
    [InlineData("frame-ancestors 'self' https://web.telegram.org")]
    public void The_policy_allows_only_what_the_spa_loads(string directive)
    {
        Assert.Contains(directive, SecurityHeaders.ContentSecurityPolicy);
    }
}
