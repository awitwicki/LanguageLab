using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public enum LoginOutcome
{
    SignedIn,
    Banned,
}

/// <summary>
/// The identity claims of someone who has just signed in. The OIDC handler has already
/// validated the token they came from, so this is trusted data, not request input.
/// TelegramUserId is Telegram's numeric `id` claim, never `sub`.
/// </summary>
public sealed record TelegramIdentity(
    long TelegramUserId,
    string? FirstName,
    string? LastName,
    string? Username,
    string? PhotoUrl,
    // Telegram's language_code; only the Mini App sends it.
    string? LanguageCode = null);

public sealed record LoginResult(LoginOutcome Outcome, TelegramUser User);

/// <summary>
/// Turns a validated Telegram identity into an account. Registration is not a separate
/// step: the first successful login for a Telegram id creates the row.
/// </summary>
public class UserLoginService
{
    private readonly ApplicationDbContext _dbContext;

    public UserLoginService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LoginResult> LoginAsync(TelegramIdentity identity, DateTime utcNow)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.TelegramUserId == identity.TelegramUserId);

        if (user is { IsBanned: true })
        {
            // Nothing is refreshed for a banned user: a ban should not keep their
            // profile warm, and LastLoginAt should not record a login that did not happen.
            return new LoginResult(LoginOutcome.Banned, user);
        }

        // "The first registered user becomes the admin": only while this person is the instance's
        // only account — an empty table, or the one row the old config-user path created before
        // anyone logged in. Never "whoever signs in while no admin exists", which would hand admin
        // to a stranger once the last admin was gone. Asked before the new row is added.
        var isOnlyAccount = !await _dbContext.Users.AnyAsync(u => u.TelegramUserId != identity.TelegramUserId);

        if (user == null)
        {
            user = new TelegramUser { TelegramUserId = identity.TelegramUserId, CreatedAt = utcNow };
            _dbContext.Users.Add(user);
        }

        if (isOnlyAccount)
        {
            user.Role = UserRole.Admin;
        }

        user.FirstName = identity.FirstName;
        user.LastName = identity.LastName;
        user.Username = identity.Username;
        user.PhotoUrl = identity.PhotoUrl;

        // Only a suggestion for the language picker. The OIDC path never sends it, so an absent
        // code keeps the last one rather than wiping it.
        if (!string.IsNullOrWhiteSpace(identity.LanguageCode))
        {
            user.TelegramLanguageCode = identity.LanguageCode.Length > 16 ? identity.LanguageCode[..16] : identity.LanguageCode;
        }

        user.LastLoginAt = utcNow;

        await _dbContext.SaveChangesAsync();

        return new LoginResult(LoginOutcome.SignedIn, user);
    }
}
