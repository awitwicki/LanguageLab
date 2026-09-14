namespace LanguageLab.Domain.Training;

/// <summary>
/// Classic five-box Leitner. Pure: time comes from outside, there is no state.
/// Grading applies once per session per word, on the aggregate of all answers to it.
/// </summary>
public static class LeitnerScheduler
{
    public const int MinBox = 1;
    public const int MaxBox = 5;

    /// <summary>How many days to wait before the next showing. Index = Box - 1.</summary>
    public static readonly IReadOnlyList<int> IntervalDays = Array.AsReadOnly(new[] { 1, 3, 7, 14, 30 });

    public static LeitnerOutcome Grade(int box, bool allCorrect, DateTime nowUtc)
    {
        if (box is < MinBox or > MaxBox)
        {
            throw new ArgumentOutOfRangeException(
                nameof(box), box, $"Box must be within {MinBox}..{MaxBox}.");
        }

        if (!allCorrect)
        {
            var demoted = Math.Max(MinBox, box - 1);
            return new LeitnerOutcome(demoted, nowUtc.AddDays(1), IsLearned: false);
        }

        if (box == MaxBox)
        {
            return new LeitnerOutcome(MaxBox, DueAt: null, IsLearned: true);
        }

        var promoted = box + 1;
        return new LeitnerOutcome(promoted, nowUtc.AddDays(IntervalDays[promoted - 1]), IsLearned: false);
    }
}
