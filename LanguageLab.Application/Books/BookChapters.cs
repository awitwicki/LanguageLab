namespace LanguageLab.Application.Books;

/// <summary>
/// Which sections are chapters. <see cref="Depth"/> null = leaf chapters (a section with no
/// nested ones); a number = that depth, 1-based — web/src/fb2/chapters.ts's ChapterMode.
/// </summary>
public readonly record struct ChapterMode(int? Depth)
{
    public static ChapterMode Leaf => new(null);
}

/// <summary>A port of flattenChapters in web/src/fb2/chapters.ts — change both together.</summary>
public static class BookChapters
{
    /// <summary>
    /// A section is a chapter when the mode is leaf and it has no children, or when its depth
    /// reaches the mode's depth or it has no children; otherwise its children are walked. A
    /// chapter's text is its own text plus every nested section's, joined by a space.
    /// </summary>
    public static IReadOnlyList<ParsedChapter> Flatten(IReadOnlyList<BookSection> sections, ChapterMode mode)
    {
        var chapters = new List<ParsedChapter>();

        void Walk(BookSection node)
        {
            var isChapter = mode.Depth is { } depth
                ? node.Depth >= depth || node.Children.Count == 0
                : node.Children.Count == 0;

            if (isChapter)
            {
                chapters.Add(new ParsedChapter(chapters.Count, node.Title, CollectText(node)));
                return;
            }

            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }

        foreach (var section in sections)
        {
            Walk(section);
        }

        return chapters;
    }

    private static string CollectText(BookSection node) =>
        BookText.Trim(string.Join(' ', node.Children.Select(CollectText).Prepend(node.OwnText)));
}
