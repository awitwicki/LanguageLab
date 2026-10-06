using LanguageLab.Domain.Grammar;

namespace LanguageLab.Tests;

public class GrammarSyllabusTests
{
    private static IReadOnlyList<GrammarSyllabusEntry> Entries => GrammarSyllabus.Entries;

    [Fact]
    public void The_syllabus_holds_86_topics_across_four_levels()
    {
        Assert.Equal(86, Entries.Count);
        Assert.Equal(17, Entries.Count(e => e.Level == GrammarLevel.A1));
        Assert.Equal(26, Entries.Count(e => e.Level == GrammarLevel.A2));
        Assert.Equal(25, Entries.Count(e => e.Level == GrammarLevel.B1));
        Assert.Equal(18, Entries.Count(e => e.Level == GrammarLevel.B2));
    }

    [Fact]
    public void Keys_are_unique()
    {
        Assert.Equal(Entries.Count, Entries.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_entry_has_a_section_and_a_title()
    {
        Assert.All(Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Section), e.Key);
            Assert.False(string.IsNullOrWhiteSpace(e.Title), e.Key);
        });
    }

    /// <summary>The list groups consecutive entries into sections, so a section must not reappear.</summary>
    [Fact]
    public void Sections_are_contiguous_and_there_are_fifteen()
    {
        var runs = new List<string>();

        foreach (var entry in Entries)
        {
            if (runs.Count == 0 || runs[^1] != entry.Section)
            {
                runs.Add(entry.Section);
            }
        }

        Assert.Equal(15, runs.Count);
        Assert.Equal(runs.Count, runs.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Within_a_section_levels_never_go_down()
    {
        foreach (var section in Entries.GroupBy(e => e.Section))
        {
            var levels = section.Select(e => e.Level).ToList();

            Assert.Equal(levels.Order(), levels);
        }
    }

    /// <summary>Review focus 1 and 2: a written topic must be in the syllabus and agree with it.</summary>
    [Fact]
    public void Every_written_topic_matches_its_syllabus_entry()
    {
        var byKey = Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);

        Assert.All(GrammarCatalog.Topics, topic =>
        {
            Assert.True(byKey.TryGetValue(topic.Key, out var entry), topic.Key);
            Assert.Equal(entry!.Section, topic.Section);
            Assert.Equal(entry.Level, topic.Level);
            Assert.Equal(entry.Title, topic.Title);
        });
    }
}
