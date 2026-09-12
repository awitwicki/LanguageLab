using LanguageLab.Domain.Entities;

namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>MistakeVerbs feeds ErrorsOnly: the service collects them from the source session(s).</summary>
public sealed record PlanRequest(SessionMode Mode, string? Family, IReadOnlyList<string> MistakeVerbs);

/// <summary>
/// Turns a request into the ordered task queue of one session. Learn sessions cover a
/// family, weakest state first, round-robin, each verb walking its group's menu; a new
/// verb gets its card first. Errors-only and mixed sessions pick verbs across families
/// by state. Pure — the service persists the result.
/// </summary>
public static class SessionPlanner
{
    public const int MinSize = 8;
    public const int MaxSize = 16;
    public const int TasksPerVerb = 3;
    public const int MixedSize = 10;
    public const int ErrorsMinVerbs = 5;
    public const int ErrorsTasksPerVerb = 2;
    public const int MaxOddOne = 2;

    /// <summary>Priority order of states inside a session: what needs work first.</summary>
    public static readonly IReadOnlyList<VerbState> StateOrder =
    [
        VerbState.Forgotten,
        VerbState.New,
        VerbState.Learning1,
        VerbState.Learning2,
        VerbState.Learning3,
        VerbState.Learned,
        VerbState.Mastered,
    ];

    public static int LearnSize(int verbs) => Math.Clamp(TasksPerVerb * verbs, MinSize, MaxSize);

    public static IReadOnlyList<TaskSpec> Plan(PlanRequest request, IReadOnlyDictionary<string, VerbProgress> progress, Random rng) =>
        request.Mode switch
        {
            SessionMode.Learn => Learn(request.Family ?? throw new ArgumentException("A learn session needs a family."), progress, rng),
            SessionMode.ErrorsOnly => ErrorsOnly(request.MistakeVerbs, progress, rng),
            SessionMode.Mixed => Mixed(progress, rng),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unknown mode."),
        };

    /// <summary>
    /// Two more tasks on a verb after a mistake, single-verb exercises only, continuing
    /// the verb's menu from its next appearances. The card level never returns.
    /// </summary>
    public static IReadOnlyList<TaskSpec> Returns(IrregularVerb verb, VerbState state, int level, int appearance, Random rng)
    {
        var builder = new Builder(rng);
        var specs = new List<TaskSpec>();

        for (var k = appearance; specs.Count < 2; k++)
        {
            var spec = builder.Next(verb, state, Math.Max(level, TaskLevels.Recognition), k, verbIndex: 0, singleVerbOnly: true);

            if (spec != null)
            {
                specs.Add(spec);
            }
        }

        return specs;
    }

    private static IReadOnlyList<TaskSpec> Learn(string family, IReadOnlyDictionary<string, VerbProgress> progress, Random rng)
    {
        var verbs = Order(IrregularVerbCatalog.VerbsOf(family), progress);

        if (verbs.Count == 0)
        {
            return [];
        }

        return RoundRobin(verbs, LearnSize(verbs.Count), progress, rng);
    }

    private static IReadOnlyList<TaskSpec> ErrorsOnly(IReadOnlyList<string> mistakes, IReadOnlyDictionary<string, VerbProgress> progress, Random rng)
    {
        var chosen = IrregularVerbCatalog.Verbs.Where(v => mistakes.Contains(v.V1)).ToList();

        if (chosen.Count < ErrorsMinVerbs)
        {
            var topUp = IrregularVerbCatalog.Verbs
                .Where(v => !chosen.Contains(v) && StateOf(progress, v) != VerbState.New)
                .OrderBy(v => progress[v.V1].Streak)
                .ThenBy(v => progress[v.V1].LastSeenAt)
                .Take(ErrorsMinVerbs - chosen.Count);

            chosen.AddRange(topUp);
        }

        var verbs = Order(chosen, progress);

        return verbs.Count == 0 ? [] : RoundRobin(verbs, Math.Min(MaxSize, ErrorsTasksPerVerb * verbs.Count), progress, rng);
    }

