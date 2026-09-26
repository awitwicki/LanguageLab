namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>Where a verb stands after a run of answers, rebuilt from the log of them.</summary>
public sealed record VerbTally(double Mastery, int Streak, int Answers, int Knows);

/// <summary>
/// Turns one self-assessment click into numbers. The learner's own verdict decides whether
/// the answer counts; how long the click took decides how much it is worth, because an
/// instant "I know" is stronger evidence than the same answer after five seconds of
/// thinking. Two signals come out, each with one job: <see cref="NextMastery"/> feeds the
/// table's colour and the free-training draw, <see cref="NextStreak"/> feeds nothing but
/// batch progression, so advancing stays predictable however fast the learner clicks.
/// </summary>
public static class VerbScoring
{
    /// <summary>At or below this, a "know" is worth full marks.</summary>
    public const int FastMs = 1500;

    /// <summary>At or above this, a "know" is worth <see cref="SlowQuality"/>.</summary>
    public const int SlowMs = 6000;

    public const double SlowQuality = 0.45;

    /// <summary>A card left open for a minute says nothing more than a slow one.</summary>
    public const int MaxResponseMs = 60_000;

    /// <summary>Weight of the newest answer in the moving average.</summary>
    public const double Alpha = 0.4;

    public const int PassStreak = 4;

    /// <summary>Below this, a verb is drawn from free training's weak pool.</summary>
    public const double WeakBelow = 0.7;

    public static double Quality(bool known, int responseMs)
    {
        if (!known)
        {
            return 0;
        }

        var ms = Math.Clamp(responseMs, 0, MaxResponseMs);

        if (ms <= FastMs)
        {
            return 1.0;
        }

        if (ms >= SlowMs)
        {
            return SlowQuality;
        }

        var share = (double)(ms - FastMs) / (SlowMs - FastMs);

        return 1.0 - share * (1.0 - SlowQuality);
    }

    /// <summary>
    /// An exponential moving average, except for the very first answer, which sets the
    /// score outright: averaging it against a zero start would cap an instant first
    /// "I know" at <see cref="Alpha"/>, which reads as failure.
    /// </summary>
    public static double NextMastery(double mastery, int answers, double quality) =>
        answers == 0 ? quality : mastery + Alpha * (quality - mastery);

    public static int NextStreak(int streak, bool known) => known ? streak + 1 : 0;

    public static bool Passed(int streak) => streak >= PassStreak;

    /// <summary>
    /// Folds a verb's whole answer log, oldest first, into the standing it implies. The
    /// stored row is a cache of this: deriving it instead of incrementing it means two
    /// devices answering at once cannot leave the counters permanently behind the log —
    /// the next answer replays everything and lands on the truth.
    /// </summary>
    public static VerbTally Replay(IEnumerable<(bool Known, int ResponseMs)> answers)
    {
        var mastery = 0.0;
        var streak = 0;
        var count = 0;
        var knows = 0;

        foreach (var (known, responseMs) in answers)
        {
            mastery = NextMastery(mastery, count, Quality(known, responseMs));
            streak = NextStreak(streak, known);
            count++;
            knows += known ? 1 : 0;
        }

        return new VerbTally(mastery, streak, count, knows);
    }
}
