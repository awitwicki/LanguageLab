using LanguageLab.Domain;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Languages;
using LanguageLab.Domain.Lexicon;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Translation;

public enum TranslationSource
{
    /// <summary>A shared word already carried this translation; the provider was not asked.</summary>
    Dictionary,
    /// <summary>The language model answered; for an English word the translation is now in the shared vocabulary.</summary>
    Llm,
    /// <summary>The model was asked and had no answer.</summary>
    None,
    /// <summary>A miss inside the user's uncached-translation window: the model was not asked.</summary>
    RateLimited,
}

/// <summary>RetryAfterSeconds is set only with RateLimited: the wait before a miss may reach the model again.</summary>
public sealed record TranslationLookup(
    string Word, string? Translation, TranslationSource Source, int? RetryAfterSeconds = null);

/// <summary>
/// Suggests a translation for a word: the shared vocabulary first (2 797 shelf words were
/// translated by hand on 2026-09-07 and never need the network), the provider after. A
/// provider's answer for an English word (one the lexicon knows) is kept in the shared vocabulary
/// as a Machine WordTranslation in the learner's language, so the next lookup of the same word in the same language by anyone costs
/// nothing and the word becomes trainable. A translation already there is never replaced, so a
/// hand-made one always wins. Only a miss is paced — one per user every
/// UncachedTranslationLimiter.Window; a hit is always free.
/// </summary>
public class TranslationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITranslator _translator;
    private readonly UncachedTranslationLimiter _limiter;
    private readonly IEnglishLexicon _lexicon;

    public TranslationService(
        ApplicationDbContext dbContext, ITranslator translator, UncachedTranslationLimiter limiter, IEnglishLexicon lexicon)
    {
        _dbContext = dbContext;
        _translator = translator;
        _limiter = limiter;
        _lexicon = lexicon;
    }

    /// <summary>Expects an already normalized, valid word (see WordText) — the same form the personal dictionary stores.</summary>
    public async Task<TranslationLookup> LookupAsync(
        long userId, string word, LearnerLanguage language, CancellationToken cancellationToken)
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

        if (!_limiter.TryConsume(userId, out var wait))
        {
            return new TranslationLookup(word, null, TranslationSource.RateLimited, UncachedTranslationLimiter.Seconds(wait));
        }

        var translated = await _translator.TranslateAsync(word, language, cancellationToken);

        if (translated == null || translated.Length > WordTranslation.MaxTextLength)
        {
            return new TranslationLookup(word, null, TranslationSource.None);
        }

        // The shared vocabulary is what everybody's lookups and trainings read, so a lookup adds a
        // new word to it only under the rule book import follows: an English lemma. A phrase, an
        // inflected form or a made-up string (where a prompt injection would live) is answered but
        // never kept. A word already there — from an older import, say — is translated in place.
        if (shared == null && !IsImportableLemma(word))
        {
            return new TranslationLookup(word, translated, TranslationSource.Llm);
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

        return new TranslationLookup(word, translated, TranslationSource.Llm);
    }

    private bool IsImportableLemma(string word) => ImportWordText.IsValid(word) && _lexicon.LemmaOf(word) == word;
}
