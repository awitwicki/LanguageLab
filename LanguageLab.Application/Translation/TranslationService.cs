using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Languages;
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
/// Suggests a translation for a word: the shared vocabulary first (2 797 shelf words were
/// translated by hand on 2026-09-07 and never need the network), the provider after. A
/// provider's answer is kept in the shared vocabulary as a Machine WordTranslation in the
/// learner's language, so the next lookup of the same word in the same language by anyone costs
/// nothing and the word becomes trainable. A translation already there is never replaced, so a
/// hand-made one always wins.
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
    public async Task<TranslationLookup> LookupAsync(string word, LearnerLanguage language, CancellationToken cancellationToken)
    {
        var shared = await _dbContext.Words
            .Include(w => w.Translations.Where(t => t.Language == language.Code))
            .FirstOrDefaultAsync(w => w.OwnerId == null && w.Word == word, cancellationToken);

        // Filtered by hand, not just by the Include: an already-tracked WordPair (e.g. looked up
        // earlier in the same DbContext, in another language) keeps other languages' rows in this
        // collection too — the Include's Where narrows what a fresh load adds, not what a
        // change-tracked collection already holds.
        if (shared?.Translations.FirstOrDefault(t => t.Language == language.Code) is { } known)
        {
            return new TranslationLookup(word, known.Text, TranslationSource.Dictionary);
        }

        var translated = await _translator.TranslateAsync(word, language, cancellationToken);

        if (translated == null)
        {
            return new TranslationLookup(word, null, TranslationSource.None);
        }

        shared ??= _dbContext.Words.Add(new WordPair { Word = word }).Entity;
        var row = new WordTranslation
        {
            WordPair = shared, Language = language.Code, Text = translated, Origin = TranslationOrigin.Machine,
        };
        _dbContext.WordTranslations.Add(row);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two first lookups raced: either (Word, OwnerId) or (WordPairId, Language) let one
            // through. The winner's rows are as good as ours — drop ours and answer all the same.
            _dbContext.Entry(row).State = EntityState.Detached;

            if (_dbContext.Entry(shared).State == EntityState.Added)
            {
                _dbContext.Entry(shared).State = EntityState.Detached;
            }
        }

        return new TranslationLookup(word, translated, TranslationSource.MyMemory);
    }
}
