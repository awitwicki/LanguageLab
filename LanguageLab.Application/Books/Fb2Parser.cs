using System.Xml;
using System.Xml.Linq;

namespace LanguageLab.Application.Books;

/// <summary>
/// A port of parseBook in web/src/fb2/chapters.ts — change both together. Element names are
/// matched by local name, whatever the namespace; the text of an element is all of its descendant
/// text, as the browser's textContent is.
/// </summary>
internal static class Fb2Parser
{
    public static ParsedBook Parse(string xml)
    {
        XDocument doc;

        try
        {
            doc = SafeXml.Load(xml);
        }
        catch (XmlException e)
        {
            throw new BookFormatException(BookFormatError.Invalid, "not a readable fb2", e);
        }

        var title = TitleInfoChild(doc, "book-title") is { } bookTitle ? BookText.Trim(bookTitle.Value) : "";
        var sections = new List<BookSection>();

        foreach (var body in doc.Descendants().Where(element => Is(element, "body")))
        {
            // Footnotes are not the book's text: their numbers and boilerplate would yield garbage "words".
            if ((string?)body.Attribute("name") == "notes")
            {
                continue;
            }

            sections.AddRange(DirectChildSections(body).Select(section => ToSection(section, 1)));
        }

        return new ParsedBook(title, AuthorOf(doc), sections, DepthOf(sections));
    }

    /// <summary>The first element <c>description &gt; title-info &gt; name</c> in document order.</summary>
    private static XElement? TitleInfoChild(XDocument doc, string name) =>
        doc.Descendants().FirstOrDefault(element =>
            Is(element, name) && Is(element.Parent, "title-info") && Is(element.Parent!.Parent, "description"));

    /// <summary>The first author's first and last name, joined by a space; null when both are missing.</summary>
    private static string? AuthorOf(XDocument doc)
    {
        if (TitleInfoChild(doc, "author") is not { } author)
        {
            return null;
        }

        var parts = new[] { "first-name", "last-name" }
            .Select(name => author.Descendants().FirstOrDefault(element => Is(element, name)))
            .Select(element => element is null ? "" : BookText.Trim(element.Value))
            .Where(part => part != "")
            .ToList();

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static IEnumerable<XElement> DirectChildSections(XElement element) =>
        element.Elements().Where(child => Is(child, "section"));

    private static BookSection ToSection(XElement section, int depth)
    {
        var title = section.Elements().FirstOrDefault(child => Is(child, "title"));

        // The title would yield the chapter number as a "word", and nested sections are collected
        // separately — both are left out of the section's own text.
        var ownText = string.Join(' ', section.Elements()
            .Where(child => !Is(child, "section") && !Is(child, "title"))
            .Select(child => child.Value));

        return new BookSection(
            title is null ? "" : BookText.Collapse(title.Value),
            depth,
            ownText,
            DirectChildSections(section).Select(child => ToSection(child, depth + 1)).ToList());
    }

    private static int DepthOf(IEnumerable<BookSection> sections) =>
        sections.Select(section => Math.Max(section.Depth, DepthOf(section.Children))).DefaultIfEmpty(0).Max();

    private static bool Is(XElement? element, string localName) =>
        element is not null && element.Name.LocalName == localName;
}
