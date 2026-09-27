namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// Ordinary training walks a stage five words at a time: the window is simply the first
/// five verbs of the stage that have not passed yet, so it slides forward only as its own
/// words are learned — a new batch arrives when the previous one is done, and a verb that
/// regresses drops back into it.
/// </summary>
public static class BatchWindow
{
    public const int Size = 5;

    public static IReadOnlyList<VerbStanding> Of(IReadOnlyList<VerbStanding> stage) =>
        stage.Where(s => !VerbScoring.Passed(s.Streak)).Take(Size).ToList();

    /// <summary>
    /// The window in the order a session should take it: never answered first, then the
    /// lowest streak, then catalog order. A session takes the first N of this, so shortening
    /// it to three words keeps the three the learner knows least rather than the first three
    /// the catalog happens to list.
    /// </summary>
    public static IReadOnlyList<VerbStanding> Ordered(IReadOnlyList<VerbStanding> stage) =>
        Of(stage)
            .Select((standing, order) => (standing, order))
            .OrderBy(x => x.standing.Answers == 0 ? 0 : 1)
            .ThenBy(x => x.standing.Streak)
            .ThenBy(x => x.order)
            .Select(x => x.standing)
            .ToList();
}
