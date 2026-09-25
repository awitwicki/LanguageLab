namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>A task to be stored: the exercise, its verb, what it asks, its level and the payload.</summary>
public sealed record TaskSpec(ExerciseType Type, IrregularVerb Verb, FormAsked FormAsked, int Level, TaskPayload Payload)
{
    /// <summary>Multi-verb tasks are capped per session and never used as returns.</summary>
    public bool IsMultiVerb => Type is ExerciseType.Match or ExerciseType.OddOne;
}

/// <summary>One entry of a group's exercise menu.</summary>
public sealed record MenuItem(ExerciseType Type, Tense? Tense, FormAsked Form);

/// <summary>
/// The exercise menus of the learner's spec, per group and level. Group 4 at level 2
/// trains V2 and V3 separately: phase A (even appearances) asks for the Past Simple,
/// phase B (odd) for the Present Perfect.
/// </summary>
public static class Menus
{
    private static readonly MenuItem GapPast = new(ExerciseType.GapChoice, Tense.Past, FormAsked.V2);
    private static readonly MenuItem GapPerfect = new(ExerciseType.GapChoice, Tense.Perfect, FormAsked.V3);
    private static readonly MenuItem TypePast = new(ExerciseType.GapType, Tense.Past, FormAsked.V2);
    private static readonly MenuItem TypePerfect = new(ExerciseType.GapType, Tense.Perfect, FormAsked.V3);
    private static readonly MenuItem MatchV2 = new(ExerciseType.Match, null, FormAsked.V2);
    private static readonly MenuItem MatchV3 = new(ExerciseType.Match, null, FormAsked.V3);
    private static readonly MenuItem Odd = new(ExerciseType.OddOne, null, FormAsked.Recognition);
    private static readonly MenuItem Pick = new(ExerciseType.FormPick, null, FormAsked.Recognition);
    private static readonly MenuItem Triple = new(ExerciseType.TripleType, null, FormAsked.Both);

    public static IReadOnlyList<MenuItem> For(int group, int level, int appearance) =>
        (group, level) switch
        {
            (1, 2) => [GapPast, GapPerfect, Odd],
            (2, 2) => [Pick, GapPerfect],
            (3, 2) => [MatchV2, GapPast, GapPerfect],
            (4, 2) => appearance % 2 == 0 ? [MatchV2, GapPast] : [MatchV3, GapPerfect],
            (1, 3) => [TypePast, TypePerfect],
            (2, 3) => [TypePast, TypePerfect],
            (3, 3) => [Triple, TypePast, TypePerfect],
            (4, 3) => [Pick, TypePast, TypePerfect, Triple],
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Menus exist for levels 2 and 3."),
        };

    /// <summary>
    /// Which menu item a verb's k-th appearance starts from. The verb's position in the
    /// session offsets the walk, so neighbouring verbs get different exercises in the same
    /// round instead of all opening with the first item; group 4 level 2 cycles inside its phase.
    /// </summary>
    public static int StartIndex(int group, int level, int appearance, int verbIndex, int menuLength) =>
        group == 4 && level == 2 ? (appearance / 2 + verbIndex) % menuLength : (appearance + verbIndex) % menuLength;
}

/// <summary>Builds task payloads. Null means the item cannot be built for this verb (see the planner's skip rules).</summary>
public static class ExerciseFactory
{
    public const int GapChoiceDistractors = 3;
    public const int MatchMaxPairs = 5;
    public const int MatchMinVerbs = 3;

    public static TaskSpec Card(IrregularVerb verb) =>
        new(ExerciseType.Card, verb, FormAsked.Recognition, TaskLevels.Card, new TaskPayload());

    public static TaskSpec GapChoice(IrregularVerb verb, Tense tense, int level, Random rng)
    {
        var form = FormFor(tense);
        var example = verb.ExampleOf(tense);
        var options = new List<string> { example.Bracketed };
        options.AddRange(DistractorGenerator.For(verb, form, GapChoiceDistractors, rng));

        return new TaskSpec(ExerciseType.GapChoice, verb, form, level, new TaskPayload
        {
            Sentence = example.WithGap,
            Tense = tense,
            Options = Shuffle(options, rng),
            Correct = example.Bracketed,
        });
    }

