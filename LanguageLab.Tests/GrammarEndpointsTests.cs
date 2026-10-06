using System.Text.Json;
using LanguageLab.Api.Endpoints;
using LanguageLab.Domain.Grammar;

namespace LanguageLab.Tests;

/// <summary>
/// What <c>GET /api/grammar/topics</c> answers with. No HTTP here — the endpoint hands out a
/// static catalog, so the payload and its JSON names are the whole behaviour.
/// </summary>
public class GrammarEndpointsTests
{
    [Fact]
    public void The_payload_is_the_whole_syllabus_in_order()
    {
        Assert.Equal(GrammarSyllabus.Entries.Select(e => e.Key), GrammarEndpoints.Topics.Select(t => t.Key));
    }

    [Fact]
    public void Written_topics_carry_their_catalog_content()
    {
        var written = GrammarEndpoints.Topics.Where(t => !t.Planned).ToList();

        Assert.Equal(GrammarCatalog.Topics.Select(t => t.Key), written.Select(t => t.Key));
        Assert.Equal(
            GrammarCatalog.Topics.SelectMany(t => t.Exercises).Select(e => e.Sentence),
            written.SelectMany(t => t.Exercises).Select(e => e.Sentence));
        Assert.All(written, t => Assert.NotEmpty(t.Explanation));
    }

    [Fact]
    public void Planned_topics_carry_no_content()
    {
        var planned = GrammarEndpoints.Topics.Where(t => t.Planned).ToList();

        Assert.Equal(GrammarSyllabus.Entries.Count - GrammarCatalog.Topics.Count, planned.Count);
        Assert.All(planned, t =>
        {
            Assert.Empty(t.Explanation);
            Assert.Empty(t.Examples);
            Assert.Empty(t.Exercises);
        });
    }

    /// <summary>
    /// The names web/src/api/client.ts reads. Review focus 4: <c>level</c> is "A1", never "a1" or a
    /// number, whatever enum converter the app registers.
    /// </summary>
    [Fact]
    public void The_payload_serializes_to_the_names_the_spa_reads()
    {
        var json = JsonSerializer.Serialize(GrammarEndpoints.Topics, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var topic = document.RootElement[0];
        var exercise = topic.GetProperty("exercises")[0];

        foreach (var name in new[] { "key", "section", "level", "title", "planned", "explanation", "examples" })
        {
            Assert.True(topic.TryGetProperty(name, out _), name);
        }

        foreach (var name in new[] { "sentence", "options", "answer", "why" })
        {
            Assert.True(exercise.TryGetProperty(name, out _), name);
        }

        Assert.Equal("A1", topic.GetProperty("level").GetString());
        Assert.False(topic.GetProperty("planned").GetBoolean());
    }
}
