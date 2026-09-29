using System.IO.Compression;
using System.Text;

namespace LanguageLab.Tests.Books;

/// <summary>
/// Test books built in code — a port of web/src/test/epubFixtures.ts and the fb2 of
/// web/src/test/readerFixtures.ts. Change both together.
/// </summary>
internal static class BookFixtures
{
    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Files → a zip, each entry at its own compression level.</summary>
    public static byte[] Zip(IEnumerable<(string Path, byte[] Content, CompressionLevel Level)> files)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content, level) in files)
            {
                using var stream = archive.CreateEntry(path, level).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    public static byte[] Zip(IEnumerable<KeyValuePair<string, byte[]>> files) =>
        Zip(files.Select(file => (file.Key, file.Value, CompressionLevel.Optimal)));

    public static byte[] Zip(params (string Path, string Content)[] files) =>
        Zip(files.Select(file => (file.Path, Utf8(file.Content), CompressionLevel.Optimal)));

    public static string XhtmlDoc(string body, string attributes = "") =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>x</title></head><body{attributes}>{body}</body></html>
        """;

    public const string ContainerXml = """
        <?xml version="1.0"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
        </container>
        """;

    public const string Opf = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:title>Death's End</dc:title>
            <dc:creator>Cixin Liu</dc:creator>
          </metadata>
          <manifest>
            <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
            <item id="cover" href="cover.xhtml" media-type="application/xhtml+xml"/>
            <item id="c1" href="text/ch01.xhtml" media-type="application/xhtml+xml"/>
            <item id="c2" href="text/ch%2002.xhtml" media-type="application/xhtml+xml"/>
            <item id="c3" href="text/../text/ch03.xhtml" media-type="application/xhtml+xml"/>
            <item id="gone" href="text/ch04.xhtml" media-type="application/xhtml+xml"/>
            <item id="notes" href="text/notes.xhtml" media-type="application/xhtml+xml"/>
            <item id="picture" href="images/cover.png" media-type="image/png"/>
          </manifest>
          <spine>
            <itemref idref="cover" linear="no"/>
            <itemref idref="nav"/>
            <itemref idref="c1"/>
            <itemref idref="c2"/>
            <itemref idref="c3"/>
            <itemref idref="gone"/>
            <itemref idref="notes"/>
            <itemref idref="picture"/>
            <itemref idref="ghost"/>
            <itemref idref="c1"/>
          </spine>
        </package>
        """;

    public static readonly string Nav = XhtmlDoc("""
        <nav epub:type="toc"><ol>
            <li><a href="text/ch01.xhtml">The Swordholder</a></li>
            <li><a href="text/ch01.xhtml#later">The Swordholder, later</a></li>
            <li><a href="text/ch%2002.xhtml#start">Year 62</a></li>
          </ol></nav>
        """);

    /// <summary>The whole epub 3 book, in a fresh dictionary each call: override a path to build a variant.</summary>
    public static Dictionary<string, byte[]> Epub3Files() => new()
    {
        ["mimetype"] = Utf8("application/epub+zip"),
        ["META-INF/container.xml"] = Utf8(ContainerXml),
        ["OEBPS/content.opf"] = Utf8(Opf),
        ["OEBPS/nav.xhtml"] = Utf8(Nav),
        ["OEBPS/cover.xhtml"] = Utf8(XhtmlDoc("""<div><img src="images/cover.png"/></div>""")),
        ["OEBPS/text/ch01.xhtml"] = Utf8(XhtmlDoc("<h1>1</h1><p>Compared to the beginning, fewer individuals were emerging.</p><p>They still formed a stratum.</p>")),
        ["OEBPS/text/ch 02.xhtml"] = Utf8(XhtmlDoc("<p>Most men tried to adjust.</p>")),
        ["OEBPS/text/ch03.xhtml"] = Utf8(XhtmlDoc("<h2>Chapter Three</h2><p>The droplet came at noon.</p>")),
        ["OEBPS/text/notes.xhtml"] = Utf8(XhtmlDoc("<p>A footnote.</p>", " epub:type=\"footnotes\"")),
        ["OEBPS/images/cover.png"] = [0x89, 0x50, 0x4e, 0x47],
    };

    public static byte[] Epub3Bytes(params (string Path, string Content)[] overrides)
    {
        var files = Epub3Files();

        foreach (var (path, content) in overrides)
        {
            files[path] = Utf8(content);
        }

        return Zip(files);
    }

    /// <summary>
    /// A small fb2 in the shape real ones have: a default namespace, nested sections, a part title
    /// with no text of its own, a poem, an empty line and a notes body.
    /// </summary>
    public const string ReaderBookXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0">
          <description>
            <title-info>
              <author><first-name>Cixin</first-name><last-name>Liu</last-name></author>
              <book-title>Death's End</book-title>
            </title-info>
          </description>
          <body>
            <section>
              <title><p>Part One</p></title>
              <section>
                <title><p>The Swordholder</p></title>
                <p>Compared to the beginning, fewer individuals were emerging. They still formed a stratum.</p>
                <p>All of them had some difficulty reintegrating.</p>
              </section>
              <section>
                <title><p>Year 62</p></title>
                <poem><stanza><v>The silo was quiet,</v><v>the silo was cold.</v></stanza></poem>
                <empty-line/>
                <p>Most men tried to adjust.</p>
              </section>
            </section>
          </body>
          <body name="notes">
            <section><title><p>Notes</p></title><p>A footnote.</p></section>
          </body>
        </FictionBook>
        """;
}
