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
    string? PhotoUrl);

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

        // Asked before the new row is added, so a first login sees an empty table.
        var hasAdmin = await _dbContext.Users.AnyAsync(u => u.Role == UserRole.Admin);

        if (user == null)
        {
            user = new TelegramUser { TelegramUserId = identity.TelegramUserId, CreatedAt = utcNow };
            _dbContext.Users.Add(user);
        }

        // "The first registered user becomes the admin" — evaluated at login rather than
        // at insert, because rows can predate logins (the old config-user path created one).
        if (!hasAdmin)
        {
            user.Role = UserRole.Admin;
        }

        user.FirstName = identity.FirstName;
        user.LastName = identity.LastName;
        user.Username = identity.Username;
        user.PhotoUrl = identity.PhotoUrl;
        user.LastLoginAt = utcNow;

        await _dbContext.SaveChangesAsync();

        return new LoginResult(LoginOutcome.SignedIn, user);
    }
}
