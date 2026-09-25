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
}
