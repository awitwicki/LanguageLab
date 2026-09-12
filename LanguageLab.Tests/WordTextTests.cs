using LanguageLab.Domain;

namespace LanguageLab.Tests;

public class WordTextTests
{
    [Theory]
    [InlineData("  Apple ", "apple")]
    [InlineData("Give   Up", "give up")]
    [InlineData("\tMOTHER-IN-LAW\n", "mother-in-law")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_trims_collapses_whitespace_and_lowercases(string raw, string expected)
    {
        Assert.Equal(expected, WordText.Normalize(raw));
    }

    [Theory]
    [InlineData("apple", true)]
    [InlineData("give up", true)]
    [InlineData("mother-in-law", true)]
    [InlineData("o'clock", true)]
    [InlineData("o\u2019clock", true)]
    [InlineData("", false)]
    [InlineData("a1", false)]
    [InlineData("hello!", false)]
    [InlineData("яблуко", true)]
    public void IsValid_accepts_letters_spaces_hyphens_and_apostrophes(string word, bool expected)
    {
        Assert.Equal(expected, WordText.IsValid(word));
    }

    [Fact]
    public void IsValid_caps_the_length_at_64()
    {
        Assert.True(WordText.IsValid(new string('a', 64)));
        Assert.False(WordText.IsValid(new string('a', 65)));
    }
}
