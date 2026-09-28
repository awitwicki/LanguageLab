using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Xml.Parser;

namespace LanguageLab.Application.Books;

/// <summary>
/// parseDocument and the DOM helpers of web/src/books/epub.ts — change both together. Every file
/// an epub is read from goes through <see cref="Parse"/>, as it does through documentOf in the SPA.
/// The parsers are built without a browsing context's loaders: nothing is fetched, nothing runs.
/// </summary>
internal static class XhtmlDocument
{
    private const string OpsNamespace = "http://www.idpf.org/2007/ops";

    /// <summary>
    /// XML first; an epub whose documents are served as text/html is not always well-formed XML,
    /// and refusing those would lose readable books, so fall back to the HTML parser. Two inputs
    /// skip the XML parser: a DTD declaring entities (AngleSharp.Xml expands internal entities with
    /// no cap — a billion-laughs document would exhaust memory) and markup nested past
    /// <see cref="MarkupDepth.Limit"/> (it would overflow the stack). A parsed tree nested past the
    /// limit is refused, and <paramref name="cancellationToken"/> bounds the time spent: the HTML
    /// parser's cost grows with depth times size.
    /// </summary>
    public static IDocument? Parse(string source, CancellationToken cancellationToken)
    {
        if (!source.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase) && MarkupDepth.Of(source) <= MarkupDepth.Limit)
        {
            try
            {
                return new XmlParser().ParseDocumentAsync(source, cancellationToken).GetAwaiter().GetResult();
            }
            catch (XmlParseException)
            {
                // Not well-formed XML: read it as HTML below.
            }
        }

        var html = new HtmlParser().ParseDocumentAsync(source, cancellationToken).GetAwaiter().GetResult();

        if (DepthOf(html) > MarkupDepth.Limit)
        {
            throw new BookFormatException(BookFormatError.Invalid, "document nesting limit exceeded");
        }

        return html.Body is null ? null : html;
    }

    /// <summary>Every element under <paramref name="root"/> in document order — the document's root included.</summary>
    public static IEnumerable<IElement> Elements(INode root) => root switch
    {
        IDocument document => document.GetElementsByTagName("*"),
        IElement element => element.GetElementsByTagName("*"),
        _ => [],
    };

    public static string NameOf(IElement element) => element.LocalName.ToLowerInvariant();

    /// <summary>epub:type, whether or not the document declares the prefix.</summary>
    public static IReadOnlyList<string> EpubType(IElement element)
    {
        var value = element.GetAttribute(OpsNamespace, "type") ?? element.GetAttribute("epub:type") ?? "";

        return value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    public static IElement? FirstByName(INode root, string name) =>
        Elements(root).FirstOrDefault(element => NameOf(element) == name);

    public static IReadOnlyList<IElement> AllByName(INode root, string name) =>
        Elements(root).Where(element => NameOf(element) == name).ToList();

    public static IElement? BodyOf(IDocument doc) => FirstByName(doc, "body") ?? doc.DocumentElement;

    /// <summary>An element's text with whitespace collapsed; "" for none.</summary>
    public static string TextOf(IElement? element) => BookText.Collapse(element?.TextContent ?? "");

    /// <summary>The deepest element nesting of a parsed tree, walked without recursion.</summary>
    private static int DepthOf(IDocument doc)
    {
        var max = 0;
        var stack = new Stack<(INode Node, int Depth)>();
        stack.Push((doc, 0));

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            max = Math.Max(max, depth);

            for (var child = node.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is IElement)
                {
                    stack.Push((child, depth + 1));
                }
            }
        }

        return max;
    }
}
