using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LanguageLab.Tests.Fakes;

/// <summary>
/// Makes a SaveChanges fail the way Postgres refuses a row on a unique index — with a
/// DbUpdateException — whenever <c>shouldFail</c> says so for the context about to save. EF
/// InMemory enforces no unique index, so this is how the conflict paths are tested at all.
/// </summary>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    private readonly Func<DbContext, bool> _shouldFail;

    public FailingSaveInterceptor(Func<DbContext, bool> shouldFail) => _shouldFail = shouldFail;

    public int Failures { get; private set; }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        FailIfAsked(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        FailIfAsked(eventData);
        return ValueTask.FromResult(result);
    }

    private void FailIfAsked(DbContextEventData eventData)
    {
        if (eventData.Context is { } context && _shouldFail(context))
        {
            Failures++;
            throw new DbUpdateException("Simulated unique violation.");
        }
    }
}
