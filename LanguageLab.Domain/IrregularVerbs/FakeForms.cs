namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// Made-up forms of a verb for the drill's wrong options — the mistakes a learner who
/// regularises an irregular verb actually makes (<c>put → putted</c>, <c>bring → brang</c>).
/// Rule-generated, never hand-written per verb, and never a real form of the verb itself.
/// </summary>
public static class FakeForms
{
    /// <summary>
    /// Real English words the rules would produce, which would read as wrong for a reason
    /// other than the verb form: <c>costed</c> is a real past of <i>cost</i> ("estimate").
    /// </summary>
    public static readonly IReadOnlySet<string> Excluded = new HashSet<string>(StringComparer.Ordinal)
    {
        "costed", "thank", "bed", "seed", "singed", "ringed", "haven", "leaven",
    };

    /// <summary>Two-syllable verbs stressed on the last syllable, which double like monosyllables.</summary>
    private static readonly IReadOnlySet<string> DoublesFinal = new HashSet<string>(StringComparer.Ordinal)
    {
        "begin", "forget",
    };

    /// <summary>+ed, then +en, then the i → a / i → u swap of an -ing / -ink verb.</summary>
    public static IReadOnlyList<string> Of(IrregularVerb verb)
    {
        var v1 = verb.V1;
        var candidates = new List<string> { Suffixed(v1, "ed"), Suffixed(v1, "en") };

        if (v1.EndsWith("ing", StringComparison.Ordinal) || v1.EndsWith("ink", StringComparison.Ordinal))
        {
            var stem = v1[..^3];
            var tail = v1[^2..];

            candidates.Add(stem + "a" + tail);
            candidates.Add(stem + "u" + tail);
        }

        var real = verb.V2.Concat(verb.V3).Append(v1).ToHashSet(StringComparer.Ordinal);

        return candidates
            .Where(c => !real.Contains(c) && !Excluded.Contains(c))
            .Distinct()
            .ToList();
    }

    private static string Suffixed(string v1, string suffix)
    {
        if (v1.EndsWith('e'))
        {
            return v1 + suffix[1..];
        }

        if (v1.Length > 1 && v1[^1] == 'y' && !IsVowel(v1[^2]))
        {
            return v1[..^1] + "i" + suffix;
        }

        return Doubles(v1) ? v1 + v1[^1] + suffix : v1 + suffix;
    }

    /// <summary>A one-syllable consonant–vowel–consonant ending doubles its consonant: put → putted.</summary>
    private static bool Doubles(string v1) =>
        DoublesFinal.Contains(v1)
        || (v1.Length >= 3
            && VowelGroups(v1) == 1
            && !IsVowel(v1[^1]) && !"wxy".Contains(v1[^1])
            && IsVowel(v1[^2])
            && !IsVowel(v1[^3]));

    private static int VowelGroups(string word)
    {
        var groups = 0;

        for (var i = 0; i < word.Length; i++)
        {
            if (IsVowel(word[i]) && (i == 0 || !IsVowel(word[i - 1])))
            {
                groups++;
            }
        }

        return groups;
    }

    private static bool IsVowel(char c) => "aeiou".Contains(c);
}
