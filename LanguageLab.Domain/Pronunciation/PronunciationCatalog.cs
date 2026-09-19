namespace LanguageLab.Domain.Pronunciation;

/// <summary>One catalog word: the target of a practice attempt in one sound family.</summary>
public sealed record PronunciationWord(string Word, string Ipa, string FamilyKey, string AudioUsFile, string AudioUkFile);

/// <summary>One target-sound group the learner progresses through, in catalog order.</summary>
public sealed record SoundFamily(string Key, string Title, IReadOnlyList<string> TargetSounds);

/// <summary>
/// Static data — words are never database rows, same as the irregular-verbs catalog.
/// Families and Words are declared in PronunciationCatalog.Generated.cs, produced by
/// generate_pronunciation_catalog.py; this file only adds lookup helpers on top and is
/// never regenerated.
/// </summary>
public static partial class PronunciationCatalog
{
    public static PronunciationWord? Find(string word) =>
        Words.FirstOrDefault(w => string.Equals(w.Word, word, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<PronunciationWord> WordsOf(string familyKey) =>
        Words.Where(w => w.FamilyKey == familyKey).ToList();

    public static SoundFamily? FamilyByKey(string key) =>
        Families.FirstOrDefault(f => f.Key == key);
}
