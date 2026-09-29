using System.Diagnostics;
using System.IO.Compression;
using LanguageLab.Application.Books;
using static LanguageLab.Tests.Books.BookFixtures;

namespace LanguageLab.Tests.Books;

/// <summary>The upload comes from any signed-in user: the parser is an attack surface.</summary>
public class BookParserSecurityTests
{
    private static readonly BookParser Parser = new();

    private const string BookSentence = "The droplet came at noon and nobody moved.";

    private static BookFormatException Refusal(byte[] file) => Assert.Throws<BookFormatException>(() => Parser.Parse(file));

    /// <summary>
    /// Runs <paramref name="action"/> on a thread with a 1 MB stack — smaller than any server
    /// thread's — so unbounded recursion fails here instead of passing on a roomy test thread.
    /// A stack overflow cannot be caught: without the depth guard this test run crashes.
    /// </summary>
    private static Exception? OnSmallStack(Action action)
    {
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                thrown = e;
            }
        }, 1024 * 1024);

        thread.Start();
        thread.Join();

        return thrown;
    }

    private static string Nested(string open, string close, int depth, string inner) =>
        string.Concat(Enumerable.Repeat(open, depth)) + inner + string.Concat(Enumerable.Repeat(close, depth));

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://127.0.0.1:9/secret")]
    public void An_fb2_referencing_an_external_entity_is_refused_without_resolving_it(string systemId)
    {
        var fb2 = $"""
            <?xml version="1.0"?>
            <!DOCTYPE FictionBook [<!ENTITY secret SYSTEM "{systemId}">]>
            <FictionBook><body><section><p>&secret;</p></section></body></FictionBook>
            """;

        Assert.Equal(BookFormatError.Invalid, Refusal(Utf8(fb2)).Error);
    }

    [Fact]
    public void An_fb2_declaring_an_external_entity_it_never_uses_still_parses()
    {
        var fb2 = """
            <?xml version="1.0"?>
            <!DOCTYPE FictionBook [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
            <FictionBook><body><section><p>plain text</p></section></body></FictionBook>
            """;

        Assert.Equal("plain text", Assert.Single(Parser.Parse(Utf8(fb2)).Sections).OwnText);
    }

    [Fact]
    public void A_billion_laughs_fb2_is_refused_at_once()
    {
        var entities = string.Concat(Enumerable.Range(1, 9).Select(i =>
            $"<!ENTITY l{i} \"{string.Concat(Enumerable.Repeat($"&l{i - 1};", 10))}\">"));
        var fb2 = $"""
            <?xml version="1.0"?>
            <!DOCTYPE FictionBook [<!ENTITY l0 "lol">{entities}]>
            <FictionBook><body><section><p>&l9;</p></section></body></FictionBook>
            """;
        var clock = Stopwatch.StartNew();

        Assert.Equal(BookFormatError.Invalid, Refusal(Utf8(fb2)).Error);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_billion_laughs_xhtml_document_is_read_without_expanding_its_entities()
    {
        var entities = string.Concat(Enumerable.Range(1, 9).Select(i =>
            $"<!ENTITY l{i} \"{string.Concat(Enumerable.Repeat($"&l{i - 1};", 10))}\">"));
        var chapter = $"""
            <?xml version="1.0"?>
            <!DOCTYPE html [<!ENTITY l0 "lol">{entities}]>
            <html xmlns="http://www.w3.org/1999/xhtml"><body><p>{BookSentence}</p><p>&l9;</p></body></html>
            """;

        var book = Parser.Parse(Epub3Bytes(("OEBPS/text/ch03.xhtml", chapter)));

        var text = book.Sections[2].OwnText;
        Assert.Contains(BookSentence, text);
        Assert.DoesNotContain("lollol", text);
    }

    [Fact]
    public void An_xhtml_document_referencing_an_external_entity_never_resolves_it()
    {
        var chapter = $"""
            <?xml version="1.0"?>
            <!DOCTYPE html [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
            <html xmlns="http://www.w3.org/1999/xhtml"><body><p>{BookSentence}</p><p>&secret;</p></body></html>
            """;

        var text = Parser.Parse(Epub3Bytes(("OEBPS/text/ch03.xhtml", chapter))).Sections[2].OwnText;

        Assert.Contains(BookSentence, text);
        Assert.DoesNotContain("root:", text);
    }

    [Fact]
    public void A_zip_with_40_mb_of_zeros_in_one_entry_is_refused()
    {
        var bomb = Zip(new[] { ("book.fb2", new byte[40 * 1024 * 1024], CompressionLevel.SmallestSize) });

        Assert.Equal(BookFormatError.Invalid, Refusal(bomb).Error);
    }

    [Fact]
    public void A_spine_href_climbing_out_of_the_archive_stays_inside_it()
    {
        var opf = Opf.Replace("href=\"text/../text/ch03.xhtml\"", "href=\"../../../etc/passwd\"");

        var book = Parser.Parse(Epub3Bytes(("OEBPS/content.opf", opf)));

        // "etc/passwd" is looked up among the archive's own entries, finds nothing and is skipped.
        Assert.Equal(new[] { "The Swordholder", "Year 62" }, book.Sections.Select(section => section.Title));
    }

    [Fact]
    public void A_refusal_never_quotes_the_book()
    {
        var broken = new[]
        {
            Utf8($"<FictionBook><body><section><p>{BookSentence}</p></body></FictionBook>"),
            Epub3Bytes(("META-INF/encryption.xml", $"<encryption>{BookSentence}</encryption>")),
            Zip(("a.fb2", BookSentence), ("b.fb2", BookSentence)),
            Epub3Bytes(("OEBPS/content.opf", $"<package>{BookSentence}</package>")),
        };

        foreach (var file in broken)
        {
            var error = Refusal(file);

            Assert.DoesNotContain("droplet", error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void An_fb2_nested_past_the_limit_is_refused_instead_of_overflowing_the_stack()
    {
        var fb2 = $"<FictionBook><body>{Nested("<section>", "</section>", 20_000, "<p>deep</p>")}</body></FictionBook>";

        var thrown = OnSmallStack(() => Parser.Parse(Utf8(fb2)));

        Assert.Equal(BookFormatError.Invalid, Assert.IsType<BookFormatException>(thrown).Error);
    }

    [Fact]
    public void An_xhtml_document_nested_past_the_limit_is_refused_instead_of_overflowing_the_stack()
    {
        var chapter = XhtmlDoc(Nested("<div>", "</div>", 20_000, "<p>deep</p>"));

        var thrown = OnSmallStack(() => Parser.Parse(Epub3Bytes(("OEBPS/text/ch03.xhtml", chapter))));

        Assert.Equal(BookFormatError.Invalid, Assert.IsType<BookFormatException>(thrown).Error);
    }

    [Fact]
    public void An_xhtml_document_hiding_its_depth_behind_a_processing_instruction_is_refused_instead_of_overflowing_the_stack()
    {
        // MarkupDepth's PI skip once searched for the first '>' instead of "?>", so a PI containing
        // an accidental '>' followed by "</z>" was read as a real closing tag, cancelling out a real
        // <div> open and hiding depth from the pre-scan that gates AngleSharp.Xml's parser.
        var chapter = XhtmlDoc(string.Concat(Enumerable.Repeat("<div><?p ></z>?>", 20_000)));

        var thrown = OnSmallStack(() => Parser.Parse(Epub3Bytes(("OEBPS/text/ch03.xhtml", chapter))));

        Assert.Equal(BookFormatError.Invalid, Assert.IsType<BookFormatException>(thrown).Error);
    }

    [Fact]
    public void An_xhtml_document_using_non_ascii_element_names_does_not_overflow_the_stack()
    {
        // U+2160 is not char.IsLetter, so MarkupDepth once skipped these tags entirely without
        // counting them, while AngleSharp.Xml still parses and nests them like any other element,
        // letting a shallow-looking document reach the XML parser and overflow the stack. Correctly
        // counted, the document is routed to the HTML parser instead — which does not recognise
        // U+2160 as a tag-name character at all, so it reads as flat text and parses without error;
        // the only thing this proves is that parsing completes instead of crashing the process.
        var chapter = XhtmlDoc(Nested("<Ⅰ>", "</Ⅰ>", 20_000, "x"));

        var thrown = OnSmallStack(() => Parser.Parse(Epub3Bytes(("OEBPS/text/ch03.xhtml", chapter))));

        Assert.Null(thrown);
    }

    [Fact]
    public void A_document_nested_just_inside_the_limit_still_reads()
    {
        var chapter = XhtmlDoc(Nested("<div>", "</div>", 200, $"<p>{BookSentence}</p>"));

        var book = Parser.Parse(Epub3Bytes(("OEBPS/text/ch03.xhtml", chapter)));

        Assert.Equal(BookSentence, book.Sections[2].OwnText);
    }

    [Fact]
    public void A_toc_with_many_fragments_in_one_document_does_not_blow_past_the_deadline()
    {
        // EpubToc.Remember once checked for a duplicate fragment with a linear scan of what it had
        // already collected for that path, making a nav document with N distinct fragments pointing
        // into one file cost O(N^2) — quadratic in a size the zip and entry limits do not bound.
        var links = string.Concat(Enumerable.Range(0, 30_000).Select(i => $"""<li><a href="text/ch01.xhtml#f{i}">f{i}</a></li>"""));
        var nav = XhtmlDoc($"""<nav epub:type="toc"><ol>{links}</ol></nav>""");
        var parser = new BookParser(TimeSpan.FromSeconds(2));
        var clock = Stopwatch.StartNew();

        parser.Parse(Epub3Bytes(("OEBPS/nav.xhtml", nav)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_book_that_takes_too_long_to_parse_is_refused()
    {
        // 50,000 unclosed <div>s: the HTML parser's cost grows with depth times size — seconds of CPU.
        var chapter = "<html><body>" + string.Concat(Enumerable.Repeat("<div>", 50_000)) + "x</body></html>";
        var parser = new BookParser(TimeSpan.FromMilliseconds(200));
        var clock = Stopwatch.StartNew();

        var error = Assert.Throws<BookFormatException>(() => parser.Parse(Epub3Bytes(("OEBPS/text/ch03.xhtml", chapter))));

        Assert.Equal("book parsing time limit exceeded", error.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3));
    }
}
