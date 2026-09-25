using LanguageLab.Api.Auth;
using LanguageLab.Application.Services;

namespace LanguageLab.Tests;

public class WebAppInitDataTests
{
    /// <summary>The sample token from Telegram's Bot API documentation — nobody's real one.</summary>
    private const string Token = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";

    /// <summary>
    /// A launch string signed for <see cref="Token"/> with Python's hmac module, not with the
    /// code under test, so this checks the algorithm against an independent implementation:
    /// secret = HMAC_SHA256(key "WebAppData", token); hash = HMAC_SHA256(secret, check string),
    /// the check string being every field but hash — `signature` included — sorted by key and
    /// joined with newlines.
    /// </summary>
    private const string Signed =
        "query_id=AAHdF6IQAAAAAN0XohDhrOrc" +
        "&user=%7B%22id%22%3A987654321%2C%22first_name%22%3A%22John%22%2C%22last_name%22%3A%22Doe%22" +
        "%2C%22username%22%3A%22johndoe%22%2C%22language_code%22%3A%22en%22%2C%22photo_url%22%3A" +
        "%22https%3A%2F%2Ft.me%2Fi%2Fuserpic%2F320%2Fjohndoe.jpg%22%7D" +
        "&auth_date=1758369600" +
        "&signature=abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijk" +
        "&hash=a7b3c5d47b105f7a8a629b0f7d1e0c3fc8d1b0cccc0464514cd52ca9ccc6b750";

    /// <summary>
    /// Signed the same way. Telegram guarantees only id and first_name, and a launch from the
    /// chat menu button carries no query_id.
    /// </summary>
    private const string Minimal =
        "user=%7B%22id%22%3A90431123%2C%22first_name%22%3A%22Andrii%22%7D&auth_date=1758369600" +
        "&hash=3a41ce34e414dbd6e931cb62a2cee8b74adbe274627af00d0c46f8117525aefa";

    /// <summary>Correctly signed, but the user has no id — a good signature is not enough.</summary>
    private const string NoId =
        "user=%7B%22first_name%22%3A%22Nobody%22%7D&auth_date=1758369600" +
        "&hash=94710823fd98f48a23646cb9334d2f7ef16c0c4cf4be5eebda382f6887fb8b09";

    private static readonly DateTimeOffset AuthDate = DateTimeOffset.FromUnixTimeSeconds(1758369600);
    private static readonly DateTimeOffset Now = AuthDate.AddMinutes(30);

    [Fact]
    public void Accepts_a_correctly_signed_launch_and_maps_the_user()
    {
        var result = WebAppInitData.Validate(Signed, Token, Now);

        Assert.Null(result.Error);
        var identity = Assert.IsType<TelegramIdentity>(result.Identity);
        Assert.Equal(987654321, identity.TelegramUserId);
        Assert.Equal("John", identity.FirstName);
        Assert.Equal("Doe", identity.LastName);
        Assert.Equal("johndoe", identity.Username);
        Assert.Equal("https://t.me/i/userpic/320/johndoe.jpg", identity.PhotoUrl);
    }

    [Fact]
    public void Optional_profile_fields_come_back_null()
    {
        var identity = WebAppInitData.Validate(Minimal, Token, Now).Identity;

        Assert.NotNull(identity);
        Assert.Equal(90431123, identity.TelegramUserId);
        Assert.Equal("Andrii", identity.FirstName);
        Assert.Null(identity.LastName);
        Assert.Null(identity.Username);
        Assert.Null(identity.PhotoUrl);
    }

    [Fact]
    public void Rejects_a_changed_field()
    {
        var tampered = Signed.Replace("johndoe", "janedoe");

        var result = WebAppInitData.Validate(tampered, Token, Now);

        Assert.Null(result.Identity);
        Assert.Contains("signature", result.Error);
    }

    [Fact]
    public void Rejects_a_hash_made_with_another_bots_token()
    {
        Assert.Null(WebAppInitData.Validate(Signed, "654321:other-token", Now).Identity);
    }

    [Fact]
    public void Rejects_a_launch_without_a_hash()
    {
        var unsigned = Signed[..Signed.IndexOf("&hash=", StringComparison.Ordinal)];

        Assert.Null(WebAppInitData.Validate(unsigned, Token, Now).Identity);
    }

    // 24 hours is Telegram's own default (@telegram-apps/init-data-node). A live cookie wins
    // over initData at boot, so the window only ever bites on a cold sign-in.
    [Fact]
    public void Rejects_a_launch_older_than_the_window()
    {
        Assert.NotNull(WebAppInitData.Validate(Signed, Token, AuthDate.AddHours(23)).Identity);

        var stale = WebAppInitData.Validate(Signed, Token, AuthDate.AddHours(24).AddMinutes(1));

        Assert.Null(stale.Identity);
        Assert.Contains("old", stale.Error);
    }

    [Fact]
    public void Rejects_a_user_without_an_id()
    {
        Assert.Null(WebAppInitData.Validate(NoId, Token, Now).Identity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a launch string")]
    [InlineData("hash=zz")]
    public void Rejects_input_that_is_not_a_launch_string(string? input)
    {
        var result = WebAppInitData.Validate(input, Token, Now);

        Assert.Null(result.Identity);
        Assert.NotNull(result.Error);
    }
}
