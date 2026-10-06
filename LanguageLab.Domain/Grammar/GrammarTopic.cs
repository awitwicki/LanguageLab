namespace LanguageLab.Domain.Grammar;

/// <summary>
/// One pick-the-form exercise: a sentence with one <see cref="Blank"/> and 2–4 options, exactly
/// one of them <see cref="Answer"/>. <see cref="Why"/> is the one line shown after a wrong pick.
/// The browser judges the pick itself — nothing is stored (docs/trainers.md → Grammar).
/// </summary>
public sealed record GrammarExercise(string Sentence, IReadOnlyList<string> Options, string Answer, string Why)
{
    public const string Blank = "___";
}

/// <summary>
/// One written grammar topic — its order, section, level and title come from
/// <see cref="GrammarSyllabus"/>, which the catalog tests hold it to. <see cref="Key"/> is stable — a later phase keys progress
/// rows on it. <see cref="Explanation"/> is short paragraphs and <see cref="Examples"/> whole
/// sentences; in both, square brackets mark what the client shows in bold —
/// <c>She [is] a teacher.</c>
/// </summary>
public sealed record GrammarTopic(
    string Key,
    string Section,
    GrammarLevel Level,
    string Title,
    IReadOnlyList<string> Explanation,
    IReadOnlyList<string> Examples,
    IReadOnlyList<GrammarExercise> Exercises);
