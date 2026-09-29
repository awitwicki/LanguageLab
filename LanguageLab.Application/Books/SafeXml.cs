using System.Xml;
using System.Xml.Linq;

namespace LanguageLab.Application.Books;

/// <summary>
/// System.Xml for untrusted XML (fb2, encryption.xml): no DTD, no resolver, so no external entity
/// and no entity expansion — a reference to a declared entity is an undeclared-entity error. Throws
/// <see cref="XmlException"/> for anything it will not read, including nesting past
/// <see cref="MarkupDepth.Limit"/>.
/// </summary>
internal static class SafeXml
{
    public static XDocument Load(string xml)
    {
        // A streaming pass first: XmlReader reads iteratively, XElement.Value recurses per level.
        using (var scan = XmlReader.Create(new StringReader(xml), Settings()))
        {
            while (scan.Read())
            {
                if (scan.Depth > MarkupDepth.Limit)
                {
                    throw new XmlException("element nesting limit exceeded");
                }
            }
        }

        using var reader = XmlReader.Create(new StringReader(xml), Settings());

        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        // Moot while DTDs are ignored — kept small in case that ever changes. Not 0: in System.Xml
        // 0 means "no limit".
        MaxCharactersFromEntities = 1_024,
    };
}
