namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>Kind is set for Wrong and Neutral; Normalized is what was actually compared.</summary>
public sealed record CheckResult(AttemptOutcome Outcome, ErrorKind? Kind, string Normalized);

/// <summary>
/// Checks one produced form against the catalog and names the mistake. The rules run
/// in the order the learner's spec lists them; a near-miss spelling on a typed task is
/// neutral once (the task stays open with a hint) and a mistake the second time.
/// </summary>
public static class AnswerChecker
{
    public static string Normalize(string answer) =>
        string.Join(' ', answer.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static CheckResult Check(IrregularVerb verb, FormAsked form, string answer, bool typed, int neutralSoFar)
    {
        var a = Normalize(answer);
        var accepted = verb.Forms(form);

        if (accepted.Contains(a))
        {
            return new CheckResult(AttemptOutcome.Correct, null, a);
        }

        if (DistractorGenerator.IsEdForm(verb, a))
        {
            return Wrong(ErrorKind.EdSuffix, a);
        }

        if (form == FormAsked.V3 && verb.V2.Contains(a))
        {
            return Wrong(ErrorKind.V2ForV3, a);
        }

        if (form == FormAsked.V2 && verb.V3.Contains(a))
        {
            return Wrong(ErrorKind.V3ForV2, a);
        }

        var neighbours = IrregularVerbCatalog.VerbsOf(verb.Family).Where(v => v.V1 != verb.V1);

        if (neighbours.Any(n => n.Forms(form).Contains(a)))
        {
            return Wrong(ErrorKind.WrongFamily, a);
        }

        if (accepted.Any(f => Levenshtein(a, f) <= 1))
        {
            return typed && neutralSoFar == 0
                ? new CheckResult(AttemptOutcome.Neutral, ErrorKind.Spelling, a)
                : Wrong(ErrorKind.Spelling, a);
        }

        return Wrong(ErrorKind.Other, a);
    }

    public static int Levenshtein(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();

        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            previous = current;
        }

        return previous[b.Length];
    }

    private static CheckResult Wrong(ErrorKind kind, string normalized) => new(AttemptOutcome.Wrong, kind, normalized);
}
