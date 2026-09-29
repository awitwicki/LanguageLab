using LanguageLab.Application.Books;

namespace LanguageLab.Tests.Books;

public class BookChaptersTests
{
    private static BookSection S(string title, int depth, string ownText, params BookSection[] children) =>
        new(title, depth, ownText, children);

    // Part One (depth 1) → Chapter 1, Chapter 2 (depth 2); Prologue (depth 1) has no children.
    private static readonly BookSection[] Tree =
    [
        S("Prologue", 1, "before the beginning"),
        S("Part One", 1, "part intro",
            S("Chapter 1", 2, "the children were playing"),
            S("Chapter 2", 2, "holston climbed")),
    ];

    [Fact]
    public void Leaf_mode_makes_a_chapter_of_every_section_with_no_children()
    {
        var chapters = BookChapters.Flatten(Tree, ChapterMode.Leaf);

        Assert.Equal(
            new[]
            {
                new ParsedChapter(0, "Prologue", "before the beginning"),
                new ParsedChapter(1, "Chapter 1", "the children were playing"),
                new ParsedChapter(2, "Chapter 2", "holston climbed"),
            },
            chapters);
    }

    [Fact]
    public void A_fixed_depth_merges_nested_sections_into_one_chapter()
    {
        var chapters = BookChapters.Flatten(Tree, new ChapterMode(1));

        Assert.Equal(
            new[]
            {
                new ParsedChapter(0, "Prologue", "before the beginning"),
                new ParsedChapter(1, "Part One", "part intro the children were playing holston climbed"),
            },
            chapters);
    }

    [Fact]
    public void A_leaf_shallower_than_the_depth_is_still_a_chapter()
    {
        var chapters = BookChapters.Flatten(Tree, new ChapterMode(2));

        Assert.Equal(new[] { "Prologue", "Chapter 1", "Chapter 2" }, chapters.Select(chapter => chapter.Title));
    }

    [Fact]
    public void Merged_text_is_trimmed_per_section_and_joined_by_a_space()
    {
        var tree = new[] { S("Part", 1, "", S("A", 2, "  alpha "), S("B", 2, "", S("C", 3, "gamma  "))) };

        var chapter = Assert.Single(BookChapters.Flatten(tree, new ChapterMode(1)));

        // collectText trims each section's merged text, then joins the pieces with a space.
        Assert.Equal("alpha gamma", chapter.Text);
    }

    [Fact]
    public void A_depth_below_one_makes_every_top_level_section_a_chapter()
    {
        var chapters = BookChapters.Flatten(Tree, new ChapterMode(0));

        Assert.Equal(new[] { "Prologue", "Part One" }, chapters.Select(chapter => chapter.Title));
    }

    [Fact]
    public void No_sections_means_no_chapters()
    {
        Assert.Empty(BookChapters.Flatten([], ChapterMode.Leaf));
    }

    [Fact]
    public void Leaf_is_the_mode_with_no_depth()
    {
        Assert.Equal(new ChapterMode(null), ChapterMode.Leaf);
        Assert.Null(ChapterMode.Leaf.Depth);
    }
}
