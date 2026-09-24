using LanguageLab.Application.Translation;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record ReaderWordView(
    string Lemma, string? Translation, TranslationSource Source, ReaderWordStatus Status, bool InSharedVocabulary);

public enum LearnOutcome
{
    /// <summary>A shared word went onto the "don't know" shelf, the same as in sorting.</summary>
    Shelved,
    /// <summary>
    /// The word went into "My words" instead: either there was a shared translation but no
    /// dictionary linked it (shelving it would never reach a training batch), or there was no
    /// shared translation at all and the caller's typed one was used.
    /// </summary>
    AddedToPersonal,
}

/// <summary>
/// The reader's word panel and its two buttons. Expects lemmas already normalized and valid
/// (WordText). A shared, translated word already linked to a dictionary is shelved like in
/// sorting, so its progress is the same as in any book; a shared, translated word linked to no
/// dictionary — or one with no shared translation at all — goes into the user's "My words"
/// instead, reusing the shared translation when there is one and falling back to the caller's
/// typed translation otherwise.
/// </summary>
public class ReaderWordService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TranslationService _translation;
    private readonly ReaderWordStatusService _statuses;
    private readonly WordSortingService _sorting;
    private readonly PersonalDictionaryService _personal;

    public ReaderWordService(
        ApplicationDbContext dbContext,
        TranslationService translation,
        ReaderWordStatusService statuses,
        WordSortingService sorting,
        PersonalDictionaryService personal)
    {
        _dbContext = dbContext;
        _translation = translation;
        _statuses = statuses;
        _sorting = sorting;
        _personal = personal;
    }

    public async Task<ReaderWordView> GetAsync(long userId, string lemma, CancellationToken cancellationToken)
    {
        // First: the lookup may create the shared row, and InSharedVocabulary must see it.
        var lookup = await _translation.LookupAsync(lemma, cancellationToken);
        var inShared = await _dbContext.Words.AnyAsync(w => w.OwnerId == null && w.Word == lemma, cancellationToken);
        var status = await _statuses.GetAsync(userId, lemma);

        return new ReaderWordView(lemma, lookup.Translation, lookup.Source, status, inShared);
    }

    /// <summary>ArgumentException with a user-facing message when a translation has to be typed first.</summary>
    public async Task<LearnOutcome> LearnAsync(long userId, string lemma, string? translation, DateTime nowUtc)
    {
        var shared = await _dbContext.Words
            .Where(w => w.OwnerId == null && w.Word == lemma && w.Translation != "")
            .Select(w => new { w.Id, w.Translation, HasDictionary = w.Dictionaries.Any() })
            .FirstOrDefaultAsync();

        // Shelving only makes the word trainable when it already belongs to a dictionary —
        // LearnableQuery requires dictionary membership, so a shared row the reader itself just
        // created (via TranslationService.LookupAsync, unlinked to any book) would sit on the
        // shelf forever and never enter a batch. That word goes to "My words" instead, which is
        // always trainable on its own.
        if (shared is { HasDictionary: true })
        {
            await _sorting.MarkAsync(userId, shared.Id, SortStatus.Unknown, nowUtc);
            return LearnOutcome.Shelved;
        }

        // Use the shared translation when there is one (no need to make the user retype it);
        // otherwise the caller must have typed one.
        var toUse = shared?.Translation ?? translation;

        if (string.IsNullOrWhiteSpace(toUse))
        {
            throw new ArgumentException("Type a translation first.");
        }

        // Null means the word is already in the user's list — already learning, nothing to do.
        await _personal.AddAsync(userId, lemma, toUse, nowUtc);
        return LearnOutcome.AddedToPersonal;
    }

    /// <summary>False when the word is not in the shared vocabulary: there is no row to shelve.</summary>
    public async Task<bool> MarkKnownAsync(long userId, string lemma, DateTime nowUtc)
    {
        var sharedId = await _dbContext.Words
            .Where(w => w.OwnerId == null && w.Word == lemma)
            .Select(w => (long?)w.Id)
            .FirstOrDefaultAsync();

        return sharedId is { } id && await _sorting.MarkAsync(userId, id, SortStatus.Known, nowUtc);
    }
}
