namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// Free training draws at random, but not evenly: about two cards in three come from what
/// the learner knows badly and the rest from what they know well, so a run keeps hammering
/// the weak words without ever letting the strong ones rot. A verb never answered counts as
/// weak, which makes a fresh scope behave as plain random.
/// </summary>
public static class FreePicker
{
    public const double WeakShare = 0.65;

    public static bool IsWeak(VerbStanding standing) =>
        standing.Answers == 0 || standing.Mastery < VerbScoring.WeakBelow;

    public static VerbStanding? Next(IReadOnlyList<VerbStanding> scope, string? exclude, Random random)
    {
        var pool = scope.Count > 1
            ? scope.Where(s => !string.Equals(s.Verb, exclude, StringComparison.Ordinal)).ToList()
            : scope;

        if (pool.Count == 0)
        {
            return null;
        }

        var weak = pool.Where(IsWeak).ToList();
        var strong = pool.Where(s => !IsWeak(s)).ToList();

        // An empty pool hands its turn to the other one rather than serving nothing.
        var fromWeak = weak.Count > 0 && (strong.Count == 0 || random.NextDouble() < WeakShare);
        var chosen = fromWeak ? weak : strong;

        return chosen[random.Next(chosen.Count)];
    }

    /// <summary>
    /// A whole chunk of a free run, drawn in one go so the browser can play it without asking
    /// again: <paramref name="count"/> picks, each one <see cref="Next"/> with the previous
    /// pick held out, so no verb lands twice in a row. A scope of one verb has nothing to
    /// alternate with and is drawn over and over; an empty scope draws nothing.
    /// </summary>
    public static IReadOnlyList<VerbStanding> Draw(
        IReadOnlyList<VerbStanding> scope, int count, Random random)
    {
        var drawn = new List<VerbStanding>(count);
        string? previous = null;

        for (var i = 0; i < count; i++)
        {
            var pick = Next(scope, previous, random);

            if (pick == null)
            {
                break;
            }

            drawn.Add(pick);
            previous = pick.Verb;
        }

        return drawn;
    }
}
