using LanguageLab.Domain.Entities;
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
/// provider's answer is kept in the shared vocabulary — a new shared row, or the empty
/// translation of an existing one — marked Machine, so the next lookup of the same word by
/// anyone costs nothing and the word becomes trainable. A translation already there is never
/// replaced, so a hand-made one always wins.
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
        var shared = await _dbContext.Words
            .FirstOrDefaultAsync(w => w.OwnerId == null && w.Word == word, cancellationToken);

        if (shared is { Translation.Length: > 0 })
        {
            return new TranslationLookup(word, shared.Translation, TranslationSource.Dictionary);
        }

        var translated = await _translator.TranslateAsync(word, cancellationToken);

        if (translated == null)
        {
            return new TranslationLookup(word, null, TranslationSource.None);
        }

        if (shared == null)
        {
            shared = new WordPair { Word = word, Translation = translated, TranslationOrigin = TranslationOrigin.Machine };
            _dbContext.Words.Add(shared);
        }
        else
        {
            shared.Translation = translated;
            shared.TranslationOrigin = TranslationOrigin.Machine;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two first lookups of the same word raced and the (Word, OwnerId) index let one
            // through. The winner's row is as good as ours: drop ours and answer all the same.
            _dbContext.Entry(shared).State = EntityState.Detached;
        }

        return new TranslationLookup(word, translated, TranslationSource.MyMemory);
    }
}
