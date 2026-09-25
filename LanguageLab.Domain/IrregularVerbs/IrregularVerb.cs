namespace LanguageLab.Domain.IrregularVerbs;

public enum Tense
{
    Present,
    Past,
    Perfect,
}

/// <summary>
/// One example sentence of a verb in one tense. The verb form is wrapped in square
/// brackets in the text — <c>Yesterday I [went] home.</c> — and the client emphasises
/// what is inside them when it shows the sentence.
/// </summary>
public sealed record Example(Tense Tense, string Text)
{
    private int Open => Text.IndexOf('[');
    private int Close => Text.IndexOf(']');

    /// <summary>The verb form inside the brackets.</summary>
    public string Bracketed => Text[(Open + 1)..Close];
}

/// <summary>
/// One irregular verb of the catalog. V2 and V3 are lists because <c>be</c> has
/// <c>was / were</c> and <c>get</c> has <c>got / gotten</c>. V1 is the stable key used in
/// knowledge rows and the API.
/// </summary>
public sealed record IrregularVerb(
    int Group,
    string V1,
    IReadOnlyList<string> V2,
    IReadOnlyList<string> V3,
    string Translation,
    IReadOnlyList<Example> Examples,
    string? Note = null)
{
    /// <summary>"go – went – gone"; alternatives joined with " / ".</summary>
    public string Triplet => $"{V1} – {string.Join(" / ", V2)} – {string.Join(" / ", V3)}";

    public Example ExampleOf(Tense tense) => Examples.First(e => e.Tense == tense);
}
