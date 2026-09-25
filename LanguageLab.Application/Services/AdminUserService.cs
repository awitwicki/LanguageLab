using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public enum AdminActionResult
{
    Ok,
    NotFound,

    /// <summary>An admin tried to ban, demote or delete themselves.</summary>
    SelfAction,

    /// <summary>The change would leave the instance with no administrator at all.</summary>
    LastAdmin,
}

public sealed record AdminUserView(
    long Id,
    long TelegramUserId,
    string DisplayName,
    string? Username,
    string? PhotoUrl,
    UserRole Role,
    bool IsBanned,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

/// <summary>One page of the admin list. Total counts the whole filtered list, not the page.</summary>
public sealed record AdminUserPage(IReadOnlyList<AdminUserView> Items, int Total, int Page, int PageSize);

/// <summary>
/// The admin panel's operations, guards included. They live here rather than in the
/// endpoints so the rules can be tested without an HTTP stack.
/// </summary>
public class AdminUserService
{
    private readonly ApplicationDbContext _dbContext;

    public AdminUserService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Oldest first: the admin is normally the first row, and the list reads as a history.
    /// Search is a case-insensitive substring over first name, last name and username —
    /// DisplayName is assembled in memory, so the query has to look at its parts. A term
    /// made of digits is also tried as the Telegram id, which is what an admin has at hand
    /// when a profile is blank. Out-of-range paging is clamped rather than refused.
    /// </summary>
    public async Task<AdminUserPage> ListAsync(string? search = null, int page = 1, int pageSize = DefaultPageSize)
    {
        page = Math.Max(page, 1);
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var query = _dbContext.Users.AsNoTracking();
        var term = search?.Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(term))
        {
            var telegramId = long.TryParse(term, out var parsed) ? parsed : (long?)null;

            query = query.Where(u =>
                (u.FirstName != null && u.FirstName.ToLower().Contains(term))
                || (u.LastName != null && u.LastName.ToLower().Contains(term))
                || (u.Username != null && u.Username.ToLower().Contains(term))
                || (telegramId != null && u.TelegramUserId == telegramId));
        }

        var total = await query.CountAsync();

        var users = await query
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = users
            .Select(u => new AdminUserView(
                u.Id, u.TelegramUserId, u.DisplayName, u.Username, u.PhotoUrl,
                u.Role, u.IsBanned, u.CreatedAt, u.LastLoginAt))
            .ToList();

        return new AdminUserPage(items, total, page, pageSize);
    }

    public async Task<AdminActionResult> SetBannedAsync(long actorId, long targetId, bool banned)
    {
        if (actorId == targetId)
        {
            return AdminActionResult.SelfAction;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == targetId);

        if (user == null)
        {
            return AdminActionResult.NotFound;
        }

        user.IsBanned = banned;
        await _dbContext.SaveChangesAsync();

        return AdminActionResult.Ok;
    }

    public async Task<AdminActionResult> SetRoleAsync(long actorId, long targetId, UserRole role)
    {
        if (actorId == targetId)
        {
            return AdminActionResult.SelfAction;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == targetId);

        if (user == null)
        {
            return AdminActionResult.NotFound;
        }

        if (role != UserRole.Admin && await UserRules.IsLastAdminAsync(_dbContext, user))
        {
            return AdminActionResult.LastAdmin;
        }

        user.Role = role;
        await _dbContext.SaveChangesAsync();

        return AdminActionResult.Ok;
    }

    /// <summary>
    /// Hard delete. The user's shelves, Leitner progress and trainings go with them by
    /// cascade; dictionaries they imported survive with a null owner. The personal dictionary
    /// goes too.
    /// </summary>
    public async Task<AdminActionResult> DeleteAsync(long actorId, long targetId)
    {
        if (actorId == targetId)
        {
            return AdminActionResult.SelfAction;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == targetId);

        if (user == null)
        {
            return AdminActionResult.NotFound;
        }

        if (await UserRules.IsLastAdminAsync(_dbContext, user))
        {
            return AdminActionResult.LastAdmin;
        }

        UserRules.RemovePersonalDictionary(_dbContext, targetId);
        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();

        return AdminActionResult.Ok;
    }
}
