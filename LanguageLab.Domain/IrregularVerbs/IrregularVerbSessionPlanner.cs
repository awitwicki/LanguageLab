using LanguageLab.Domain.Entities;

namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// Picks one session's verbs for a step, weakest first: verbs with a missed form, then verbs
/// never graded (both in table order, so new material arrives in its families), then verbs
/// in progress by total streak and by how long ago they were graded. Learned verbs are left
/// out — until the step has nothing else, when the session becomes a review of the
/// longest-unanswered verbs. Deterministic: the open card is the verb's strongest form.
/// </summary>
public static class IrregularVerbSessionPlanner
{
    public const int SessionSize = 10;

    public static SessionPlan Plan(int step, IReadOnlyList<IrregularVerbFormProgress> rows)
    {
        if (!IrregularVerbTable.Steps.TryGetValue(step, out var verbs))
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "Step must be 1..4.");
        }

        var progress = verbs.Select((verb, index) => (Index: index, Progress: VerbProgress.Of(verb, rows))).ToList();

        var toLearn = progress
            .Where(x => !x.Progress.IsLearned)
            .OrderBy(x => Key(x.Progress))
            .ThenBy(x => x.Index)
            .Take(SessionSize)
            .Select(x => Card(x.Progress))
            .ToList();

        if (toLearn.Count > 0)
        {
            return new SessionPlan(IsReview: false, toLearn);
        }

        var review = progress
            .OrderBy(x => x.Progress.LastAnsweredAt ?? DateTime.MinValue)
            .ThenBy(x => x.Index)
            .Take(SessionSize)
            .Select(x => Card(x.Progress))
            .ToList();

        return new SessionPlan(IsReview: true, review);
    }

    // Tier first; streak and age only matter inside the in-progress tier — a missed verb may
    // still carry a learned form, and that must not push it behind the unseen ones.
    private static (int Tier, int StreakSum, DateTime Last) Key(VerbProgress p)
    {
        if (p.HasMiss)
        {
            return (0, 0, DateTime.MinValue);
        }

        if (!p.HasRows)
        {
            return (1, 0, DateTime.MinValue);
        }

        return (2, p.StreakSum, p.LastAnsweredAt ?? DateTime.MinValue);
    }

    private static PlannedCard Card(VerbProgress p) => new(p.Verb, p.OpenForm);
}
