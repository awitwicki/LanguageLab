namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// The one line shown with every feedback: the group's rule in the verb's own forms.
/// Not a theory page — the triplet is always there so the learner sees the whole verb.
/// </summary>
public static class Explanations
{
    public static string For(IrregularVerb verb, FormAsked form, Tense? tense, ErrorKind? kind)
    {
        var v2 = string.Join(" / ", verb.V2);
        var v3 = string.Join(" / ", verb.V3);
        var perfect = tense == Tense.Perfect || (tense == null && form == FormAsked.V3);

        var line = verb.Group switch
        {
            1 => $"{verb.V1} doesn't change: {verb.Triplet}.",
            2 => perfect
                ? $"After have / has it's {v3}, not {v2}: {verb.Triplet}."
                : $"{verb.V1} goes back to its first form: {verb.Triplet}.",
            3 => $"V2 and V3 are the same: {verb.Triplet}.",
            _ => perfect
                ? $"After have / has it's {v3}, not {v2}: {verb.Triplet}."
                : $"Past Simple is {v2}: {verb.Triplet}.",
        };

        if (kind == ErrorKind.EdSuffix)
        {
            line = "No -ed here. " + line;
        }

        return verb.Note == null ? line : $"{line} {verb.Note}";
    }
}
