using LanguageLab.Domain.Grammar;

namespace LanguageLab.Api.Endpoints;

/// <summary>
/// One syllabus topic as the browser gets it: a written one with its content, a planned one
/// (<see cref="Planned"/>) with empty lists, so the client keeps one shape. <see cref="Level"/> is
/// "A1"…"B2" — mapped here rather than left to the app's camel-case enum converter.
/// </summary>
public sealed record GrammarTopicView(
    string Key,
    string Section,
    string Level,
    string Title,
    bool Planned,
    IReadOnlyList<string> Explanation,
    IReadOnlyList<string> Examples,
    IReadOnlyList<GrammarExercise> Exercises);

/// <summary>
/// The grammar syllabus, read-only and answers included: the browser judges its own picks and
/// nothing is stored (docs/trainers.md → Grammar). Open to every learner language — the
/// explanations are English.
/// </summary>
public static class GrammarEndpoints
{
    internal static IReadOnlyList<GrammarTopicView> Topics { get; } = Build();

    public static void MapGrammarEndpoints(this WebApplication app)
    {
        var grammar = app.MapGroup("/api/grammar").RequireAuthorization();

        grammar.MapGet("/topics", () => Results.Ok(Topics));
    }

    private static List<GrammarTopicView> Build()
    {
        var written = GrammarCatalog.Topics.ToDictionary(t => t.Key, StringComparer.Ordinal);

        return GrammarSyllabus.Entries
            .Select(entry => written.TryGetValue(entry.Key, out var topic)
                ? new GrammarTopicView(entry.Key, entry.Section, entry.Level.ToString(), entry.Title, false,
                    topic.Explanation, topic.Examples, topic.Exercises)
                : new GrammarTopicView(entry.Key, entry.Section, entry.Level.ToString(), entry.Title, true,
                    [], [], []))
            .ToList();
    }
}
