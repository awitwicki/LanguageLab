namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// One card's worth of drill: a sentence split around its blank, the options to fill it with,
/// and the one option that is right. Built on the server and handed to the browser whole, so
/// the browser judges a click at once and the server can judge it again from the log.
/// </summary>
public sealed record VerbExercise(
    PromptForm Form,
    string Before,
    string After,
    IReadOnlyList<string> Options,
    string Answer);

/// <summary>Turns a verb and a form into ready exercises — its own sentence plus templates, shuffled.</summary>
public static class ExerciseBuilder
{
    public const int MaxOptions = 4;

    public const int TemplatesPerForm = 2;

    public static Tense TenseOf(PromptForm form) => form switch
    {
        PromptForm.V1 => Tense.Present,
        PromptForm.V2 => Tense.Past,
        _ => Tense.Perfect,
    };

    /// <summary>The exercises of all three forms, V1 first.</summary>
    public static IReadOnlyList<VerbExercise> All(IrregularVerb verb, Random dice) =>
        Enum.GetValues<PromptForm>().SelectMany(form => For(verb, form, dice)).ToList();

    public static IReadOnlyList<VerbExercise> For(IrregularVerb verb, PromptForm form, Random dice)
    {
        var tense = TenseOf(form);
        var own = verb.ExampleOf(tense);
        var open = own.Text.IndexOf('[');
        var close = own.Text.IndexOf(']');

        var exercises = new List<VerbExercise>
        {
            Build(verb, form, own.Text[..open], own.Text[(close + 1)..], own.Bracketed, dice),
        };

        if (verb.OwnSentencesOnly)
        {
            return exercises;
        }

        foreach (var template in SentenceTemplates.Pick(tense, TemplatesPerForm, dice))
        {
            var blank = template.IndexOf(SentenceTemplates.Blank, StringComparison.Ordinal);

            exercises.Add(Build(
                verb, form,
                template[..blank],
                template[(blank + SentenceTemplates.Blank.Length)..],
                verb.FormsOf(form)[0],
                dice));
        }

        // Otherwise the own sentence — built first, above — would always be the first exercise
        // a learner meets, and a word answered right inside its own form's first showing would
        // never draw a template: the very thing the templates exist to prevent.
        Shuffle(exercises, dice);

        return exercises;
    }

    private static void Shuffle<T>(IList<T> items, Random dice)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = dice.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// The answer, the verb's other real forms, then fakes — capped at <see cref="MaxOptions"/>
    /// and shuffled. Every other member of the answer's own set is left out, so exactly one
    /// option is right: "was" never sits beside "were", "got" never beside "gotten".
    /// </summary>
    private static VerbExercise Build(
        IrregularVerb verb, PromptForm form, string before, string after, string answer, Random dice)
    {
        var right = verb.FormsOf(form);
        var others = verb.V2.Concat(verb.V3).Prepend(verb.V1).Where(f => !right.Contains(f));

        var options = others
            .Concat(FakeForms.Of(verb))
            .Prepend(answer)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxOptions)
            .ToList();

        for (var i = options.Count - 1; i > 0; i--)
        {
            var j = dice.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }

        return new VerbExercise(form, before, after, options, answer);
    }
}
