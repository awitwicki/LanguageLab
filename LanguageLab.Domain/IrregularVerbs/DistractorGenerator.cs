namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// Wrong options that look like real learner mistakes rather than random words: the
/// regular -ed form, the other past form (went for gone), a neighbour from the same
/// family (brought for bought), the hand-picked confusables, and only then forms from
/// other groups. Group 1 has no meaningful family neighbours — its verbs never change —
/// so it skips straight to confusables and other groups.
/// </summary>
public static class DistractorGenerator
{
    private const string Vowels = "aeiou";

    /// <summary>
    /// The regular past form a learner would build by the spelling rules: cut → cutted
    /// (a short vowel before a final consonant doubles it), leave → leaved, fly → flied,
    /// cost → costed. "be" would give "bed", a real word — it gets "beed" instead.
    /// </summary>
    public static string EdForm(string v1)
    {
        if (v1 == "be")
        {
            return "beed";
        }

        if (v1.EndsWith('e'))
        {
            return v1 + "d";
        }

        if (v1.EndsWith('y') && v1.Length > 1 && !Vowels.Contains(v1[^2]))
        {
            return v1[..^1] + "ied";
        }

        if (DoublesFinalConsonant(v1))
        {
            return v1 + v1[^1] + "ed";
        }

        return v1 + "ed";
    }

    /// <summary>Consonant + single vowel + consonant ending (cut, swim, begin), except w / x / y.</summary>
    private static bool DoublesFinalConsonant(string v1) =>
        v1.Length >= 3
        && !Vowels.Contains(v1[^1]) && !"wxy".Contains(v1[^1])
        && Vowels.Contains(v1[^2])
        && !Vowels.Contains(v1[^3]);

    /// <summary>Every regular spelling a learner might try, doubled consonant included (swim → swimmed).</summary>
    public static bool IsEdForm(IrregularVerb verb, string answer)
    {
        var v1 = verb.V1;
        var candidates = new HashSet<string>(StringComparer.Ordinal) { v1 + "ed", v1 + "d", v1 + v1[^1] + "ed", EdForm(v1) };

        return candidates.Contains(answer);
    }

    /// <summary>Up to count distractors for the asked form, typical mistakes first, never an accepted form.</summary>
    public static IReadOnlyList<string> For(IrregularVerb verb, FormAsked form, int count, Random rng)
    {
        var accepted = new HashSet<string>(verb.Forms(form), StringComparer.Ordinal);
        var result = new List<string>();

        void Add(string candidate)
        {
            if (result.Count < count && !accepted.Contains(candidate) && !result.Contains(candidate))
            {
                result.Add(candidate);
            }
        }

        var swap = form == FormAsked.V3 ? verb.V2 : verb.V3;

        // Group 2 in the perfect: "has came" is the mistake to catch, so the swap leads.
        if (verb.Group == 2)
        {
            foreach (var s in swap) Add(s);
        }

        Add(EdForm(verb.V1));

        if (verb.Group != 1)
        {
            foreach (var s in swap) Add(s);

            foreach (var neighbour in Shuffle(IrregularVerbCatalog.VerbsOf(verb.Family).Where(v => v.V1 != verb.V1), rng))
            {
                foreach (var f in neighbour.Forms(form)) Add(f);
            }
        }

        foreach (var c in verb.Confusables ?? []) Add(c);

        foreach (var other in Shuffle(IrregularVerbCatalog.Verbs.Where(v => v.Group != verb.Group), rng))
        {
            foreach (var f in other.Forms(form)) Add(f);
        }

        return result;
    }

    private static List<T> Shuffle<T>(IEnumerable<T> items, Random rng)
    {
        var list = items.ToList();

        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}
