using AngleSharp.Dom;
using static LanguageLab.Application.Books.XhtmlDocument;

namespace LanguageLab.Application.Books;

/// <summary>A TOC entry: a label, and the fragment id it points at inside its document ("" for none).</summary>
internal sealed record TocEntry(string Fragment, string Label);

/// <summary>A manifest item: its resolved path, lowercase media type and properties.</summary>
internal sealed record ManifestItem(string Path, string MediaType, IReadOnlyList<string> Properties);

/// <summary>
/// Paths inside the archive and the book's TOC — resolvePath, dirOf, percentDecoded, tocEntries,
/// navEntries, ncxEntries and remember in web/src/books/epub.ts. Change both together.
/// </summary>
internal static class EpubToc
{
    public static string DirOf(string path)
    {
        var slash = path.LastIndexOf('/');

        return slash < 0 ? "" : path[..(slash + 1)];
    }

    /// <summary>
    /// decodeURIComponent. It never throws: a malformed % sequence stays as written, where the SPA
    /// keeps the whole href as written — the same path unless one href mixes both.
    /// </summary>
    public static string PercentDecoded(string raw) => Uri.UnescapeDataString(raw);

    /// <summary>
    /// An href inside the archive: the fragment dropped, percent-decoded, "." and ".." collapsed. A
    /// ".." above the root is dropped, as in the SPA, so the path never leaves the archive: it only
    /// ever looks up the in-memory entries.
    /// </summary>
    public static string ResolvePath(string @base, string href)
    {
        var decoded = PercentDecoded(href.Split('#')[0]);
        var segments = new List<string>();

        foreach (var segment in (@base + decoded).Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// TOC entries by document path, in TOC order. An epub 3 navigation document first, an epub 2
    /// toc.ncx second. Hrefs resolve against the TOC's own directory, which is not always the package's.
    /// </summary>
    public static Dictionary<string, List<TocEntry>> Entries(
        IReadOnlyDictionary<string, byte[]> entries,
        IDocument opf,
        IReadOnlyDictionary<string, ManifestItem> manifest,
        CancellationToken cancellationToken)
    {
        var nav = manifest.Values.FirstOrDefault(item => item.Properties.Contains("nav"));

        if (nav is not null)
        {
            var titles = NavEntries(entries, nav.Path, cancellationToken);

            if (titles.Count > 0)
            {
                return titles;
            }
        }

        var tocId = FirstByName(opf, "spine")?.GetAttribute("toc");
        var ncx = (tocId is { Length: > 0 } && manifest.TryGetValue(tocId, out var byId) ? byId : null)
            ?? manifest.Values.FirstOrDefault(item => item.MediaType == "application/x-dtbncx+xml");

        return ncx is null ? [] : NcxEntries(entries, ncx.Path, cancellationToken);
    }

    public static IDocument? DocumentOf(IReadOnlyDictionary<string, byte[]> entries, string path, CancellationToken cancellationToken) =>
        entries.TryGetValue(path, out var bytes) ? Parse(XmlDecoding.Decode(bytes), cancellationToken) : null;

    private static Dictionary<string, List<TocEntry>> NavEntries(
        IReadOnlyDictionary<string, byte[]> entries, string path, CancellationToken cancellationToken)
    {
        var titles = new Dictionary<string, List<TocEntry>>(StringComparer.Ordinal);

        if (DocumentOf(entries, path, cancellationToken) is not { } doc)
        {
            return titles;
        }

        var navs = AllByName(doc, "nav");
        var toc = navs.FirstOrDefault(nav => EpubType(nav).Contains("toc")) ?? navs.FirstOrDefault();

        if (toc is null)
        {
            return titles;
        }

        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var link in AllByName(toc, "a"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var href = link.GetAttribute("href");
            var label = TextOf(link);

            if (!string.IsNullOrEmpty(href) && label != "")
            {
                Remember(titles, seen, DirOf(path), href, label);
            }
        }

        return titles;
    }

    private static Dictionary<string, List<TocEntry>> NcxEntries(
        IReadOnlyDictionary<string, byte[]> entries, string path, CancellationToken cancellationToken)
    {
        var titles = new Dictionary<string, List<TocEntry>>(StringComparer.Ordinal);

        if (DocumentOf(entries, path, cancellationToken) is not { } doc)
        {
            return titles;
        }

        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // NameOf lowercases every local name, so the names matched against are lowercase too.
        foreach (var point in AllByName(doc, "navpoint"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var src = FirstByName(point, "content")?.GetAttribute("src");
            var label = TextOf(FirstByName(point, "text"));

            if (!string.IsNullOrEmpty(src) && label != "")
            {
                Remember(titles, seen, DirOf(path), src, label);
            }
        }

        return titles;
    }

    /// <summary>
    /// The first entry for a place wins: a second entry pointing at the same spot must not rename
    /// it. <paramref name="seen"/> tracks the fragments already recorded per path so a document with
    /// many entries in one file — no zip or entry limit bounds that count — stays linear instead of
    /// re-scanning <paramref name="titles"/>'s list on every entry.
    /// </summary>
    private static void Remember(
        Dictionary<string, List<TocEntry>> titles, Dictionary<string, HashSet<string>> seen, string @base, string href, string label)
    {
        var path = ResolvePath(@base, href);
        var hash = href.IndexOf('#');
        var fragment = hash < 0 ? "" : PercentDecoded(href[(hash + 1)..]);

        if (!seen.TryGetValue(path, out var fragments))
        {
            seen[path] = fragments = new HashSet<string>(StringComparer.Ordinal);
            titles[path] = [];
        }

        if (fragments.Add(fragment))
        {
            titles[path].Add(new TocEntry(fragment, label));
        }
    }
}
