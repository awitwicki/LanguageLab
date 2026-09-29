using LanguageLab.Application.Books;
using static LanguageLab.Tests.Books.BookFixtures;

namespace LanguageLab.Tests.Books;

/// <summary>Ported from the documentParagraphs cases of web/src/books/epub.test.ts.</summary>
public class EpubParagraphsTests
{
    private static IReadOnlyList<string> Paragraphs(string xhtml) => EpubParagraphs.DocumentParagraphs(xhtml);

    [Fact]
    public void Takes_one_paragraph_per_block_element_in_document_order()
    {
        var paragraphs = Paragraphs(XhtmlDoc("<p>The silo was quiet.</p><blockquote><p>Holston climbed.</p></blockquote><ul><li>A list line.</li></ul>"));

        Assert.Equal(new[] { "The silo was quiet.", "Holston climbed.", "A list line." }, paragraphs);
    }

    [Fact]
    public void Leaves_out_headings_scripts_navigation_and_footnotes()
    {
        var paragraphs = Paragraphs(XhtmlDoc(
            "<h1>Chapter One</h1><p>Real text.</p><script>var a = 1;</script><style>p{color:red}</style>" +
            """<nav epub:type="toc"><ol><li><a href="ch01.xhtml">Chapter One</a></li></ol></nav>""" +
            """<aside epub:type="footnote"><p>A footnote.</p></aside>"""));

        Assert.Equal(new[] { "Real text." }, paragraphs);
    }

    [Fact]
    public void Collapses_whitespace_and_drops_empty_paragraphs()
    {
        Assert.Equal(new[] { "The silo was quiet." }, Paragraphs(XhtmlDoc("<p>  The   silo\n was quiet. </p><p> </p><p></p>")));
    }

    [Fact]
    public void Falls_back_to_text_bearing_elements_in_a_book_whose_paragraphs_are_divs()
    {
        var paragraphs = Paragraphs(XhtmlDoc("""<div class="body"><div>First line.</div><div>Second line.</div></div>"""));

        Assert.Equal(new[] { "First line.", "Second line." }, paragraphs);
    }

    [Fact]
    public void Reads_a_document_that_is_html_rather_than_well_formed_xml()
    {
        Assert.Equal(new[] { "Unclosed paragraph. Still text." }, Paragraphs("<html><body><p>Unclosed paragraph.<br>Still text.</body></html>"));
    }

    [Fact]
    public void Yields_nothing_for_a_document_with_no_text_at_all()
    {
        Assert.Empty(Paragraphs(XhtmlDoc("""<div><img src="cover.png"/></div>""")));
    }

    [Fact]
    public void Inserts_a_separator_at_br_and_between_nested_blocks_instead_of_gluing_words_together()
    {
        var paragraphs = Paragraphs(XhtmlDoc("<blockquote><p>He climbed.</p><p>She fell.</p></blockquote><p>The wind<br/>blew hard.</p>"));

        Assert.Equal(new[] { "He climbed. She fell.", "The wind blew hard." }, paragraphs);
    }

    [Fact]
    public void Does_not_let_a_nested_script_or_heading_leak_into_a_paragraph_it_sits_inside()
    {
        Assert.Equal(new[] { "Before.After." }, Paragraphs(XhtmlDoc("<p>Before.<script>var a = 1;</script>After.</p>")));
    }

    [Fact]
    public void Reads_dirty_xhtml_with_html_entities_and_an_unclosed_br_through_the_html_parser()
    {
        var dirty = """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>x</title></head>
            <body><p>One&nbsp;two&mdash;three<br>four.</p><p>Five.</p></body></html>
            """;

        Assert.Equal(new[] { "One two—three four.", "Five." }, Paragraphs(dirty));
    }

    [Fact]
    public void Honours_epub_type_on_a_document_that_never_declares_the_prefix()
    {
        var paragraphs = Paragraphs("""<html><body><p>Kept.</p><aside epub:type="footnote"><p>Dropped.</p></aside></body></html>""");

        Assert.Equal(new[] { "Kept." }, paragraphs);
    }

    [Fact]
    public void Reads_upper_case_tags_like_lower_case_ones()
    {
        Assert.Equal(new[] { "Upper." }, Paragraphs(XhtmlDoc("<H1>Title</H1><P>Upper.</P>")));
    }

    [Fact]
    public void Marks_a_cut_before_the_paragraph_that_holds_the_toc_anchor()
    {
        var doc = XhtmlDocument.Parse(XhtmlDoc("""<p>Before.</p><p><a id="here"/>After.</p><p id="other">Not a cut.</p>"""), CancellationToken.None)!;

        var blocks = EpubParagraphs.BlocksOf(doc, new HashSet<string> { "here" });

        Assert.Equal(
            new[] { new EpubBlock("Before.", null), new EpubBlock(null, "here"), new EpubBlock("After.", null), new EpubBlock("Not a cut.", null) },
            blocks);
    }
}
