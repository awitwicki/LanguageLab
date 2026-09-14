using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record ImportWord(string Word, int Count);

public sealed record ImportChapter(int Order, string Title, IReadOnlyList<ImportWord> Words);

/// <summary>
/// A book arrives with chapters; a flat list (like a "top 500") comes through Words.
/// Exactly one of the two must be set.
/// </summary>
public sealed record ImportRequest(
    string Name,
    IReadOnlyList<ImportChapter>? Chapters,
    IReadOnlyList<ImportWord>? Words,
    bool? IsPublic = null);

public sealed record ImportResult(long DictionaryId, int TotalWords, int NewWords, int ReusedWords);

/// <summary>
/// Loads a book parsed on the client into the DB. Raw text never gets here —
/// only base forms with frequencies.
/// </summary>
public class BookImportService
{
    private readonly ApplicationDbContext _dbContext;

    public BookImportService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ImportResult> ImportAsync(ImportRequest request, long ownerId, bool isPublic)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("The dictionary name cannot be empty.", nameof(request));
        }

        var chapters = Normalize(request.Chapters);
        var flat = NormalizeWords(request.Words);

        if (chapters.Count == 0 && flat.Count == 0)
        {
            throw new ArgumentException("The import is empty: neither chapters nor words.", nameof(request));
        }

        // Book frequency is the sum over chapters. Flat imports have no chapters,
        // so the sum is taken straight from the list.
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var counts in chapters.Select(c => c.Words).Append(flat))
        {
            foreach (var (word, count) in counts)
            {
                totals[word] = totals.GetValueOrDefault(word) + count;
            }
        }

        var allWords = totals.Keys.ToList();

        // Shared rows only: a user's personal "silo" is theirs — a book must neither reuse it
        // nor trip over it in the dictionary below (Word is unique per owner, not globally).
        var existing = await _dbContext.Words
            .Where(w => w.OwnerId == null && allWords.Contains(w.Word))
            .ToDictionaryAsync(w => w.Word, StringComparer.Ordinal);

        // Existing words are left alone entirely: the book arrives with empty translations
        // and would wipe out the result of the translation step.
        var created = new List<WordPair>();

        foreach (var word in allWords)
        {
            if (existing.ContainsKey(word))
            {
                continue;
            }

            var pair = new WordPair { Word = word, Translation = string.Empty };
            created.Add(pair);
            existing[word] = pair;
        }

        _dbContext.Words.AddRange(created);

        var dictionary = new Domain.Entities.Dictionary
        {
            Name = request.Name.Trim(),
            WordsCount = totals.Count,
            OwnerId = ownerId,
            IsPublic = isPublic,
        };

        _dbContext.Dictionaries.Add(dictionary);

        foreach (var chapter in chapters)
        {
            dictionary.Chapters.Add(new Chapter
            {
                Order = chapter.Order,
                Title = chapter.Title,
                WordsCount = chapter.Words.Count,
                Words = chapter.Words
                    .Select(cw => new ChapterWord { WordPair = existing[cw.Key], Count = cw.Value })
                    .ToList()
            });
        }

        foreach (var (word, frequency) in totals)
        {
            _dbContext.DictionaryWords.Add(new DictionaryWord
            {
                Dictionary = dictionary,
                WordPair = existing[word],
                Frequency = frequency
            });
        }

        // One SaveChanges is one transaction. An explicit BeginTransaction is redundant
        // here, and the InMemory provider in the tests does not support it anyway.
        await _dbContext.SaveChangesAsync();

        return new ImportResult(
            dictionary.Id,
            TotalWords: totals.Count,
            NewWords: created.Count,
            ReusedWords: totals.Count - created.Count);
    }

    private sealed record NormalizedChapter(int Order, string Title, Dictionary<string, int> Words);

    private static List<NormalizedChapter> Normalize(IReadOnlyList<ImportChapter>? chapters)
    {
        if (chapters == null)
        {
            return [];
        }

        return chapters
            .OrderBy(c => c.Order)
            .Select(c => new NormalizedChapter(c.Order, c.Title.Trim(), NormalizeWords(c.Words)))
            .Where(c => c.Words.Count > 0)
            .ToList();
    }

    /// <summary>
    /// The client already lowercases the words, but the same word can arrive
    /// twice — deduplication is needed regardless.
    /// </summary>
    private static Dictionary<string, int> NormalizeWords(IReadOnlyList<ImportWord>? words)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);

        if (words == null)
        {
            return result;
        }

        foreach (var item in words)
        {
            var word = item.Word.Trim().ToLowerInvariant();

            if (word.Length == 0 || item.Count <= 0)
            {
                continue;
            }

            result[word] = result.GetValueOrDefault(word) + item.Count;
        }

        return result;
    }
}
