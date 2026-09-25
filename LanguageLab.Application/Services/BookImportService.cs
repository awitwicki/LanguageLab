using LanguageLab.Domain;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record ImportWord(string Word, int Count);

public sealed record ImportChapter(int Order, string Title, IReadOnlyList<ImportWord> Words);

/// <summary>
/// A book arrives with chapters; a flat list (like a "top 500") comes through Words.
/// Exactly one of the two must be set. FileHash: SHA-256 of the fb2 file, for the reader.
/// </summary>
public sealed record ImportRequest(
    string Name,
    IReadOnlyList<ImportChapter>? Chapters,
    IReadOnlyList<ImportWord>? Words,
    bool RequestPublication = false,
    string? FileHash = null);

public sealed record ImportResult(long DictionaryId, int TotalWords, int NewWords, int ReusedWords, int DroppedWords);

/// <summary>
/// Loads a book parsed on the client into the DB. Raw text never gets here —
/// only base forms with frequencies.
/// </summary>
public class BookImportService
{
    /// <summary>Distinct words one dictionary may hold. A long novel lemmatizes to 10-15k.</summary>
    public const int MaxWords = 50_000;

    /// <summary>Chapters one book may have; leaf chapters of a very long book stay well under this.</summary>
    public const int MaxChapters = 2_000;

    private readonly ApplicationDbContext _dbContext;

    public BookImportService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Where an import lands. A plain user's "share this" is a request, not a decision — that is
    /// the whole trust boundary behind opening import to everyone.
    /// </summary>
    public static PublicationStatus StatusFor(UserRole role, bool requestPublication)
    {
        if (!requestPublication)
        {
            return PublicationStatus.Private;
        }

        return UserRoles.CanPublishDirectly(role) ? PublicationStatus.Published : PublicationStatus.Pending;
    }

    public async Task<ImportResult> ImportAsync(ImportRequest request, long ownerId, PublicationStatus status)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("The dictionary name cannot be empty.", nameof(request));
        }

        if (request.Chapters is { Count: > MaxChapters })
        {
            throw new ArgumentException(
                $"The book has too many chapters: {request.Chapters.Count}, and the limit is {MaxChapters}.",
                nameof(request));
        }

        var dropped = new HashSet<string>(StringComparer.Ordinal);
        var chapters = Normalize(request.Chapters, dropped);
        var flat = NormalizeWords(request.Words, dropped);

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

        // The junk check comes before the emptiness check: a file of nothing but junk empties
        // every chapter, and "the import is empty" would blame the wrong thing.
        var seen = totals.Count + dropped.Count;

        if (seen > 0 && (double)dropped.Count / seen > ImportWordText.MaxJunkShare)
        {
            throw new ArgumentException(
                $"{dropped.Count} of {seen} words are not English words — this file is not a book LanguageLab can use.",
                nameof(request));
        }

        if (chapters.Count == 0 && flat.Count == 0)
        {
            throw new ArgumentException("The import is empty: neither chapters nor words.", nameof(request));
        }

        if (totals.Count > MaxWords)
        {
            throw new ArgumentException(
                $"The book has too many words: {totals.Count}, and the limit is {MaxWords}.", nameof(request));
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

        // The hash is what points a reader's book at this dictionary, and it arrives from the
        // client with nothing to verify it against — the file itself is never uploaded. Keep it
        // only when this user already has a ReaderBook row with that hash: it does not prove the
        // hash is genuine (registering one is just another client-named claim), but it raises the
        // bar past a casual collision or a drive-by import with no ReaderBook at all. The actual
        // backstop against a stranger's junk import reaching other readers is publication review
        // below: an unreviewed import is Private, invisible to everyone but its owner and admins
        // regardless of what hash it claims.
        var fileHash = ReaderHash.Normalize(request.FileHash) is { } normalized
            && await _dbContext.ReaderBooks.AnyAsync(b => b.UserId == ownerId && b.FileHash == normalized)
                ? normalized
                : null;

        var dictionary = new Domain.Entities.Dictionary
        {
            Name = TitleText.Truncate(request.Name.Trim()),
            WordsCount = totals.Count,
            OwnerId = ownerId,
            PublicationStatus = status,
            FileHash = fileHash,
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
            ReusedWords: totals.Count - created.Count,
            DroppedWords: dropped.Count);
    }

    private sealed record NormalizedChapter(int Order, string Title, Dictionary<string, int> Words);

    private static List<NormalizedChapter> Normalize(IReadOnlyList<ImportChapter>? chapters, HashSet<string> dropped)
    {
        if (chapters == null)
        {
            return [];
        }

        return chapters
            .OrderBy(c => c.Order)
            .Select(c => new NormalizedChapter(c.Order, TitleText.Truncate(c.Title.Trim()), NormalizeWords(c.Words, dropped)))
            .Where(c => c.Words.Count > 0)
            .ToList();
    }

    /// <summary>
    /// The client already lowercases the words, but the same word can arrive twice —
    /// deduplication is needed regardless. Words the fb2 tokenizer could never have produced
    /// are dropped here and counted, so the caller can refuse a file that is mostly junk.
    /// </summary>
    private static Dictionary<string, int> NormalizeWords(IReadOnlyList<ImportWord>? words, HashSet<string> dropped)
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

            if (!ImportWordText.IsValid(word))
            {
                dropped.Add(word);
                continue;
            }

            result[word] = result.GetValueOrDefault(word) + item.Count;
        }

        return result;
    }
}
