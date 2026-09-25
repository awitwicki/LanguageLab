using LanguageLab.Domain;

namespace LanguageLab.Tests;

public class TitleTextTests
{
    [Fact]
    public void A_short_title_is_untouched() => Assert.Equal("Wool", TitleText.Truncate("Wool"));

    [Fact]
    public void A_long_title_is_cut_to_the_maximum() =>
        Assert.Equal(TitleText.MaxLength, TitleText.Truncate(new string('a', TitleText.MaxLength + 50)).Length);
}
