using LanguageLab.Application.Translation;
using Microsoft.Extensions.Options;

namespace LanguageLab.Tests;

public class MyMemoryWordBudgetTests
{
    private static readonly DateTime Day1 = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = new(2026, 9, 26, 0, 0, 1, DateTimeKind.Utc);

    private static MyMemoryWordBudget Budget(string? email = null) =>
        new(Options.Create(new TranslationOptions { MyMemoryEmail = email }));

    [Fact]
    public void Without_an_email_words_get_sixty_percent_of_five_thousand()
    {
        var budget = Budget();

        Assert.True(budget.TryConsume(3_000, Day1));
        Assert.False(budget.TryConsume(1, Day1));
    }

    [Fact]
    public void With_an_email_words_get_sixty_percent_of_fifty_thousand()
    {
        var budget = Budget("me@example.com");

        Assert.True(budget.TryConsume(30_000, Day1));
        Assert.False(budget.TryConsume(1, Day1));
    }

    [Fact]
    public void The_budget_resets_on_the_next_utc_day()
    {
        var budget = Budget();

        Assert.True(budget.TryConsume(3_000, Day1));
        Assert.True(budget.TryConsume(3_000, Day2));
    }

    [Fact]
    public void A_refused_request_spends_nothing()
    {
        var budget = Budget();

        Assert.False(budget.TryConsume(3_001, Day1));
        Assert.True(budget.TryConsume(3_000, Day1));
    }

    [Fact]
    public void The_two_budgets_together_stay_inside_the_providers_day()
    {
        Assert.Equal(
            MyMemoryDailyLimits.Anonymous,
            (int)(MyMemoryDailyLimits.Anonymous * (MyMemorySentenceBudget.Share + MyMemoryWordBudget.Share)));
    }
}
