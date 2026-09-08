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

    /// <summary>Oldest first: the admin is normally the first row, and the list reads as a history.</summary>
    public async Task<IReadOnlyList<AdminUserView>> ListAsync()
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .ToListAsync();

        return users
            .Select(u => new AdminUserView(
                u.Id, u.TelegramUserId, u.DisplayName, u.Username, u.PhotoUrl,
                u.Role, u.IsBanned, u.CreatedAt, u.LastLoginAt))
            .ToList();
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

        if (role != UserRole.Admin && await IsLastAdminAsync(user))
        {
            return AdminActionResult.LastAdmin;
        }

        user.Role = role;
        await _dbContext.SaveChangesAsync();

        return AdminActionResult.Ok;
    }

    /// <summary>
    /// Hard delete. The user's shelves, Leitner progress and trainings go with them by
    /// cascade; dictionaries they imported survive with a null owner.
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

        if (await IsLastAdminAsync(user))
        {
            return AdminActionResult.LastAdmin;
        }

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();

        return AdminActionResult.Ok;
    }

    private async Task<bool> IsLastAdminAsync(TelegramUser user) =>
        user.Role == UserRole.Admin && await _dbContext.Users.CountAsync(u => u.Role == UserRole.Admin) <= 1;
}
