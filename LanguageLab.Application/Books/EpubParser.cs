using AngleSharp.Dom;
using static LanguageLab.Application.Books.XhtmlDocument;

namespace LanguageLab.Application.Books;

/// <summary>One chapter: an XHTML document of the spine, or the part of one between two places its TOC points at.</summary>
/// <param name="Title">Empty when neither the TOC nor a heading names it — shown as an ordinal, as for fb2.</param>
internal sealed record EpubChapter(string Title, IReadOnlyList<string> Paragraphs);

/// <param name="Title">"" when the package names none; the caller's fallback is the file name.</param>
/// <param name="Author">"" when the package names none.</param>
internal sealed record EpubBook(string Title, string Author, IReadOnlyList<EpubChapter> Chapters);

/// <summary>
/// A port of parseEpub in web/src/books/epub.ts and fromEpub in web/src/books/toParsedBook.ts —
/// change both together.
/// </summary>
internal static class EpubParser
{
    private static readonly HashSet<string> XhtmlTypes = ["application/xhtml+xml", "text/html"];

    /// <summary>Documents that are apparatus, not the book — the epub counterpart of fb2's &lt;body name="notes"&gt;.</summary>
    private static readonly HashSet<string> ApparatusTypes = ["toc", "landmarks", "footnotes", "endnotes", "rearnotes"];

    /// <summary>
    /// An epub → the book. Chapters are the spine's XHTML documents in the spine's own order, each
    /// cut further wherever the TOC points inside it (<see cref="ChaptersOf"/>); the navigation
    /// document, apparatus, non-XHTML items, linear="no" items, items whose file or manifest entry
    /// is missing, and a document listed a second time are all left out.
    /// </summary>
    public static EpubBook Parse(IReadOnlyDictionary<string, byte[]> entries, CancellationToken cancellationToken)
    {
        var container = EpubToc.DocumentOf(entries, "META-INF/container.xml", cancellationToken);
        var opfPath = container is null ? null : FirstByName(container, "rootfile")?.GetAttribute("full-path");
        var opf = string.IsNullOrEmpty(opfPath)
            ? null
            : EpubToc.DocumentOf(entries, EpubToc.ResolvePath("", opfPath), cancellationToken);

        if (string.IsNullOrEmpty(opfPath) || opf is null)
        {
            throw new BookFormatException(BookFormatError.Invalid, "epub package not found");
        }

        var @base = EpubToc.DirOf(EpubToc.ResolvePath("", opfPath));
        var metadata = FirstByName(opf, "metadata");
        var manifest = ReadManifest(opf, @base);
        var toc = EpubToc.Entries(entries, opf, manifest, cancellationToken);
        var spine = FirstByName(opf, "spine");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chapters = new List<EpubChapter>();

        foreach (var itemRef in spine is null ? [] : AllByName(spine, "itemref"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (itemRef.GetAttribute("linear") == "no")
            {
                continue;
            }

            if (!manifest.TryGetValue(itemRef.GetAttribute("idref") ?? "", out var item)
                || !XhtmlTypes.Contains(item.MediaType)
                || item.Properties.Contains("nav")
                || !seen.Add(item.Path))
            {
                continue;
            }

            var doc = EpubToc.DocumentOf(entries, item.Path, cancellationToken);

            if (doc is null || IsApparatus(doc))
            {
                continue;
            }

            chapters.AddRange(ChaptersOf(doc, toc.TryGetValue(item.Path, out var entriesOfDoc) ? entriesOfDoc : []));
        }

        if (chapters.Count == 0)
        {
            throw new BookFormatException(BookFormatError.Invalid, "epub has no readable chapters");
        }

        return new EpubBook(
            metadata is null ? "" : TextOf(FirstByName(metadata, "title")),
            metadata is null ? "" : TextOf(FirstByName(metadata, "creator")),
            chapters);
    }

    /// <summary>
    /// fromEpub: an epub's chapters are flat, so every one becomes one depth-1 section, its text the
    /// paragraphs joined by a space; <see cref="BookChapters.Flatten"/> then yields them one for one
    /// at any level.
    /// </summary>
    public static ParsedBook ToParsedBook(EpubBook book) => new(
        book.Title,
        book.Author == "" ? null : book.Author,
        book.Chapters.Select(chapter => new BookSection(chapter.Title, 1, string.Join(' ', chapter.Paragraphs), [])).ToList(),
        1);

    private static Dictionary<string, ManifestItem> ReadManifest(IDocument opf, string @base)
    {
        var items = new Dictionary<string, ManifestItem>(StringComparer.Ordinal);
        INode scope = FirstByName(opf, "manifest") ?? (INode)opf;

        foreach (var item in AllByName(scope, "item"))
        {
            var id = item.GetAttribute("id");
            var href = item.GetAttribute("href");

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(href))
            {
                continue;
            }

            items[id] = new ManifestItem(
                EpubToc.ResolvePath(@base, href),
                (item.GetAttribute("media-type") ?? "").ToLowerInvariant(),
                (item.GetAttribute("properties") ?? "").ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        return items;
    }

    private static string HeadingOf(IDocument doc)
    {
        foreach (var element in Elements(doc))
        {
            if (NameOf(element) is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" && TextOf(element) is { Length: > 0 } text)
            {
                return text;
            }
        }

        return "";
    }

    private static bool IsApparatus(IDocument doc) =>
        new[] { doc.DocumentElement, BodyOf(doc) }
            .Where(element => element is not null)
            .Any(element => EpubType(element!).Any(ApparatusTypes.Contains));

    /// <summary>
    /// One spine document → its chapters. Every TOC fragment found in the document starts a chapter
    /// named by its label; what comes before the first of them is named by the document's other TOC
    /// entry (a bare href, or a fragment the document does not have), and when the TOC cuts
    /// nothing, by its first heading. A chapter left with no text — a part title right before its
    /// first chapter — is dropped.
    /// </summary>
    private static IEnumerable<EpubChapter> ChaptersOf(IDocument doc, IReadOnlyList<TocEntry> toc)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in toc.Where(entry => entry.Fragment != ""))
        {
            labels[entry.Fragment] = entry.Label;
        }

        var blocks = EpubParagraphs.BlocksOf(doc, labels.Keys.ToHashSet(StringComparer.Ordinal));
        var found = blocks.Where(block => block.Cut is not null).Select(block => block.Cut!).ToHashSet(StringComparer.Ordinal);
        var opening = toc.FirstOrDefault(entry => !found.Contains(entry.Fragment))?.Label;
        var title = opening ?? (found.Count == 0 ? HeadingOf(doc) : "");
        var paragraphs = new List<string>();
        var chapters = new List<EpubChapter> { new(title, paragraphs) };

        foreach (var block in blocks)
        {
            if (block.Cut is not null)
            {
                paragraphs = [];
                chapters.Add(new EpubChapter(labels.GetValueOrDefault(block.Cut, ""), paragraphs));
            }
            else
            {
                paragraphs.Add(block.Text!);
            }
        }

        return chapters.Where(chapter => chapter.Paragraphs.Count > 0);
    }
}
