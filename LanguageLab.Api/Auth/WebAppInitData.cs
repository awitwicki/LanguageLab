using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanguageLab.Application.Services;
using Microsoft.AspNetCore.WebUtilities;

namespace LanguageLab.Api.Auth;

/// <summary>Either an identity or the reason there is none — never both, never neither.</summary>
public sealed record InitDataResult(TelegramIdentity? Identity, string? Error)
{
    [MemberNotNullWhen(true, nameof(Identity))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsValid => Identity != null;

    public static InitDataResult Valid(TelegramIdentity identity) => new(identity, null);

    public static InitDataResult Invalid(string error) => new(null, error);
}

/// <summary>
/// Checks the launch parameters a Telegram Mini App receives (window.Telegram.WebApp.initData)
/// the way https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
/// prescribes, and turns the `user` field into the same <see cref="TelegramIdentity"/> the
/// OIDC flow produces. Until the signature checks out the string is request input and nothing
/// in it is read; after that it is as trustworthy as an id_token, because only Telegram and
/// the holder of the bot token can produce the hash.
/// </summary>
public static class WebAppInitData
{
    /// <summary>
    /// How old a launch may be. Telegram signs fresh parameters on every open, so a cold sign-in
    /// always has young ones; the window is the 24 hours Telegram's own
    /// @telegram-apps/init-data-node defaults to.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    // In the first HMAC this literal is the key and the bot token is the message — the
    // documented order, and an easy one to get backwards.
    private static readonly byte[] SecretKey = "WebAppData"u8.ToArray();

    public static InitDataResult Validate(string? initData, string botToken, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(initData))
        {
            return InitDataResult.Invalid("No launch parameters were sent.");
        }

        // application/x-www-form-urlencoded: values arrive percent-encoded, and the check
        // string is built from the decoded ones.
        var fields = QueryHelpers.ParseQuery(initData);

        if (!fields.Remove("hash", out var hash) || !TryFromHex(hash.ToString(), out var received))
        {
            return InitDataResult.Invalid("The launch parameters carry no signature.");
        }

        // Every field but hash, as `key=value`, sorted by key, one per line. `signature` (the
        // Ed25519 one meant for third parties) is just another field here.
        var checkString = string.Join('\n', fields
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));

        var secret = HMACSHA256.HashData(SecretKey, Encoding.UTF8.GetBytes(botToken));
        var expected = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(checkString));

        if (!CryptographicOperations.FixedTimeEquals(expected, received))
        {
            return InitDataResult.Invalid("The launch parameters' signature does not match.");
        }

        // From here on the fields are Telegram's.
        if (!fields.TryGetValue("auth_date", out var authDate) ||
            !long.TryParse(authDate.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return InitDataResult.Invalid("The launch parameters carry no auth_date.");
        }

        if (now - DateTimeOffset.FromUnixTimeSeconds(unixSeconds) > MaxAge)
        {
            return InitDataResult.Invalid("The launch parameters are too old. Reopen the app from the bot.");
        }

        if (!fields.TryGetValue("user", out var user))
        {
            return InitDataResult.Invalid("The launch parameters carry no user.");
        }

        var identity = ReadUser(user.ToString());

        return identity == null
            ? InitDataResult.Invalid("The launch parameters' user has no id.")
            : InitDataResult.Valid(identity);
    }

    private static bool TryFromHex(string text, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(text);
            return bytes.Length == HMACSHA256.HashSizeInBytes;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    /// <summary>The `user` field is a WebAppUser JSON object; only id and first_name are guaranteed.</summary>
    private static TelegramIdentity? ReadUser(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.Number ||
                !id.TryGetInt64(out var telegramUserId))
            {
                return null;
            }

            return new TelegramIdentity(
                telegramUserId,
                OptionalString(root, "first_name"),
                OptionalString(root, "last_name"),
                OptionalString(root, "username"),
                OptionalString(root, "photo_url"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
