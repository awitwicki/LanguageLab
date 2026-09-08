using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record ImportWord(string Word, int Count);

public sealed record ImportChapter(int Order, string Title, IReadOnlyList<ImportWord> Words);

/// <summary>
/// Книжка приходить із главами; плаский список (на кшталт «топ-500») — через Words.
/// Задано має бути рівно одне з двох.
/// </summary>
public sealed record ImportRequest(
    string Name,
    IReadOnlyList<ImportChapter>? Chapters,
    IReadOnlyList<ImportWord>? Words,
    bool? IsPublic = null);

public sealed record ImportResult(long DictionaryId, int TotalWords, int NewWords, int ReusedWords);

/// <summary>
/// Заливає розібрану на клієнті книжку в БД. Сирий текст сюди не потрапляє —
/// лише базові форми з частотами.
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
            throw new ArgumentException("Назва словника не може бути порожньою.", nameof(request));
        }

        var chapters = Normalize(request.Chapters);
        var flat = NormalizeWords(request.Words);

        if (chapters.Count == 0 && flat.Count == 0)
        {
            throw new ArgumentException("Імпорт порожній: немає ні глав, ні слів.", nameof(request));
        }

        // Частота по книжці — сума по главах. Для пласких імпортів глав немає,
        // і сума береться прямо зі списку.
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var counts in chapters.Select(c => c.Words).Append(flat))
        {
            foreach (var (word, count) in counts)
            {
                totals[word] = totals.GetValueOrDefault(word) + count;
            }
        }

        var allWords = totals.Keys.ToList();

        var existing = await _dbContext.Words
            .Where(w => allWords.Contains(w.Word))
            .ToDictionaryAsync(w => w.Word, StringComparer.Ordinal);

        // Наявні слова не чіпаємо взагалі: книжка приходить із порожніми перекладами
        // і затерла б результат кроку перекладу.
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

        // Один SaveChanges — одна транзакція. Явна BeginTransaction тут зайва
        // й до того ж не підтримується InMemory-провайдером у тестах.
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
    /// Клієнт уже приводить слова до нижнього регістру, але одне слово може
    /// прийти двічі — дедуплікація потрібна в будь-якому разі.
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
