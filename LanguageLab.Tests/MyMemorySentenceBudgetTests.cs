using LanguageLab.Application.Translation;
using Microsoft.Extensions.Options;

namespace LanguageLab.Tests;

public class MyMemorySentenceBudgetTests
{
    private static readonly DateTime Day1 = new(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day1Later = new(2026, 9, 24, 23, 59, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = new(2026, 9, 25, 0, 0, 1, DateTimeKind.Utc);

    private static MyMemorySentenceBudget Budget(string? email = null) =>
        new(Options.Create(new TranslationOptions { MyMemoryEmail = email }));

    [Fact]
    public void Without_an_email_the_daily_limit_is_two_thousand_characters()
    {
        var budget = Budget();

        Assert.True(budget.TryConsume(2_000, Day1));
        Assert.False(budget.TryConsume(1, Day1));
    }

    [Fact]
    public void With_an_email_the_daily_limit_is_twenty_thousand_characters()
    {
        var budget = Budget("me@example.com");

        Assert.True(budget.TryConsume(20_000, Day1));
        Assert.False(budget.TryConsume(1, Day1));
    }

    [Fact]
    public void Characters_accumulate_across_calls_until_the_limit_is_reached()
    {
        var budget = Budget();

        Assert.True(budget.TryConsume(1_200, Day1));
        Assert.True(budget.TryConsume(800, Day1Later));
        Assert.False(budget.TryConsume(1, Day1Later));
    }

    [Fact]
    public void The_budget_resets_on_the_next_utc_day()
    {
        var budget = Budget();

        Assert.True(budget.TryConsume(2_000, Day1));
        Assert.False(budget.TryConsume(1, Day1Later));

        Assert.True(budget.TryConsume(2_000, Day2));
    }
}
