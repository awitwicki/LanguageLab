using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class VerbScoringTests
{
    [Fact]
    public void Not_knowing_scores_zero_however_fast_the_click_was()
    {
        Assert.Equal(0, VerbScoring.Quality(known: false, responseMs: 200));
        Assert.Equal(0, VerbScoring.Quality(known: false, responseMs: 30_000));
    }

    [Fact]
    public void An_instant_know_scores_full_marks_up_to_the_fast_threshold()
    {
        Assert.Equal(1.0, VerbScoring.Quality(known: true, responseMs: 0));
        Assert.Equal(1.0, VerbScoring.Quality(known: true, responseMs: VerbScoring.FastMs));
    }

    [Fact]
    public void A_slow_know_bottoms_out_at_the_slow_quality()
    {
        Assert.Equal(VerbScoring.SlowQuality, VerbScoring.Quality(known: true, responseMs: VerbScoring.SlowMs));
        Assert.Equal(VerbScoring.SlowQuality, VerbScoring.Quality(known: true, responseMs: 20_000));
    }

    [Fact]
    public void Between_the_thresholds_quality_falls_off_linearly()
    {
        // Halfway between 1500 ms and 6000 ms is halfway between 1.0 and 0.45.
        Assert.Equal(0.725, VerbScoring.Quality(known: true, responseMs: 3750), precision: 10);
    }

    /// <summary>Review focus 1: a tab left open, or a clock that jumped backwards.</summary>
    [Theory]
    [InlineData(-5000, 1.0)]
    [InlineData(int.MinValue, 1.0)]
    [InlineData(int.MaxValue, VerbScoring.SlowQuality)]
    public void An_absurd_response_time_is_clamped_rather_than_trusted(int responseMs, double expected)
    {
        var quality = VerbScoring.Quality(known: true, responseMs);

        Assert.Equal(expected, quality);
        Assert.InRange(quality, 0, 1);
    }

    [Fact]
    public void The_first_answer_sets_mastery_outright()
    {
        // Seeding the average from zero would cap a first instant "I know" at alpha.
        Assert.Equal(1.0, VerbScoring.NextMastery(mastery: 0, answers: 0, quality: 1.0));
        Assert.Equal(0, VerbScoring.NextMastery(mastery: 0, answers: 0, quality: 0));
    }

    [Fact]
    public void Later_answers_move_mastery_by_alpha_towards_the_new_quality()
    {
        Assert.Equal(0.7, VerbScoring.NextMastery(mastery: 0.5, answers: 1, quality: 1.0), precision: 10);
        Assert.Equal(0.3, VerbScoring.NextMastery(mastery: 0.5, answers: 7, quality: 0), precision: 10);
    }

    [Fact]
    public void Mastery_stays_inside_the_unit_range_over_a_long_run()
    {
        var mastery = 0.0;

        for (var i = 0; i < 200; i++)
        {
            var known = i % 3 != 0;
            mastery = VerbScoring.NextMastery(mastery, answers: i, VerbScoring.Quality(known, responseMs: 900));
            Assert.InRange(mastery, 0, 1);
        }
    }

    [Fact]
    public void A_streak_counts_knows_and_any_miss_resets_it()
    {
        Assert.Equal(3, VerbScoring.NextStreak(2, known: true));
        Assert.Equal(0, VerbScoring.NextStreak(9, known: false));
    }

    /// <summary>
    /// The answer log is the source of truth, so a standing has to be reconstructible from
    /// it — replaying has to land exactly where answering one at a time does.
    /// </summary>
    [Fact]
    public void Replaying_an_empty_log_is_a_standing_at_zero()
    {
        Assert.Equal(new VerbTally(0, 0, 0, 0), VerbScoring.Replay([]));
    }

    [Fact]
    public void Replaying_counts_answers_knows_and_the_trailing_streak()
    {
        var tally = VerbScoring.Replay([(true, 500), (false, 500), (true, 500), (true, 500)]);

        Assert.Equal(4, tally.Answers);
        Assert.Equal(3, tally.Knows);
        Assert.Equal(2, tally.Streak); // the miss reset it; two knows since
    }

    [Fact]
    public void Replaying_lands_where_answering_one_at_a_time_lands()
    {
        (bool Known, int ResponseMs)[] log = [(true, 400), (false, 800), (true, 3750), (true, 200), (true, 9000)];

        var mastery = 0.0;
        var streak = 0;

        for (var i = 0; i < log.Length; i++)
        {
            mastery = VerbScoring.NextMastery(mastery, i, VerbScoring.Quality(log[i].Known, log[i].ResponseMs));
            streak = VerbScoring.NextStreak(streak, log[i].Known);
        }

        var tally = VerbScoring.Replay(log);

        Assert.Equal(mastery, tally.Mastery, precision: 10);
        Assert.Equal(streak, tally.Streak);
        Assert.Equal(log.Length, tally.Answers);
        Assert.Equal(4, tally.Knows);
    }

    [Fact]
    public void A_verb_passes_on_the_fourth_know_in_a_row()
    {
        Assert.False(VerbScoring.Passed(3));
        Assert.True(VerbScoring.Passed(4));
        Assert.True(VerbScoring.Passed(12));
    }
}
