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
    /// The window's weakest verb — lowest mastery, then fewest answers, then catalog order
    /// — skipping the one just answered unless it is all that is left. Null once the whole
    /// stage has passed.
    /// </summary>
    public static VerbStanding? Next(IReadOnlyList<VerbStanding> stage, string? exclude)
    {
        var window = Of(stage);

        if (window.Count == 0)
        {
            return null;
        }

        var pool = window.Count > 1
            ? window.Where(s => !string.Equals(s.Verb, exclude, StringComparison.Ordinal)).ToList()
            : window;

        return pool
            .Select((standing, order) => (standing, order))
            .OrderBy(x => x.standing.Mastery)
            .ThenBy(x => x.standing.Answers)
            .ThenBy(x => x.order)
            .First()
            .standing;
    }
}