    /// <summary>Three V2 forms of group 1 (which never change) and one from another group — the odd one.</summary>
    public static TaskSpec? OddOne(IrregularVerb verb, int level, Random rng)
    {
        if (verb.Group != 1)
        {
            return null;
        }

        var siblings = Shuffle(IrregularVerbCatalog.VerbsOfGroup(1).Where(v => v.V1 != verb.V1).ToList(), rng).Take(2);
        var outsiders = IrregularVerbCatalog.Verbs.Where(v => v.Group != 1).ToList();
        var odd = outsiders[rng.Next(outsiders.Count)];

        var options = new List<string> { verb.V2[0] };
        options.AddRange(siblings.Select(s => s.V2[0]));
        options.Add(odd.V2[0]);

        return new TaskSpec(ExerciseType.OddOne, verb, FormAsked.Recognition, level, new TaskPayload
        {
            Options = Shuffle(options, rng),
            Correct = odd.V2[0],
            Hint = odd.V1,
        });
    }

    /// <summary>The anchor verb and its family neighbours, up to five pairs; null for a family too small to match.</summary>
    public static TaskSpec? Match(IrregularVerb anchor, FormAsked form, int level, Random rng)
    {
        var family = IrregularVerbCatalog.VerbsOf(anchor.Family);

        if (family.Count < MatchMinVerbs)
        {
            return null;
        }

        var start = family.ToList().FindIndex(v => v.V1 == anchor.V1);
        var verbs = Enumerable.Range(0, Math.Min(MatchMaxPairs, family.Count)).Select(i => family[(start + i) % family.Count]).ToList();
        var pairs = verbs.Select(v => new MatchPair(v.V1, string.Join(" / ", v.Forms(form)))).ToList();

        return new TaskSpec(ExerciseType.Match, anchor, form, level, new TaskPayload
        {
            Form = form,
            Pairs = pairs,
            RightOrder = Shuffle(pairs.Select(p => p.Right).ToList(), rng),
            Matched = [],
        });
    }

    /// <summary>A past or perfect sentence with the form marked; the learner says which form it is. Null when V2 = V3.</summary>
    public static TaskSpec? FormPick(IrregularVerb verb, int level, Random rng)
    {
        if (verb.SecondAndThirdAlike)
        {
            return null;
        }

        var tense = rng.Next(2) == 0 ? Tense.Past : Tense.Perfect;

        return new TaskSpec(ExerciseType.FormPick, verb, FormAsked.Recognition, level, new TaskPayload
        {
            Sentence = verb.ExampleOf(tense).Text,
            Tense = tense,
            Correct = tense == Tense.Past ? "v2" : "v3",
        });
    }

    public static TaskSpec GapType(IrregularVerb verb, Tense tense, int level) =>
        new(ExerciseType.GapType, verb, FormFor(tense), level, new TaskPayload
        {
            Sentence = verb.ExampleOf(tense).WithGap,
            Tense = tense,
            Hint = verb.V1,
        });

    /// <summary>Group 3 below Learning3 mirrors V2 into V3 as it is typed — the rule made visible.</summary>
    public static TaskSpec TripleType(IrregularVerb verb, VerbState state, int level) =>
        new(ExerciseType.TripleType, verb, FormAsked.Both, level, new TaskPayload
        {
            AutofillV3 = verb.Group == 3 && state is VerbState.New or VerbState.Learning1 or VerbState.Learning2,
        });

    public static TaskSpec? Build(MenuItem item, IrregularVerb verb, VerbState state, int level, Random rng) =>
        item.Type switch
        {
            ExerciseType.GapChoice => GapChoice(verb, item.Tense!.Value, level, rng),
            ExerciseType.OddOne => OddOne(verb, level, rng),
            ExerciseType.Match => Match(verb, item.Form, level, rng),
            ExerciseType.FormPick => FormPick(verb, level, rng),
            ExerciseType.GapType => GapType(verb, item.Tense!.Value, level),
            ExerciseType.TripleType => TripleType(verb, state, level),
            _ => throw new ArgumentOutOfRangeException(nameof(item), item.Type, "Not a menu exercise."),
        };

    private static FormAsked FormFor(Tense tense) => tense == Tense.Past ? FormAsked.V2 : FormAsked.V3;

    private static List<T> Shuffle<T>(List<T> items, Random rng)
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
