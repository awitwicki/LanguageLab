using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public enum ReaderWordStatus
{
    New,
    Learning,
    Known,
}

/// <summary>
/// The shelf a word actually sits on, for the word panel's buttons. Unlike
/// <see cref="ReaderWordStatus"/> it tells an ignored word from a known one: the highlights do
/// the same thing with both (stop marking the word), but the learner needs to see which button
/// they pressed, and press it again to undo.
/// </summary>
public enum ReaderWordShelf
{
    New,
    Learning,
    Known,
    Ignored,
}

/// <summary>
/// InTraining: the learner has a Leitner row on the word, so it is not just shelved but really
/// being trained. The panel refuses to undo such a word rather than throw the progress away.
/// </summary>
public sealed record ReaderWordShelfState(ReaderWordShelf Shelf, bool InTraining);

/// <summary>Word texts, ordinal-sorted. A word in neither list is new to the learner.</summary>
public sealed record WordStatuses(IReadOnlyList<string> Learning, IReadOnlyList<string> Known);

/// <summary>
/// The learner's standing on each word text. Per row first: a Leitner progress row decides
/// (learned → known, else learning) because graduation leaves the "don't know" row in place;
/// without one, "don't know" → learning, "know" → known, "exclude" → ignored (a word the learner
/// asked never to see again). Then per text: a shared and a personal row of the same word that
/// disagree keep the most engaged shelf — learning over known over ignored. The reader's
/// highlights ask for <see cref="ReaderWordStatus"/>, which collapses ignored into known; the
/// word panel asks for the shelf itself.
/// </summary>
public class ReaderWordStatusService
{
    private readonly ApplicationDbContext _dbContext;

    public ReaderWordStatusService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WordStatuses> GetAsync(long userId)
    {
        var loaded = await LoadAsync(userId, word: null);

        return new WordStatuses(
            Pick(loaded.ByWord, ReaderWordStatus.Learning),
            Pick(loaded.ByWord, ReaderWordStatus.Known));
    }

    public async Task<ReaderWordStatus> GetAsync(long userId, string word)
    {
        var loaded = await LoadAsync(userId, word);

        return ToStatus(loaded.ByWord.GetValueOrDefault(word, ReaderWordShelf.New));
    }

    public async Task<ReaderWordShelfState> GetShelfAsync(long userId, string word)
    {
        var loaded = await LoadAsync(userId, word);

        return new ReaderWordShelfState(
            loaded.ByWord.GetValueOrDefault(word, ReaderWordShelf.New),
            loaded.InTraining.Contains(word));
    }

    private sealed record Loaded(Dictionary<string, ReaderWordShelf> ByWord, HashSet<string> InTraining);

    private async Task<Loaded> LoadAsync(long userId, string? word)
    {
        var progress = _dbContext.WordProgresses.Where(p => p.UserId == userId);
        var unknown = _dbContext.UnknownWords.Where(u => u.UserId == userId);
        var known = _dbContext.KnownWords.Where(k => k.UserId == userId);
        var excluded = _dbContext.ExcludedWords.Where(e => e.UserId == userId);

        if (word != null)
        {
            progress = progress.Where(p => p.WordPair.Word == word);
            unknown = unknown.Where(u => u.WordPair.Word == word);
            known = known.Where(k => k.WordPair.Word == word);
            excluded = excluded.Where(e => e.WordPair.Word == word);
        }

        var perRow = new Dictionary<long, (string Word, ReaderWordShelf Shelf)>();

        // Lowest precedence first: each later loop overwrites the rows it knows about.
        foreach (var row in await excluded.Select(e => new { e.WordPairId, e.WordPair.Word }).ToListAsync())
        {
            perRow[row.WordPairId] = (row.Word, ReaderWordShelf.Ignored);
        }

        foreach (var row in await known.Select(k => new { k.WordPairId, k.WordPair.Word }).ToListAsync())
        {
            perRow[row.WordPairId] = (row.Word, ReaderWordShelf.Known);
        }

        foreach (var row in await unknown.Select(u => new { u.WordPairId, u.WordPair.Word }).ToListAsync())
        {
            perRow[row.WordPairId] = (row.Word, ReaderWordShelf.Learning);
        }

        var inTraining = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in await progress.Select(p => new { p.WordPairId, p.WordPair.Word, p.IsLearned }).ToListAsync())
        {
            perRow[row.WordPairId] = (row.Word, row.IsLearned ? ReaderWordShelf.Known : ReaderWordShelf.Learning);
            inTraining.Add(row.Word);
        }

        var byWord = new Dictionary<string, ReaderWordShelf>(StringComparer.Ordinal);

        foreach (var (text, shelf) in perRow.Values)
        {
            if (!byWord.TryGetValue(text, out var earlier) || Engagement(shelf) > Engagement(earlier))
            {
                byWord[text] = shelf;
            }
        }

        return new Loaded(byWord, inTraining);
    }

    /// <summary>How much the learner has committed to the word — the tie-break between two rows of one text.</summary>
    private static int Engagement(ReaderWordShelf shelf) => shelf switch
    {
        ReaderWordShelf.Learning => 3,
        ReaderWordShelf.Known => 2,
        ReaderWordShelf.Ignored => 1,
        _ => 0,
    };

    /// <summary>An ignored word is not highlighted, the same as a known one.</summary>
    private static ReaderWordStatus ToStatus(ReaderWordShelf shelf) => shelf switch
    {
        ReaderWordShelf.Learning => ReaderWordStatus.Learning,
        ReaderWordShelf.New => ReaderWordStatus.New,
        _ => ReaderWordStatus.Known,
    };

    private static IReadOnlyList<string> Pick(Dictionary<string, ReaderWordShelf> byWord, ReaderWordStatus status) =>
        byWord.Where(p => ToStatus(p.Value) == status).Select(p => p.Key).Order(StringComparer.Ordinal).ToList();
}
