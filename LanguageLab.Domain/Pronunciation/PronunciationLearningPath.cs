namespace LanguageLab.Domain.Pronunciation;

public enum FamilyStatus
{
    Available,
    Done,
}

public sealed record FamilyProgress(SoundFamily Family, int Total, int Mastered, FamilyStatus Status);

/// <summary>
/// Every sound family is open from the start — the path tells the done ones apart, it
/// gates nothing. Done = at least 80% of the family is Mastered; a family drops back to
/// Available when enough of its words regress. Mirrors the irregular-verbs trainer's
/// LearningPath one-for-one.
/// </summary>
public static class PronunciationLearningPath
{
    public const double DoneShare = 0.8;

    public static IReadOnlyList<FamilyProgress> Evaluate(IReadOnlyDictionary<string, PronunciationState> states) =>
        PronunciationCatalog.Families
            .Select(family =>
            {
                var words = PronunciationCatalog.WordsOf(family.Key);
                var mastered = words.Count(w => StateOf(states, w) == PronunciationState.Mastered);
                var done = mastered >= Math.Ceiling(words.Count * DoneShare);

                return new FamilyProgress(family, words.Count, mastered, done ? FamilyStatus.Done : FamilyStatus.Available);
            })
            .ToList();

    private static PronunciationState StateOf(IReadOnlyDictionary<string, PronunciationState> states, PronunciationWord word) =>
        states.TryGetValue(word.Word, out var s) ? s : PronunciationState.New;
}
