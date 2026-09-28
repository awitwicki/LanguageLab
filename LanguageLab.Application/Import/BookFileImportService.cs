using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LanguageLab.Application.Books;
using LanguageLab.Application.Services;
using LanguageLab.Application.Translation;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Languages;
using LanguageLab.Domain.Lexicon;

namespace LanguageLab.Application.Import;

/// <summary>The book parsed, but its text is not recognizably English — the one refusal the parser cannot see.</summary>
public sealed class NotEnglishBookException : Exception
{
    public NotEnglishBookException()
        : base("The book's text is not recognizably English.")
    {
    }
}

/// <summary>
/// The server side of book import: bytes → SHA-256 → IBookParser → BookChapters.Flatten →
/// ImportTokenizer → BookImportService, then a background-translation enqueue for the importer's
/// language. The file is parsed in memory and never stored (roadmap Q4).
/// </summary>
public partial class BookFileImportService
{
    private readonly IBookParser _parser;
    private readonly IEnglishLexicon _lexicon;
    private readonly BookImportService _import;
    private readonly ITranslationQueue _queue;

    public BookFileImportService(
        IBookParser parser, IEnglishLexicon lexicon, BookImportService import, ITranslationQueue queue)
    {
        _parser = parser;
        _lexicon = lexicon;
        _import = import;
        _queue = queue;
    }

    /// <summary>
    /// Throws <see cref="BookFormatException"/> (unreadable or DRM),
    /// <see cref="NotEnglishBookException"/> (coverage below the gate) or
    /// <see cref="ArgumentException"/> (the core's own refusals). A null
    /// <paramref name="language"/> skips the queue and reports TranslationQueued false —
    /// the import itself still succeeds.
    /// </summary>
    public async Task<ImportResult> ImportAsync(
        byte[] file, string fileName, ChapterMode mode, bool requestPublication,
        long ownerId, UserRole role, LearnerLanguage? language, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
        var book = _parser.Parse(file);
        var tokenized = ImportTokenizer.Tokenize(BookChapters.Flatten(book.Sections, mode), _lexicon);

        if (!tokenized.LooksEnglish)
        {
            throw new NotEnglishBookException();
        }

        var request = new ImportRequest(
            Name: book.Title.Length > 0 ? book.Title : TitleFromFileName(fileName),
            Chapters: tokenized.Chapters
                .Select(chapter => new ImportChapter(chapter.Order, chapter.Title, chapter.Words))
                .ToList(),
            Words: null,
            RequestPublication: requestPublication,
            FileHash: hash);

        var result = await _import.ImportAsync(
            request, ownerId, BookImportService.StatusFor(role, requestPublication));

        if (language is null)
        {
            return result;
        }

        // After the core's SaveChangesAsync and outside any transaction — the queue saves on the
        // shared DbContext (ITranslationQueue's contract).
        await _queue.EnqueueAsync(result.DictionaryId, language, cancellationToken);

        return result with { TranslationQueued = true };
    }

    /// <summary>A port of stripBookExtension (web/src/books/format.ts) — the fallback title C2 left to this stream.</summary>
    internal static string TitleFromFileName(string fileName) =>
        SingleExtension().Replace(DoubleExtension().Replace(fileName, ""), "");

    [GeneratedRegex(@"\.(?:fb2|epub)\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex DoubleExtension();

    [GeneratedRegex(@"\.(?:fb2|epub|zip)$", RegexOptions.IgnoreCase)]
    private static partial Regex SingleExtension();
}