    private static IReadOnlyList<TaskSpec> Mixed(IReadOnlyDictionary<string, VerbProgress> progress, Random rng)
    {
        var verbs = IrregularVerbCatalog.Verbs
            .Where(v => StateOf(progress, v) != VerbState.New)
            .OrderBy(v => Priority(StateOf(progress, v)))
            .ThenBy(v => progress[v.V1].LastSeenAt)
            .Take(MixedSize)
            .ToList();

        return verbs.Count == 0 ? [] : RoundRobin(verbs, verbs.Count, progress, rng);
    }

    private static List<IrregularVerb> Order(IEnumerable<IrregularVerb> verbs, IReadOnlyDictionary<string, VerbProgress> progress) =>
        verbs
            .Select((v, i) => (Verb: v, Index: i))
            .OrderBy(x => Priority(StateOf(progress, x.Verb)))
            .ThenBy(x => x.Index)
            .Select(x => x.Verb)
            .ToList();

    private static IReadOnlyList<TaskSpec> RoundRobin(
        IReadOnlyList<IrregularVerb> verbs, int size, IReadOnlyDictionary<string, VerbProgress> progress, Random rng)
    {
        var builder = new Builder(rng);
        var appearances = new Dictionary<string, int>(StringComparer.Ordinal);
        var specs = new List<TaskSpec>();

        for (var i = 0; i < size; i++)
        {
            var verb = verbs[i % verbs.Count];
            var state = StateOf(progress, verb);
            var k = appearances.GetValueOrDefault(verb.V1);
            appearances[verb.V1] = k + 1;

            if (state == VerbState.New && k == 0)
            {
                specs.Add(ExerciseFactory.Card(verb));
                continue;
            }

            // A new verb has just had its card: from here on it practises like Learning1.
            var level = state == VerbState.New ? TaskLevels.Recognition : TaskLevels.For(state);
            var spec = builder.Next(verb, state, level, state == VerbState.New ? k - 1 : k, i % verbs.Count, singleVerbOnly: false);

            if (spec != null)
            {
                specs.Add(spec);
            }
        }

        return specs;
    }

    private static int Priority(VerbState state) => StateOrder.ToList().IndexOf(state);

    private static VerbState StateOf(IReadOnlyDictionary<string, VerbProgress> progress, IrregularVerb verb) =>
        progress.TryGetValue(verb.V1, out var p) ? p.State : VerbState.New;

    /// <summary>Walks a verb's menu from its appearance index, honouring the per-session caps.</summary>
    private sealed class Builder(Random rng)
    {
        private int _oddOnes;
        private readonly HashSet<FormAsked> _matchForms = [];

        public TaskSpec? Next(IrregularVerb verb, VerbState state, int level, int appearance, int verbIndex, bool singleVerbOnly)
        {
            var menu = Menus.For(verb.Group, level, appearance);
            var start = Menus.StartIndex(verb.Group, level, appearance, verbIndex, menu.Count);

            for (var offset = 0; offset < menu.Count; offset++)
            {
                var item = menu[(start + offset) % menu.Count];

                if (item.Type is ExerciseType.Match or ExerciseType.OddOne && singleVerbOnly)
                {
                    continue;
                }

                if (item.Type == ExerciseType.OddOne && _oddOnes >= MaxOddOne)
                {
                    continue;
                }

                if (item.Type == ExerciseType.Match && _matchForms.Contains(item.Form))
                {
                    continue;
                }

                var spec = ExerciseFactory.Build(item, verb, state, level, rng);

                if (spec == null)
                {
                    continue;
                }

                if (spec.Type == ExerciseType.OddOne)
                {
                    _oddOnes++;
                }

                if (spec.Type == ExerciseType.Match)
                {
                    _matchForms.Add(item.Form);
                }

                return spec;
            }

            return null;
        }
    }
}
