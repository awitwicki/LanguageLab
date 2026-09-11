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
    /// with a null owner.
    /// </summary>
    public async Task<AccountDeleteResult> DeleteOwnAsync(long userId)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return AccountDeleteResult.NotFound;
        }

        if (await UserRules.IsLastAdminAsync(_dbContext, user))
        {
            return AccountDeleteResult.LastAdmin;
        }

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();

        return AccountDeleteResult.Ok;
    }
}
