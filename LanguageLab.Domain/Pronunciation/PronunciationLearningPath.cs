namespace LanguageLab.Domain.Pronunciation;

public enum FamilyStatus
{
    Locked,
    Available,
    Done,
}

public sealed record FamilyProgress(SoundFamily Family, int Total, int Mastered, bool Started, FamilyStatus Status);

/// <summary>
/// Which sound families a learner may practice. Done = at least 80% of the family is
/// Mastered; the next family opens once the previous one is done and never locks again —
/// a family with any word past New stays available even if the one before it regressed.
/// Mirrors the irregular-verbs trainer's LearningPath one-for-one.
/// </summary>
public static class PronunciationLearningPath
{
    public const double DoneShare = 0.8;

    public static IReadOnlyList<FamilyProgress> Evaluate(IReadOnlyDictionary<string, PronunciationState> states)
    {
        var result = new List<FamilyProgress>();
        var previousDone = true;

        foreach (var family in PronunciationCatalog.Families)
        {
            var words = PronunciationCatalog.WordsOf(family.Key);
            var mastered = words.Count(w => StateOf(states, w) == PronunciationState.Mastered);
            var started = words.Any(w => StateOf(states, w) != PronunciationState.New);
            var done = mastered >= Math.Ceiling(words.Count * DoneShare);

            var status = done ? FamilyStatus.Done
                : previousDone || started ? FamilyStatus.Available
                : FamilyStatus.Locked;

            result.Add(new FamilyProgress(family, words.Count, mastered, started, status));
            previousDone = done;
        }

        return result;
    }

    private static PronunciationState StateOf(IReadOnlyDictionary<string, PronunciationState> states, PronunciationWord word) =>
        states.TryGetValue(word.Word, out var s) ? s : PronunciationState.New;
}
