using LanguageLab.Domain.Pronunciation;

namespace LanguageLab.Tests;

public class PronunciationCatalogTests
{
    private static readonly string AudioDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "web", "public", "pronunciation-audio");

    [Fact]
    public void Every_family_has_at_least_one_word()
    {
        foreach (var family in PronunciationCatalog.Families)
        {
            Assert.NotEmpty(PronunciationCatalog.WordsOf(family.Key));
        }
    }

    [Fact]
    public void No_word_appears_in_two_families()
    {
        var duplicates = PronunciationCatalog.Words
            .GroupBy(w => w.Word)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_word_belongs_to_a_family_that_exists()
    {
        var familyKeys = PronunciationCatalog.Families.Select(f => f.Key).ToHashSet();

        foreach (var word in PronunciationCatalog.Words)
        {
            Assert.Contains(word.FamilyKey, familyKeys);
        }
    }

    [Fact]
    public void Every_word_has_both_accent_audio_files_on_disk()
    {
        foreach (var word in PronunciationCatalog.Words)
        {
            Assert.True(File.Exists(Path.Combine(AudioDir, word.AudioUsFile)), $"missing {word.AudioUsFile}");
            Assert.True(File.Exists(Path.Combine(AudioDir, word.AudioUkFile)), $"missing {word.AudioUkFile}");
        }
    }

    [Fact]
    public void Find_is_case_insensitive_and_returns_null_for_unknown_words()
    {
        var firstWord = PronunciationCatalog.Words.First().Word;

        Assert.NotNull(PronunciationCatalog.Find(firstWord));
        Assert.NotNull(PronunciationCatalog.Find(firstWord.ToUpperInvariant()));
        Assert.Null(PronunciationCatalog.Find("not-a-real-catalog-word-xyz"));
    }

    [Fact]
    public void WordsOf_returns_empty_for_an_unknown_family()
    {
        Assert.Empty(PronunciationCatalog.WordsOf("not-a-real-family-xyz"));
    }

    [Fact]
    public void FamilyByKey_returns_null_for_an_unknown_family()
    {
        Assert.Null(PronunciationCatalog.FamilyByKey("not-a-real-family-xyz"));
    }
}
