using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LanguageLab.Application.Services;

/// <summary>
/// Invariants about accounts that more than one service must honour. Kept in one place so
/// the admin panel and a user acting on their own account cannot drift apart on them.
/// </summary>
public static class UserRules
{
    /// <summary>
    /// An instance must always keep at least one administrator, or nobody can promote
    /// anyone ever again. True when removing or demoting this user would break that.
    /// </summary>
    public static async Task<bool> IsLastAdminAsync(ApplicationDbContext dbContext, TelegramUser user) =>
        user.Role == UserRole.Admin && await dbContext.Users.CountAsync(u => u.Role == UserRole.Admin) <= 1;

    /// <summary>
    /// <see cref="IsLastAdminAsync"/> is a count followed by a separate write: two admins demoting
    /// or deleting each other at once could both see "2" and leave none. A transaction-scoped
    /// advisory lock serializes every admin-count change; take it before loading the user, and
    /// commit after the save. Null on a non-relational provider (the in-memory test database),
    /// where there is no concurrency to guard.
    /// </summary>
    public static async Task<IDbContextTransaction?> LockAdminsAsync(ApplicationDbContext dbContext)
    {
        if (!dbContext.Database.IsRelational())
        {
            return null;
        }

        var transaction = await dbContext.Database.BeginTransactionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7426001)");
        return transaction;
    }

    /// <summary>
    /// Dictionary → Owner is SetNull so imported books survive their importer; the personal
    /// dictionary is the one book that is nothing without its owner, so it goes by hand. Its
    /// words cascade through WordPair → Owner.
    /// </summary>
    public static void RemovePersonalDictionary(ApplicationDbContext dbContext, long userId) =>
        dbContext.Dictionaries.RemoveRange(dbContext.Dictionaries.Where(d => d.IsPersonal && d.OwnerId == userId));
}
