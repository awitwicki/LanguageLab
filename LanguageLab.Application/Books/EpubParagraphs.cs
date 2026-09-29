using AngleSharp.Dom;
using static LanguageLab.Application.Books.XhtmlDocument;

namespace LanguageLab.Application.Books;

/// <summary>
/// A document in reading order: a paragraph's text, or the place where an element with one of the
/// TOC's fragment ids starts — the only way a TOC says where a chapter inside a document begins.
/// Exactly one of the two is set.
/// </summary>
internal readonly record struct EpubBlock(string? Text, string? Cut);

/// <summary>
/// The paragraph rules of web/src/books/epub.ts (collectParagraphs, collectTextBlocks,
/// textWithBreaks, markCuts, blocksOf) — change both together.
/// </summary>
internal static class EpubParagraphs
{
    /// <summary>Elements whose text is one paragraph of the book — the epub counterpart of fb2's &lt;p&gt; and &lt;v&gt;.</summary>
    private static readonly HashSet<string> ParagraphTags = ["p", "li", "blockquote", "pre", "figcaption"];

    /// <summary>
    /// Never the book's text: a heading is the chapter's title (fb2's parser leaves &lt;title&gt; out
    /// for the same reason), and navigation, scripts and footnote boilerplate would enter the
    /// dictionary as words.
    /// </summary>
    private static readonly HashSet<string> SkipTags = ["h1", "h2", "h3", "h4", "h5", "h6", "script", "style", "nav"];

    private static readonly HashSet<string> NoteTypes = ["footnote", "endnote", "rearnote", "noteref"];

    private delegate void Collector(IElement element, IReadOnlySet<string> cuts, List<EpubBlock> into);

    /// <summary>One XHTML document's paragraphs — documentParagraphs, the entry point of the SPA's tests.</summary>
    public static IReadOnlyList<string> DocumentParagraphs(string xhtml)
    {
        var doc = Parse(xhtml, CancellationToken.None);

        return doc is null ? [] : TextsOf(BlocksOf(doc, new HashSet<string>()));
    }

    /// <summary>
    /// The document's blocks. The fallback to text-bearing elements is decided on what is left
    /// after trimming: a document of empty &lt;p&gt; elements is as good as one that has none.
    /// </summary>
    public static IReadOnlyList<EpubBlock> BlocksOf(IDocument doc, IReadOnlySet<string> cuts)
    {
        var body = BodyOf(doc);

        if (body is null)
        {
            return [];
        }

        var blocks = Collected(body, cuts, CollectParagraphs);

        return blocks.Any(block => block.Text is not null) ? blocks : Collected(body, cuts, CollectTextBlocks);
    }

    public static IReadOnlyList<string> TextsOf(IEnumerable<EpubBlock> blocks) =>
        blocks.Where(block => block.Text is not null).Select(block => block.Text!).ToList();

    private static bool Skipped(IElement element) =>
        SkipTags.Contains(NameOf(element)) || EpubType(element).Any(NoteTypes.Contains);

    /// <summary>
    /// An element's text, depth-first, with a separator wherever plain concatenation would run two
    /// words together: at &lt;br&gt; and around a nested paragraph tag. Skipped elements contribute
    /// nothing, even nested inside the paragraph this is called on.
    /// </summary>
    private static string TextWithBreaks(INode node)
    {
        if (node.NodeType == NodeType.Text)
        {
            return node.TextContent;
        }

        if (node is not IElement element)
        {
            return "";
        }

        if (Skipped(element))
        {
            return "";
        }

        if (NameOf(element) == "br")
        {
            return " ";
        }

        var inner = string.Concat(element.ChildNodes.Select(TextWithBreaks));

        return ParagraphTags.Contains(NameOf(element)) ? $" {inner} " : inner;
    }

    /// <summary>
    /// The fragment ids of <paramref name="element"/> that the TOC points at, added as cuts.
    /// <paramref name="deep"/> also takes its descendants', for an element the walk does not
    /// descend into: an anchor inside a paragraph cuts before that paragraph, and one on a skipped
    /// heading (the usual place for it) still counts.
    /// </summary>
    private static void MarkCuts(IElement element, IReadOnlySet<string> cuts, List<EpubBlock> into, bool deep)
    {
        if (cuts.Count == 0)
        {
            return;
        }

        var candidates = deep ? Elements(element).Prepend(element) : [element];

        foreach (var candidate in candidates)
        {
            if (candidate.GetAttribute("id") is { Length: > 0 } id && cuts.Contains(id))
            {
                into.Add(new EpubBlock(null, id));
            }
        }
    }

    private static void CollectParagraphs(IElement element, IReadOnlySet<string> cuts, List<EpubBlock> into)
    {
        foreach (var child in element.Children)
        {
            if (Skipped(child))
            {
                MarkCuts(child, cuts, into, deep: true);
                continue;
            }

            if (ParagraphTags.Contains(NameOf(child)))
            {
                MarkCuts(child, cuts, into, deep: true);
                into.Add(new EpubBlock(TextWithBreaks(child), null));
                continue;
            }

            MarkCuts(child, cuts, into, deep: false);
            CollectParagraphs(child, cuts, into);
        }
    }

    /// <summary>Some books use a &lt;div&gt; per paragraph and no &lt;p&gt; at all: take every element holding its own text.</summary>
    private static void CollectTextBlocks(IElement element, IReadOnlySet<string> cuts, List<EpubBlock> into)
    {
        foreach (var child in element.Children)
        {
            if (Skipped(child))
            {
                MarkCuts(child, cuts, into, deep: true);
                continue;
            }

            var ownText = child.ChildNodes.Any(node =>
                node.NodeType == NodeType.Text && BookText.Trim(node.TextContent) != "");

            if (ownText)
            {
                MarkCuts(child, cuts, into, deep: true);
                into.Add(new EpubBlock(TextWithBreaks(child), null));
            }
            else
            {
                MarkCuts(child, cuts, into, deep: false);
                CollectTextBlocks(child, cuts, into);
            }
        }
    }

    private static List<EpubBlock> Collected(IElement body, IReadOnlySet<string> cuts, Collector collect)
    {
        var raw = new List<EpubBlock>();

        collect(body, cuts, raw);

        return raw
            .Select(block => block.Text is null ? block : new EpubBlock(BookText.Collapse(block.Text), null))
            .Where(block => block.Text is null || block.Text != "")
            .ToList();
    }
}
