using LanguageLab.Application.Books;
using static LanguageLab.Tests.Books.BookFixtures;

namespace LanguageLab.Tests.Books;

/// <summary>Ported from the parseEpub cases of web/src/books/epub.test.ts.</summary>
public class EpubParserTests
{
    private static EpubBook Parse(byte[] zip) => EpubParser.Parse(SafeZip.Read(zip), CancellationToken.None);

    private static readonly EpubBook Book = Parse(Epub3Bytes());

    /// <summary>Chapters as (title, paragraphs) pairs: records holding lists compare the lists by reference.</summary>
    private static void AssertChapters(EpubBook book, params (string Title, string[] Paragraphs)[] expected)
    {
        Assert.Equal(expected.Select(chapter => chapter.Title), book.Chapters.Select(chapter => chapter.Title));

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Paragraphs, book.Chapters[i].Paragraphs);
        }
    }

    [Fact]
    public void Takes_the_title_and_the_author_from_the_package_metadata()
    {
        Assert.Equal("Death's End", Book.Title);
        Assert.Equal("Cixin Liu", Book.Author);
    }

    [Fact]
    public void Leaves_the_title_empty_when_the_package_names_none()
    {
        var opf = Opf.Replace("<dc:title>Death's End</dc:title>", "");

        Assert.Equal("", Parse(Epub3Bytes(("OEBPS/content.opf", opf))).Title);
    }

    [Fact]
    public void Makes_a_chapter_of_every_readable_spine_document_in_spine_order()
    {
        // cover: linear="no"; nav: the navigation document; notes: epub:type="footnotes";
        // picture: not XHTML; gone: its file is not in the archive; ghost: no manifest item;
        // and ch01 is listed twice.
        Assert.Equal(
            new[] { "Compared to the beginning, fewer individuals were emerging.", "Most men tried to adjust.", "The droplet came at noon." },
            Book.Chapters.Select(chapter => chapter.Paragraphs[0]));
    }

    [Fact]
    public void Keeps_a_document_listed_twice_in_the_spine_only_once()
    {
        Assert.Single(Book.Chapters, chapter => chapter.Paragraphs[0].StartsWith("Compared to the beginning", StringComparison.Ordinal));
    }

    [Fact]
    public void Skips_a_spine_item_with_no_manifest_entry_and_one_whose_file_is_missing()
    {
        Assert.Equal(3, Book.Chapters.Count);
    }

    [Fact]
    public void Resolves_hrefs_against_the_package_directory_percent_decoded_and_with_dot_dot_collapsed()
    {
        Assert.Equal(new[] { "Most men tried to adjust." }, Book.Chapters[1].Paragraphs);
        Assert.Equal(new[] { "The droplet came at noon." }, Book.Chapters[2].Paragraphs);
    }

    [Fact]
    public void Names_a_chapter_by_its_first_heading_when_the_toc_does_not()
    {
        Assert.Equal("Chapter Three", Book.Chapters[2].Title);
    }

    [Fact]
    public void Keeps_every_paragraph_of_a_chapter()
    {
        Assert.Equal(
            new[] { "Compared to the beginning, fewer individuals were emerging.", "They still formed a stratum." },
            Book.Chapters[0].Paragraphs);
    }

    [Fact]
    public void Refuses_an_archive_with_no_container_no_package_or_no_text()
    {
        Assert.Throws<BookFormatException>(() => Parse(Zip(("mimetype", "application/epub+zip"))));
        Assert.Throws<BookFormatException>(() => Parse(Zip(("META-INF/container.xml", ContainerXml))));
        Assert.Throws<BookFormatException>(() => Parse(Epub3Bytes(
            ("OEBPS/text/ch01.xhtml", XhtmlDoc("""<div><img src="x.png"/></div>""")),
            ("OEBPS/text/ch 02.xhtml", XhtmlDoc("<p> </p>")),
            ("OEBPS/text/ch03.xhtml", XhtmlDoc("")))));
    }

    [Fact]
    public void Names_chapters_from_an_epub_3_navigation_document_the_first_entry_winning_per_file()
    {
        // ch01 also has an <h1>1</h1>; the TOC label must win. Its second TOC entry
        // ("The Swordholder, later") points at the same file and must not rename it.
        Assert.Equal(new[] { "The Swordholder", "Year 62", "Chapter Three" }, Book.Chapters.Select(chapter => chapter.Title));
    }

    [Fact]
    public void Names_chapters_from_an_epub_2_toc_ncx_when_there_is_no_navigation_document()
    {
        const string ncx = """
            <?xml version="1.0" encoding="utf-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <navMap>
                <navPoint id="p1" playOrder="1">
                  <navLabel><text>The Swordholder</text></navLabel>
                  <content src="text/ch01.xhtml"/>
                  <navPoint id="p1a" playOrder="2">
                    <navLabel><text>Later that day</text></navLabel>
                    <content src="text/ch01.xhtml#later"/>
                  </navPoint>
                </navPoint>
                <navPoint id="p2" playOrder="3">
                  <navLabel><text>Year 62</text></navLabel>
                  <content src="text/ch%2002.xhtml"/>
                </navPoint>
              </navMap>
            </ncx>
            """;
        var opf = Opf
            .Replace("""<item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>""", """<item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>""")
            .Replace("<spine>", """<spine toc="ncx">""")
            .Replace("""<itemref idref="nav"/>""", "");

        var epub2 = Parse(Epub3Bytes(("OEBPS/content.opf", opf), ("OEBPS/toc.ncx", ncx)));

        Assert.Equal(new[] { "The Swordholder", "Year 62", "Chapter Three" }, epub2.Chapters.Select(chapter => chapter.Title));
    }

    [Fact]
    public void Resolves_toc_hrefs_against_the_toc_document_not_the_package()
    {
        // The nav document moves one level down, so its hrefs lose the "text/" prefix.
        var nav = XhtmlDoc("""<nav epub:type="toc"><ol><li><a href="ch01.xhtml">The Swordholder</a></li></ol></nav>""");
        var opf = Opf.Replace("href=\"nav.xhtml\"", "href=\"text/nav.xhtml\"");

        var moved = Parse(Epub3Bytes(("OEBPS/content.opf", opf), ("OEBPS/text/nav.xhtml", nav)));

        Assert.Equal("The Swordholder", moved.Chapters[0].Title);
    }

    private static EpubBook SingleDocument(string body, string nav)
    {
        var opf = Opf.Replace("""<itemref idref="c2"/>""", "").Replace("""<itemref idref="c3"/>""", "");

        return Parse(Epub3Bytes(
            ("OEBPS/content.opf", opf),
            ("OEBPS/nav.xhtml", XhtmlDoc($"""<nav epub:type="toc"><ol>{nav}</ol></nav>""")),
            ("OEBPS/text/ch01.xhtml", XhtmlDoc(body))));
    }

    [Fact]
    public void Cuts_the_document_into_a_chapter_at_every_toc_fragment_named_by_its_toc_label()
    {
        var book = SingleDocument(
            """<p>Front matter.</p><h2 id="one">One</h2><p>First text.</p><p>More first text.</p><section id="two"><h2>Two</h2><p>Second text.</p></section>""",
            """<li><a href="text/ch01.xhtml">Contents</a></li><li><a href="text/ch01.xhtml#one">Chapter One</a></li><li><a href="text/ch01.xhtml#two">Chapter Two</a></li>""");

        AssertChapters(book,
            ("Contents", ["Front matter."]),
            ("Chapter One", ["First text.", "More first text."]),
            ("Chapter Two", ["Second text."]));
    }

    [Fact]
    public void Cuts_in_document_order_before_the_paragraph_an_anchor_sits_inside()
    {
        var book = SingleDocument(
            """<p>Start.</p><p><a id="b"/>Bravo text.</p><p>Also bravo.</p><div><p id="a">Alpha text.</p></div>""",
            """<li><a href="text/ch01.xhtml#a">Alpha</a></li><li><a href="text/ch01.xhtml#b">Bravo</a></li>""");

        AssertChapters(book,
            ("", ["Start."]),
            ("Bravo", ["Bravo text.", "Also bravo."]),
            ("Alpha", ["Alpha text."]));
    }

    [Fact]
    public void Drops_a_toc_entry_with_no_text_before_the_next_one_and_ignores_a_fragment_the_document_lacks()
    {
        var book = SingleDocument(
            """<h1 id="part">Part One</h1><h2 id="c1">1</h2><p>Only text.</p>""",
            """<li><a href="text/ch01.xhtml#part">Part One</a></li><li><a href="text/ch01.xhtml#c1">Chapter 1</a></li><li><a href="text/ch01.xhtml#missing">Nowhere</a></li>""");

        AssertChapters(book, ("Chapter 1", ["Only text."]));
    }

    [Fact]
    public void Cuts_a_book_whose_paragraphs_are_divs_as_well()
    {
        var book = SingleDocument(
            """<div><div>Opening line.</div><div id="next">Next line.</div></div>""",
            """<li><a href="text/ch01.xhtml">Opening</a></li><li><a href="text/ch01.xhtml#next">Next</a></li>""");

        Assert.Equal(new[] { "Opening", "Next" }, book.Chapters.Select(chapter => chapter.Title));
    }
}
