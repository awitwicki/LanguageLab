using LanguageLab.Domain.Entities;

namespace LanguageLab.Tests;

/// <summary>
/// Seeds a word with its Ukrainian translation as a WordTranslation row. A null or empty uk
/// seeds an untranslated word.
/// </summary>
public static class TestWords
{
    public const string Uk = "uk";

    public static WordPair Pair(long id, string word, string? uk, long? ownerId = null)
    {
        var pair = new WordPair { Id = id, Word = word, OwnerId = ownerId };

        if (!string.IsNullOrEmpty(uk))
        {
            pair.Translations.Add(new WordTranslation { Language = Uk, Text = uk });
        }

        return pair;
    }

    /// <summary>The same, for tests that let the database assign the id.</summary>
    public static WordPair Pair(string word, string? uk, long? ownerId = null) => Pair(0, word, uk, ownerId);
}
