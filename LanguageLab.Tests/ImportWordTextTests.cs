using LanguageLab.Domain;

namespace LanguageLab.Tests;

public class ImportWordTextTests
{
    [Theory]
    [InlineData("silo")]
    [InlineData("abide")]
    public void Plain_lowercase_words_are_valid(string word) => Assert.True(ImportWordText.IsValid(word));

    [Theory]
    [InlineData("")]
    [InlineData("go")]                 // shorter than the tokenizer ever emits
    [InlineData("Silo")]               // not normalized
    [InlineData("don't")]              // the tokenizer keeps apostrophes out of lemmas
    [InlineData("give up")]            // a space is a phrase, not a book word
    [InlineData("слово")]              // non-Latin
    [InlineData("word‮reversed")] // bidi override
    [InlineData("🙂🙂🙂")]
    public void Anything_the_tokenizer_never_emits_is_invalid(string word) =>
        Assert.False(ImportWordText.IsValid(word));

    [Fact]
    public void A_word_longer_than_the_maximum_is_invalid() =>
        Assert.False(ImportWordText.IsValid(new string('a', ImportWordText.MaxLength + 1)));
}
