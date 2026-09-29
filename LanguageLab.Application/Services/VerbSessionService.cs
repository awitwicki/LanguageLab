using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Application.Services;

/// <summary>What the learner asked to train: the mode and the set it is drawn from.</summary>
public sealed record SessionRequest(DrillMode Mode, int? Group, DrillScope Scope);

/// <summary>How far the learner is through the set this session was drawn from.</summary>
public sealed record ScopeProgressView(int Passed, int Total);

/// <summary>A verb as a session needs it: everything to draw its cards, plus where the learner stands.</summary>
public sealed record SessionVerbView(
    string V1,
    string V2,
    string V3,
    string Translation,
    int Group,
    string? Note,
    IReadOnlyList<VerbExercise> Exercises,
    IReadOnlyList<PromptForm> FormOrder,
    double Mastery,
    int Streak,
    int Answers);

/// <summary>One card of a free run's queue: which verb, prompted by which form.</summary>
public sealed record SessionCardView(string Verb, PromptForm PromptForm);

/// <summary>
/// A whole session: the verbs in play, and — for a free run — the order to play them in.
/// </summary>
public sealed record VerbSessionView(
    DrillMode Mode,
    IReadOnlyList<SessionVerbView> Verbs,
    IReadOnlyList<SessionCardView>? Queue,
    ScopeProgressView Scope);

/// <summary>
/// Hands the browser a whole session rather than one card at a time. Which verbs the set
/// holds is the scope's business, the order is <see cref="BatchWindow"/>'s or
/// <see cref="FreePicker"/>'s, and this service only joins the two to the catalog.
///
/// Ordinary training gets the window and no queue: the browser runs the round itself, because
/// the next card depends on the answer just given and a queue drawn in advance could not react
/// to it. A free run is a weighted random draw with nothing to react to, so its queue is drawn
/// here. Either way the exercises travel with the verbs, because the browser judges a pick at
/// once for its own screen — the server judges it again from <c>chosen</c> when the answer
/// reaches it.
/// </summary>
public class VerbSessionService
{
    /// <summary>How many cards a free run is handed at a time.</summary>
    public const int FreeQueueLength = 20;

    private readonly VerbKnowledgeService _knowledge;

    public VerbSessionService(VerbKnowledgeService knowledge)
    {
        _knowledge = knowledge;
    }

    /// <summary>Null when ordinary training has passed every verb of its stage.</summary>
    public async Task<VerbSessionView?> StartAsync(long userId, SessionRequest request, Random? random = null)
    {
        var dice = random ?? Random.Shared;
        var verbs = VerbsOf(request);
        var standings = await _knowledge.StandingsAsync(userId, verbs);
        var scope = new ScopeProgressView(standings.Count(s => VerbScoring.Passed(s.Streak)), standings.Count);

        if (request.Mode == DrillMode.Batch)
        {
            var window = BatchWindow.Ordered(standings);

            return window.Count == 0
                ? null
                : new VerbSessionView(DrillMode.Batch, window.Select(s => Verb(s, dice)).ToList(), null, scope);
        }

        var drawn = FreePicker.Draw(standings, FreeQueueLength, dice);

        var queue = drawn
            .Select(s => new SessionCardView(s.Verb, (PromptForm)dice.Next(3)))
            .ToList();

        // The queue names a verb as often as it drew it; the data beside it is listed once.
        var listed = drawn
            .DistinctBy(s => s.Verb)
            .Select(s => Verb(s, dice))
            .ToList();

        return new VerbSessionView(DrillMode.Free, listed, queue, scope);
    }

    private static SessionVerbView Verb(VerbStanding standing, Random dice)
    {
        var verb = IrregularVerbCatalog.Find(standing.Verb)!;

        return new SessionVerbView(
            verb.V1,
            string.Join(" / ", verb.V2),
            string.Join(" / ", verb.V3),
            verb.Translation,
            verb.Group,
            verb.Note,
            ExerciseBuilder.All(verb, dice),
            ShuffledForms(dice),
            standing.Mastery,
            standing.Streak,
            standing.Answers);
    }

    /// <summary>
    /// The three forms in a per-verb random order. The browser walks it instead of rolling its
    /// own dice: it needs no randomness of its own, its tests stay deterministic, and
    /// consecutive showings of one verb cannot prompt the same form twice.
    /// </summary>
    private static IReadOnlyList<PromptForm> ShuffledForms(Random dice)
    {
        var forms = new List<PromptForm> { PromptForm.V1, PromptForm.V2, PromptForm.V3 };

        for (var i = forms.Count - 1; i > 0; i--)
        {
            var j = dice.Next(i + 1);
            (forms[i], forms[j]) = (forms[j], forms[i]);
        }

        return forms;
    }

    private static IReadOnlyList<IrregularVerb> VerbsOf(SessionRequest request) =>
        request.Mode == DrillMode.Batch
            ? IrregularVerbCatalog.VerbsOfGroup(request.Group!.Value)
            : request.Scope switch
            {
                DrillScope.Stage => IrregularVerbCatalog.VerbsOfGroup(request.Group!.Value),
                DrillScope.Cumulative => IrregularVerbCatalog.VerbsUpToGroup(request.Group!.Value),
                _ => IrregularVerbCatalog.Verbs,
            };
}
