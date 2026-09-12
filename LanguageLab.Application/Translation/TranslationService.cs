using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Translation;

public enum TranslationSource
{
    /// <summary>A shared word already carried this translation; the provider was not asked.</summary>
    Dictionary,
    MyMemory,
    None,
}

public sealed record TranslationLookup(string Word, string? Translation, TranslationSource Source);

/// <summary>
/// Suggests a translation for a hand-typed word: the shared vocabulary first (2 797 shelf words
/// were translated by hand on 2026-09-07 and never need the network), the provider after.
/// Reads only — a provider's guess is not cached into shared rows.
/// </summary>
public class TranslationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITranslator _translator;

    public TranslationService(ApplicationDbContext dbContext, ITranslator translator)
    {
        _dbContext = dbContext;
        _translator = translator;
    }

    /// <summary>Expects an already normalized, valid word (see WordText) — the same form the personal dictionary stores.</summary>
    public async Task<TranslationLookup> LookupAsync(string word, CancellationToken cancellationToken)
    {
        var known = await _dbContext.Words
            .Where(w => w.OwnerId == null && w.Word == word && w.Translation != "")
            .Select(w => w.Translation)
            .FirstOrDefaultAsync(cancellationToken);

        if (known != null)
        {
            return new TranslationLookup(word, known, TranslationSource.Dictionary);
        }

        var translated = await _translator.TranslateAsync(word, cancellationToken);

        return translated == null
            ? new TranslationLookup(word, null, TranslationSource.None)
            : new TranslationLookup(word, translated, TranslationSource.MyMemory);
    }
}
