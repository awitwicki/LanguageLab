namespace LanguageLab.Domain.IrregularVerbs;

public enum Tense
{
    Present,
    Past,
    Perfect,
}

/// <summary>
/// One example sentence of a verb in one tense. The verb form is wrapped in square
/// brackets in the text — <c>Yesterday I [went] home.</c> — so the same sentence serves
/// the card (form shown), the gap exercises (form removed) and FormPick (form marked).
/// </summary>
public sealed record Example(Tense Tense, string Text)
{
    private int Open => Text.IndexOf('[');
    private int Close => Text.IndexOf(']');

    /// <summary>The verb form inside the brackets.</summary>
    public string Bracketed => Text[(Open + 1)..Close];

    /// <summary>The sentence with the brackets removed.</summary>
    public string Plain => Text.Remove(Close, 1).Remove(Open, 1);

    /// <summary>The sentence with the bracketed form replaced by a gap.</summary>
    public string WithGap => Text[..Open] + "___" + Text[(Close + 1)..];
}

/// <summary>
/// A family: verbs of one group that follow the same sound / spelling pattern and are
/// learned together in one session. Suffixes are what the card highlights in V2 / V3.
/// </summary>
public sealed record VerbFamily(string Key, int Group, string Title, IReadOnlyList<string> Suffixes);

/// <summary>
/// One irregular verb of the catalog. V2 and V3 are lists because <c>be</c> has
/// <c>was / were</c> and <c>get</c> has <c>got / gotten</c>; every listed form is an
/// accepted answer. V1 is the stable key used in progress rows and the API.
/// Confusables are hand-picked wrong forms the rules cannot derive (cut → cat).
/// </summary>
public sealed record IrregularVerb(
    int Group,
    string Family,
    string V1,
    IReadOnlyList<string> V2,
    IReadOnlyList<string> V3,
    string Translation,
    IReadOnlyList<Example> Examples,
    string? Note = null,
    IReadOnlyList<string>? Confusables = null)
{
    /// <summary>"go – went – gone"; alternatives joined with " / ".</summary>
    public string Triplet => $"{V1} – {string.Join(" / ", V2)} – {string.Join(" / ", V3)}";

    /// <summary>The accepted forms for what a task asks; Both and Recognition have no single answer.</summary>
    public IReadOnlyList<string> Forms(FormAsked form) =>
        form switch
        {
            FormAsked.V2 => V2,
            FormAsked.V3 => V3,
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Only V2 and V3 have forms."),
        };

    public Example ExampleOf(Tense tense) => Examples.First(e => e.Tense == tense);

    public IReadOnlyList<string> AllForms => new[] { V1 }.Concat(V2).Concat(V3).Distinct(StringComparer.Ordinal).ToList();

    public bool SecondAndThirdAlike => V2.SequenceEqual(V3);
}
