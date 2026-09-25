namespace LanguageLab.Domain.IrregularVerbs;

public enum FamilyStatus
{
    Available,
    Done,
}

public sealed record FamilyProgress(VerbFamily Family, int Total, int Learned, FamilyStatus Status);

/// <summary>
/// Every family is open from the first session — the path tells the done ones apart, it
/// gates nothing. Done = at least 80 % of the family is Learned; a family drops back to
/// Available when enough of its verbs are forgotten.
/// </summary>
public static class LearningPath
{
    public const double DoneShare = 0.8;
    public const int FamiliesForMixed = 2;

    public static IReadOnlyList<FamilyProgress> Evaluate(IReadOnlyDictionary<string, VerbState> states) =>
        IrregularVerbCatalog.Families
            .Select(family =>
            {
                var verbs = IrregularVerbCatalog.VerbsOf(family.Key);
                var learned = verbs.Count(v => StateOf(states, v) == VerbState.Learned);
                var done = learned >= Math.Ceiling(verbs.Count * DoneShare);

                return new FamilyProgress(family, verbs.Count, learned, done ? FamilyStatus.Done : FamilyStatus.Available);
            })
            .ToList();

    public static bool MixedAvailable(IReadOnlyList<FamilyProgress> path) =>
        path.Count(f => f.Status == FamilyStatus.Done) >= FamiliesForMixed;

    private static VerbState StateOf(IReadOnlyDictionary<string, VerbState> states, IrregularVerb verb) =>
        states.TryGetValue(verb.V1, out var s) ? s : VerbState.New;
}
