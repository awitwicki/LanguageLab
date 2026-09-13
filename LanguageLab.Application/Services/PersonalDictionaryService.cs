using LanguageLab.Domain;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>Box is null until the word's first exercise; 1..5 while learning; IsLearned once graduated.</summary>
public sealed record PersonalWord(long WordPairId, string Word, string Translation, int? Box, bool IsLearned);

/// <summary>One line of a bulk import, before normalization.</summary>
public sealed record BulkWordEntry(string Word, string Translation);

/// <summary>Word/Translation echo the normalized input, for a UI to render a per-line result list.</summary>
public sealed record BulkWordOutcome(string Word, string Translation, bool Added, string? Error);

public sealed record PersonalDictionaryView(
    long Id,
    string Name,
    int WordsCount,
    int LearnableCount,
    int DueCount,
    LearningProgress Learning,
    IReadOnlyList<PersonalWord> Words);

/// <summary>
/// The user's own word list. An ordinary Dictionary row underneath (IsPersonal, private, no
/// chapters), so preview, new-batch and review train it like any book. What is special is how
/// words get in — typed, not imported, as rows owned by the user — and that they are shelved
/// "don't know" on arrival: nobody adds a word they already know.
/// </summary>
public class PersonalDictionaryService
{
    public const string Name = "My words";

    private readonly ApplicationDbContext _dbContext;
    private readonly WordSelectionService _selection;
    private readonly LearningProgressService _learningProgress;

    public PersonalDictionaryService(
        ApplicationDbContext dbContext, WordSelectionService selection, LearningProgressService learningProgress)
    {
        _dbContext = dbContext;
        _selection = selection;
        _learningProgress = learningProgress;
    }

    public async Task<Domain.Entities.Dictionary> GetOrCreateAsync(long userId)
    {
        var existing = await Find(userId).FirstOrDefaultAsync();

        if (existing != null)
        {
            return existing;
        }

        var dictionary = new Domain.Entities.Dictionary
        {
            Name = Name,
            OwnerId = userId,
            IsPublic = false,
            IsPersonal = true,
            WordsCount = 0,
        };

        _dbContext.Dictionaries.Add(dictionary);

        try
        {
            await _dbContext.SaveChangesAsync();
            return dictionary;
        }
        catch (DbUpdateException)
        {
            // Two first requests raced and the unique index let exactly one through: forget
            // ours and take theirs.
            _dbContext.Entry(dictionary).State = EntityState.Detached;
            return await Find(userId).FirstAsync();
        }
    }

    /// <summary>
    /// Null when the user already has this word (the endpoint answers 409); ArgumentException
    /// with a user-facing message for an empty translation or an unusable word.
    /// </summary>
    public async Task<PersonalWord?> AddAsync(long userId, string rawWord, string rawTranslation, DateTime nowUtc)
    {
        var word = WordText.Normalize(rawWord);
        var translation = rawTranslation.Trim();

        if (!WordText.IsValid(word))
        {
            throw new ArgumentException($"The word must be 1–{WordText.MaxLength} letters, spaces, hyphens or apostrophes.");
        }

        if (translation.Length == 0)
        {
            throw new ArgumentException("The translation cannot be empty.");
        }

        if (await _dbContext.Words.AnyAsync(w => w.OwnerId == userId && w.Word == word))
        {
            return null;
        }

        var dictionary = await GetOrCreateAsync(userId);
        var pair = new WordPair { Word = word, Translation = translation, OwnerId = userId };
        var dictionaryWord = new DictionaryWord { Dictionary = dictionary, WordPair = pair, Frequency = 0 };

        // Straight onto the "don't know" shelf — that is what makes it learnable. A brand-new
        // row can be on no other shelf, so WordSortingService.MarkAsync's exclusivity dance is
        // not needed.
        var unknownWord = new UnknownWord { UserId = userId, WordPair = pair, CreatedAt = nowUtc };

        _dbContext.Words.Add(pair);
        _dbContext.DictionaryWords.Add(dictionaryWord);
        _dbContext.UnknownWords.Add(unknownWord);
        dictionary.WordsCount++;

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two AddAsync calls for the same user+word (a double-click, a retried request
            // after a dropped response) can both pass the AnyAsync check above; only the real
            // (Word, OwnerId) unique index actually stops the second one. Mirror
            // GetOrCreateAsync's race recovery: drop our half-built rows and answer the same
            // way the fast duplicate path above does — no other row could trip this index.
            _dbContext.Entry(pair).State = EntityState.Detached;
            _dbContext.Entry(dictionaryWord).State = EntityState.Detached;
            _dbContext.Entry(unknownWord).State = EntityState.Detached;
            dictionary.WordsCount--;

            await _dbContext.Words.FirstAsync(w => w.OwnerId == userId && w.Word == word);
            return null;
        }

        return new PersonalWord(pair.Id, pair.Word, pair.Translation, Box: null, IsLearned: false);
    }

