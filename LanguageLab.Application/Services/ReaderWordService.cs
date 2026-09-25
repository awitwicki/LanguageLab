using LanguageLab.Application.Translation;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>Where "Add to training" sends a word: the dictionary of the book being read, or "My words".</summary>
public enum LearnTarget
{
    Book,
    Personal,
}

/// <summary>
/// CanReset: whether the panel may undo the button the word currently sits under. Off when there
/// is nothing to undo, and off for a word with a Leitner row — undoing would throw progress away.
/// </summary>
public sealed record ReaderWordView(
    string Lemma, string? Translation, TranslationSource Source, ReaderWordShelf Shelf, bool CanReset,
    LearnTarget LearnTarget);

public enum ResetOutcome
{
    /// <summary>The word is off every shelf — including the case where it was on none to begin with.</summary>
    Cleared,
    /// <summary>A Leitner row makes this a word really in training, so the undo left it alone.</summary>
    InTraining,
}

public enum LearnOutcome
{
    /// <summary>The word is in this book's dictionary: it went onto the "don't know" shelf, the same as in sorting.</summary>
    Shelved,
    /// <summary>The word went into "My words", with the shared translation if there is one, else the typed one.</summary>
    AddedToPersonal,
}

/// <summary>
/// The reader's word panel and its buttons. Expects lemmas already normalized and valid
/// (WordText). "Add to training" shelves a word only in the dictionary of the book being read:
/// training pulls new words from one dictionary at a time, so a word shelved in some other book
/// would surface only if the learner happened to train that book. Everything else goes to
/// "My words". "I know it" and "Ignore" shelve the shared row, creating it untranslated when the
/// word has none — names often don't, because the provider echoes them back.
/// </summary>
public class ReaderWordService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TranslationService _translation;
    private readonly ReaderWordStatusService _statuses;
    private readonly WordSortingService _sorting;
    private readonly PersonalDictionaryService _personal;
    private readonly DictionaryAccessService _access;

    public ReaderWordService(
        ApplicationDbContext dbContext,
        TranslationService translation,
        ReaderWordStatusService statuses,
        WordSortingService sorting,
        PersonalDictionaryService personal,
        DictionaryAccessService access)
    {
        _dbContext = dbContext;
        _translation = translation;
        _statuses = statuses;
        _sorting = sorting;
        _personal = personal;
        _access = access;
    }

    /// <summary>dictionaryId: the dictionary of the book being read, if it has one.</summary>
    public async Task<ReaderWordView> GetAsync(
        long userId, UserRole role, string lemma, long? dictionaryId, CancellationToken cancellationToken)
    {
        // First: the lookup may create the shared row, and the target check must see it.
        var lookup = await _translation.LookupAsync(lemma, cancellationToken);
        var shelf = await _statuses.GetShelfAsync(userId, lemma);
        var target = await LearnTargetAsync(userId, role, lemma, dictionaryId);
        var canReset = shelf.Shelf != ReaderWordShelf.New && !shelf.InTraining;

        return new ReaderWordView(lemma, lookup.Translation, lookup.Source, shelf.Shelf, canReset, target);
    }

    /// <summary>
    /// Book when the book's dictionary is visible to the user and holds the lemma's shared,
    /// translated row; Personal otherwise.
    /// </summary>
    public async Task<LearnTarget> LearnTargetAsync(long userId, UserRole role, string lemma, long? dictionaryId)
    {
        if (dictionaryId is not { } id || !await _access.IsVisibleAsync(id, userId, role))
        {
            return LearnTarget.Personal;
        }

        var inBook = await _dbContext.Words.AnyAsync(w =>
            w.OwnerId == null && w.Word == lemma && w.Translation != "" && w.Dictionaries.Any(d => d.Id == id));

        return inBook ? LearnTarget.Book : LearnTarget.Personal;
    }

    /// <summary>ArgumentException with a user-facing message when a translation has to be typed first.</summary>
    public async Task<LearnOutcome> LearnAsync(
        long userId, UserRole role, string lemma, long? dictionaryId, string? translation, DateTime nowUtc)
    {
        var shared = await _dbContext.Words
            .Where(w => w.OwnerId == null && w.Word == lemma && w.Translation != "")
            .Select(w => new { w.Id, w.Translation })
            .FirstOrDefaultAsync();

        if (shared != null && await LearnTargetAsync(userId, role, lemma, dictionaryId) == LearnTarget.Book)
        {
            await _sorting.MarkAsync(userId, shared.Id, SortStatus.Unknown, nowUtc);
            return LearnOutcome.Shelved;
        }

        // The shared translation when there is one — no need to make the learner retype it.
        var toUse = shared?.Translation ?? translation;

        if (string.IsNullOrWhiteSpace(toUse))
        {
            throw new ArgumentException("Type a translation first.");
        }

        // Null means the word is already in the user's list — already learning, nothing to do.
        await _personal.AddAsync(userId, lemma, toUse, nowUtc);
        return LearnOutcome.AddedToPersonal;
    }

    public async Task MarkKnownAsync(long userId, string lemma, DateTime nowUtc) =>
        await _sorting.MarkAsync(userId, await SharedRowIdAsync(lemma), SortStatus.Known, nowUtc);

    /// <summary>The "exclude" shelf — names and other non-words. The reader stops highlighting it.</summary>
    public async Task IgnoreAsync(long userId, string lemma, DateTime nowUtc) =>
        await _sorting.MarkAsync(userId, await SharedRowIdAsync(lemma), SortStatus.Excluded, nowUtc);

    /// <summary>
    /// The undo behind the panel's selected button: takes the lemma off whichever shelf it sits
    /// on, and out of "My words" when that is where "Add to training" put it — left there it
    /// would keep turning up in exercises. Both the shared row and the user's own count, since a
    /// word can have one of each. Refused for a word with a Leitner row: the learner has real
    /// progress on it, and a mis-tap undo must not be what throws that away.
    /// </summary>
    public async Task<ResetOutcome> ResetAsync(long userId, string lemma)
    {
        var rows = await _dbContext.Words
            .Where(w => w.Word == lemma && (w.OwnerId == null || w.OwnerId == userId))
            .Select(w => new { w.Id, w.OwnerId })
            .ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();

        if (await _dbContext.WordProgresses.AnyAsync(p => p.UserId == userId && ids.Contains(p.WordPairId)))
        {
            return ResetOutcome.InTraining;
        }

        _dbContext.KnownWords.RemoveRange(
            _dbContext.KnownWords.Where(k => k.UserId == userId && ids.Contains(k.WordPairId)));
        _dbContext.UnknownWords.RemoveRange(
            _dbContext.UnknownWords.Where(u => u.UserId == userId && ids.Contains(u.WordPairId)));
        _dbContext.ExcludedWords.RemoveRange(
            _dbContext.ExcludedWords.Where(e => e.UserId == userId && ids.Contains(e.WordPairId)));

        await _dbContext.SaveChangesAsync();

        foreach (var own in rows.Where(r => r.OwnerId == userId))
        {
            await _personal.RemoveAsync(userId, own.Id);
        }

        return ResetOutcome.Cleared;
    }

    /// <summary>The lemma's shared row, created untranslated when there is none (as book import does).</summary>
    private async Task<long> SharedRowIdAsync(string lemma)
    {
        var existing = await _dbContext.Words
            .Where(w => w.OwnerId == null && w.Word == lemma)
            .Select(w => (long?)w.Id)
            .FirstOrDefaultAsync();

        if (existing is { } id)
        {
            return id;
        }

        var row = new WordPair { Word = lemma, Translation = string.Empty };
        _dbContext.Words.Add(row);

        try
        {
            await _dbContext.SaveChangesAsync();
            return row.Id;
        }
        catch (DbUpdateException)
        {
            // A concurrent request created it first; the (Word, OwnerId) index let one through.
            _dbContext.Entry(row).State = EntityState.Detached;

            return await _dbContext.Words
                .Where(w => w.OwnerId == null && w.Word == lemma)
                .Select(w => w.Id)
                .FirstAsync();
        }
    }
}
