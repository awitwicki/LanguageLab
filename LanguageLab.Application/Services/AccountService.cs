using LanguageLab.Domain.Languages;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public enum AccountDeleteResult
{
    Ok,
    NotFound,

    /// <summary>The user is the only administrator; leaving would strand the instance.</summary>
    LastAdmin,
}

public enum SetLanguageResult
{
    Saved,
    UnknownLanguage,
    NotFound,
}

public enum SetVerbsWordCountResult
{
    Saved,
    InvalidValue,
    NotFound,
}

/// <summary>
/// What a signed-in user may do to their own account. Deliberately separate from
/// AdminUserService, whose every operation refuses to act on the caller — self-deletion is
/// the one thing that must.
/// </summary>
public class AccountService
{
    private readonly ApplicationDbContext _dbContext;

    public AccountService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Hard delete, with the same consequences as an admin deleting the user: shelves,
    /// Leitner progress and trainings go by cascade; dictionaries they imported survive
    /// with a null owner. The personal dictionary goes too.
    /// </summary>
    public async Task<AccountDeleteResult> DeleteOwnAsync(long userId)
    {
        await using var adminLock = await UserRules.LockAdminsAsync(_dbContext);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return AccountDeleteResult.NotFound;
        }

        if (await UserRules.IsLastAdminAsync(_dbContext, user))
        {
            return AccountDeleteResult.LastAdmin;
        }

        UserRules.RemovePersonalDictionary(_dbContext, userId);
        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();

        if (adminLock != null)
        {
            await adminLock.CommitAsync();
        }

        return AccountDeleteResult.Ok;
    }

    /// <summary>Signs the user out everywhere: every cookie issued so far stops validating. False for an unknown user.</summary>
    public async Task<bool> RevokeSessionsAsync(long userId)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return false;
        }

        user.SessionVersion++;
        await _dbContext.SaveChangesAsync();
        return true;
    }

    /// <summary>Only a catalog code is accepted — Russian is not in the catalog, so it is refused like any unknown code.</summary>
    public async Task<SetLanguageResult> SetLanguageAsync(long userId, string? code)
    {
        if (LearnerLanguages.Find(code) is not { } language)
        {
            return SetLanguageResult.UnknownLanguage;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return SetLanguageResult.NotFound;
        }

        user.Language = language.Code;
        await _dbContext.SaveChangesAsync();
        return SetLanguageResult.Saved;
    }

    /// <summary>
    /// The upper bound is the client's own concern (it never offers more words than a session
    /// has); this only refuses what could not be a word count at all.
    /// </summary>
    public async Task<SetVerbsWordCountResult> SetVerbsWordCountAsync(long userId, int words)
    {
        if (words < 1)
        {
            return SetVerbsWordCountResult.InvalidValue;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return SetVerbsWordCountResult.NotFound;
        }

        user.VerbsWordCount = words;
        await _dbContext.SaveChangesAsync();
        return SetVerbsWordCountResult.Saved;
    }
}
