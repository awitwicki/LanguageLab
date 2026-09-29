using LanguageLab.Domain.Languages;

namespace LanguageLab.Tests;

public class LearnerLanguagesTests
{
    [Fact]
    public void Russian_and_English_are_never_offered()
    {
        Assert.DoesNotContain(LearnerLanguages.All, l => l.Code is "ru" or "en");
        Assert.Null(LearnerLanguages.Find("ru"));
    }

    [Fact]
    public void Ukrainian_is_first_and_the_default_and_Filipino_is_offered()
    {
        Assert.Equal("uk", LearnerLanguages.All[0].Code);
        Assert.Equal("uk", LearnerLanguages.Default.Code);
        Assert.NotNull(LearnerLanguages.Find("tl"));
    }

    [Fact]
    public void Codes_are_unique_lowercase_and_short_and_every_name_is_set()
    {
        Assert.Equal(LearnerLanguages.All.Count, LearnerLanguages.All.Select(l => l.Code).Distinct().Count());

        foreach (var language in LearnerLanguages.All)
        {
            Assert.Equal(language.Code.ToLowerInvariant(), language.Code);
            Assert.InRange(language.Code.Length, 2, LearnerLanguages.CodeMaxLength);
            Assert.False(string.IsNullOrWhiteSpace(language.EnglishName));
            Assert.False(string.IsNullOrWhiteSpace(language.NativeName));
        }
    }

    [Theory]
    [InlineData("uk", "uk")]
    [InlineData("pl", "pl")]
    [InlineData("pt-br", "pt")]
    [InlineData("PT_BR", "pt")]
    [InlineData("fil", "tl")]
    [InlineData("tl", "tl")]
    [InlineData("no", "nb")]
    [InlineData("ru", null)]
    [InlineData("en-US", null)]
    [InlineData("xx", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Telegram_codes_map_to_a_catalog_entry_or_nothing(string? telegram, string? expected)
    {
        Assert.Equal(expected, LearnerLanguages.FromTelegram(telegram)?.Code);
    }
}
