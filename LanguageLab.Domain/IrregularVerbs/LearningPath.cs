namespace LanguageLab.Domain.IrregularVerbs;

public enum FamilyStatus
{
    Locked,
    Available,
    Done,
}

public sealed record FamilyProgress(VerbFamily Family, int Total, int Learned, bool Started, FamilyStatus Status);

/// <summary>
/// Which families a learner may train. Done = at least 80 % of the family is Learned; the
/// next family opens once the previous one is done and never locks again — a family
/// with any verb past New stays available even if the one before it regressed.
/// </summary>
public static class LearningPath
{
    public const double DoneShare = 0.8;
    public const int FamiliesForMixed = 2;

    public static IReadOnlyList<FamilyProgress> Evaluate(IReadOnlyDictionary<string, VerbState> states)
    {
        var result = new List<FamilyProgress>();
        var previousDone = true;

        foreach (var family in IrregularVerbCatalog.Families)
        {
            var verbs = IrregularVerbCatalog.VerbsOf(family.Key);
            var learned = verbs.Count(v => StateOf(states, v) == VerbState.Learned);
            var started = verbs.Any(v => StateOf(states, v) != VerbState.New);
            var done = learned >= Math.Ceiling(verbs.Count * DoneShare);

            var status = done ? FamilyStatus.Done
                : previousDone || started ? FamilyStatus.Available
                : FamilyStatus.Locked;

            result.Add(new FamilyProgress(family, verbs.Count, learned, started, status));
            previousDone = done;
        }

        return result;
    }

    public static bool MixedAvailable(IReadOnlyList<FamilyProgress> path) =>
        path.Count(f => f.Status == FamilyStatus.Done) >= FamiliesForMixed;

    private static VerbState StateOf(IReadOnlyDictionary<string, VerbState> states, IrregularVerb verb) =>
        states.TryGetValue(verb.V1, out var s) ? s : VerbState.New;
}
