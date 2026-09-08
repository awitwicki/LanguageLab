using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>
/// Who may see which dictionary. Deliberately an explicit call rather than an EF global
/// query filter: a global filter would also apply inside WordSelectionService and the
/// training internals, where the scope has already been authorised, and bypassing one
/// correctly is easy to get wrong. Explicit means greppable.
/// </summary>
public class DictionaryAccessService
{
    private readonly ApplicationDbContext _dbContext;

    public DictionaryAccessService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Public books, system books (no owner) and your own. Admins curate, so they see all.</summary>
    public IQueryable<Domain.Entities.Dictionary> Visible(long userId, UserRole role) =>
        role == UserRole.Admin
            ? _dbContext.Dictionaries
            : _dbContext.Dictionaries.Where(d => d.IsPublic || d.OwnerId == userId);

    public Task<bool> IsVisibleAsync(long dictionaryId, long userId, UserRole role) =>
        Visible(userId, role).AnyAsync(d => d.Id == dictionaryId);
}
