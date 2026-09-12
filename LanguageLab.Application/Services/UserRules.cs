using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

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
    /// Dictionary → Owner is SetNull so imported books survive their importer; the personal
    /// dictionary is the one book that is nothing without its owner, so it goes by hand. Its
    /// words cascade through WordPair → Owner.
    /// </summary>
    public static void RemovePersonalDictionary(ApplicationDbContext dbContext, long userId) =>
        dbContext.Dictionaries.RemoveRange(dbContext.Dictionaries.Where(d => d.IsPersonal && d.OwnerId == userId));
}