    /// <summary>
    /// Adds each line independently via AddAsync, so one invalid or duplicate line does not
    /// stop the rest — including a duplicate against an earlier line in the same batch, since
    /// each AddAsync call commits before the next line's duplicate check runs.
    /// </summary>
    public async Task<IReadOnlyList<BulkWordOutcome>> AddManyAsync(
        long userId, IReadOnlyList<BulkWordEntry> entries, DateTime nowUtc)
    {
        var outcomes = new List<BulkWordOutcome>(entries.Count);

        foreach (var entry in entries)
        {
            var word = WordText.Normalize(entry.Word);
            var translation = entry.Translation.Trim();

            try
            {
                var added = await AddAsync(userId, entry.Word, entry.Translation, nowUtc);
                outcomes.Add(added == null
                    ? new BulkWordOutcome(word, translation, Added: false, Error: "Already in your dictionary.")
                    : new BulkWordOutcome(word, translation, Added: true, Error: null));
            }
            catch (ArgumentException e)
            {
                outcomes.Add(new BulkWordOutcome(word, translation, Added: false, Error: e.Message));
            }
        }

        return outcomes;
    }

    /// <summary>
    /// False unless the word is this user's own. The same teardown as
    /// TrainingSessionService.DeleteWordAsync — only the FK from DictionaryWords cascades in
    /// the database, and the InMemory provider cascades nothing, so everything goes by hand.
    /// </summary>
    public async Task<bool> RemoveAsync(long userId, long wordPairId)
    {
        var pair = await _dbContext.Words.FirstOrDefaultAsync(w => w.Id == wordPairId && w.OwnerId == userId);

        if (pair == null)
        {
            return false;
        }

        _dbContext.KnownWords.RemoveRange(_dbContext.KnownWords.Where(k => k.WordPairId == pair.Id));
        _dbContext.UnknownWords.RemoveRange(_dbContext.UnknownWords.Where(u => u.WordPairId == pair.Id));
        _dbContext.ExcludedWords.RemoveRange(_dbContext.ExcludedWords.Where(e => e.WordPairId == pair.Id));
        _dbContext.WordProgresses.RemoveRange(_dbContext.WordProgresses.Where(p => p.WordPairId == pair.Id));
        _dbContext.TrainingQuestions.RemoveRange(_dbContext.TrainingQuestions.Where(q => q.WordPairId == pair.Id));
        _dbContext.DictionaryWords.RemoveRange(_dbContext.DictionaryWords.Where(dw => dw.WordPairId == pair.Id));
        _dbContext.Words.Remove(pair);

        var dictionary = await Find(userId).FirstOrDefaultAsync();

        if (dictionary != null)
        {
            dictionary.WordsCount = Math.Max(0, dictionary.WordsCount - 1);
        }

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<PersonalDictionaryView> GetAsync(long userId, DateTime nowUtc)
    {
        var dictionary = await GetOrCreateAsync(userId);

        // Every owned word is in the personal dictionary by construction — no join needed.
        var words = await _dbContext.Words
            .Where(w => w.OwnerId == userId)
            .OrderByDescending(w => w.Id)
            .Select(w => new
            {
                w.Id,
                w.Word,
                w.Translation,
                Progress = _dbContext.WordProgresses
                    .Where(p => p.UserId == userId && p.WordPairId == w.Id)
                    .Select(p => new { p.Box, p.IsLearned })
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return new PersonalDictionaryView(
            dictionary.Id,
            dictionary.Name,
            dictionary.WordsCount,
            await _selection.CountLearnableAsync(userId, dictionary.Id),
            await _selection.CountDueAsync(userId, nowUtc, dictionary.Id),
            await _learningProgress.GetAsync(userId, dictionary.Id),
            words.Select(w => new PersonalWord(
                w.Id, w.Word, w.Translation, w.Progress?.Box, w.Progress?.IsLearned ?? false)).ToList());
    }

    private IQueryable<Domain.Entities.Dictionary> Find(long userId) =>
        _dbContext.Dictionaries.Where(d => d.IsPersonal && d.OwnerId == userId);
}
