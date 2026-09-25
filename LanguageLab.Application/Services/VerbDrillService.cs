using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Application.Services;

/// <summary>What the learner asked to train: the mode, the stage it was started from, and the card on screen.</summary>
public sealed record DrillRequest(DrillMode Mode, int? Group, DrillScope Scope, string? Exclude);

public sealed record CardVerbView(string V1, string V2, string V3, string Translation, int Group, string? Note);

public sealed record ExampleView(Tense Tense, string Text);

/// <summary>How far the learner is through the set this card was drawn from.</summary>
public sealed record ScopeProgressView(int Passed, int Total);

public sealed record DrillCardView(
    CardVerbView Verb, PromptForm PromptForm, ExampleView Example, ScopeProgressView Scope);

/// <summary>
/// Picks the next card. Which verbs are in play is the scope's business, which of them comes
/// next is <see cref="BatchWindow"/>'s or <see cref="FreePicker"/>'s, and this service only
/// joins the two to the catalog. The answer travels with the card because the client has to
/// blur it — self-assessment has nothing to game.
/// </summary>
public class VerbDrillService
{
    private readonly VerbKnowledgeService _knowledge;

    public VerbDrillService(VerbKnowledgeService knowledge)
    {
        _knowledge = knowledge;
    }

    /// <summary>Null when a batch run has passed every verb of its stage.</summary>
    public async Task<DrillCardView?> NextAsync(long userId, DrillRequest request, Random? random = null)
    {
        var dice = random ?? Random.Shared;
        var verbs = VerbsOf(request);
        var standings = await _knowledge.StandingsAsync(userId, verbs);

        var chosen = request.Mode == DrillMode.Batch
            ? BatchWindow.Next(standings, request.Exclude)
            : FreePicker.Next(standings, request.Exclude, dice);

        if (chosen == null)
        {
            return null;
        }

        var verb = IrregularVerbCatalog.Find(chosen.Verb)!;
        var form = (PromptForm)dice.Next(3);
        var example = verb.ExampleOf(TenseOf(form));

        return new DrillCardView(
            new CardVerbView(
                verb.V1,
                string.Join(" / ", verb.V2),
                string.Join(" / ", verb.V3),
                verb.Translation,
                verb.Group,
                verb.Note),
            form,
            new ExampleView(example.Tense, example.Text),
            new ScopeProgressView(standings.Count(s => VerbScoring.Passed(s.Streak)), standings.Count));
    }

    private static IReadOnlyList<IrregularVerb> VerbsOf(DrillRequest request) =>
        request.Mode == DrillMode.Batch
            ? IrregularVerbCatalog.VerbsOfGroup(request.Group!.Value)
            : request.Scope switch
            {
                DrillScope.Stage => IrregularVerbCatalog.VerbsOfGroup(request.Group!.Value),
                DrillScope.Cumulative => IrregularVerbCatalog.VerbsUpToGroup(request.Group!.Value),
                _ => IrregularVerbCatalog.Verbs,
            };

    private static Tense TenseOf(PromptForm form) =>
        form switch
        {
            PromptForm.V1 => Tense.Present,
            PromptForm.V2 => Tense.Past,
            _ => Tense.Perfect,
        };
}
