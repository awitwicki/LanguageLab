using LanguageLab.Application.Books;

namespace LanguageLab.Tests.Books;

/// <summary>How epub files are parsed: XML first, HTML on failure, within the nesting limit.</summary>
public class XhtmlDocumentTests
{
    [Fact]
    public void Well_formed_xhtml_keeps_xml_semantics()
    {
        // In XML a self-closing <a/> is empty; the HTML parser would open it around the text.
        var doc = XhtmlDocument.Parse("""<html xmlns="http://www.w3.org/1999/xhtml"><body><p><a id="b"/>Text.</p></body></html>""", CancellationToken.None)!;

        Assert.Equal("", doc.GetElementById("b")!.TextContent);
    }

    [Fact]
    public void Markup_that_is_not_xml_is_read_as_html()
    {
        var doc = XhtmlDocument.Parse("<html><body><p>a&nbsp;b</body></html>", CancellationToken.None)!;

        Assert.Equal("a" + (char)0xA0 + "b", doc.Body!.TextContent);
    }

    [Fact]
    public void A_document_declaring_entities_is_read_as_html_without_expanding_them()
    {
        const string declared = """<?xml version="1.0"?><!DOCTYPE r [<!ENTITY x "EXPANDED">]><r>&x;</r>""";

        Assert.DoesNotContain("EXPANDED", XhtmlDocument.Parse(declared, CancellationToken.None)!.Body!.TextContent);
    }

    [Fact]
    public void A_parsed_tree_nested_past_the_limit_is_refused()
    {
        var deep = "<html><body>" + string.Concat(Enumerable.Repeat("<div>", MarkupDepth.Limit + 10)) + "x</body></html>";

        var error = Assert.Throws<BookFormatException>(() => XhtmlDocument.Parse(deep, CancellationToken.None));

        Assert.Equal("document nesting limit exceeded", error.Message);
    }

    [Fact]
    public void Element_names_are_read_lowercase_and_epub_type_with_or_without_its_namespace()
    {
        var xml = XhtmlDocument.Parse("""<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><body><P epub:type="Footnote">x</P></body></html>""", CancellationToken.None)!;
        var html = XhtmlDocument.Parse("""<html><body><p epub:type="footnote">x<br></p></body></html>""", CancellationToken.None)!;

        Assert.Equal("p", XhtmlDocument.NameOf(XhtmlDocument.FirstByName(xml, "p")!));
        Assert.Equal(new[] { "footnote" }, XhtmlDocument.EpubType(XhtmlDocument.FirstByName(xml, "p")!));
        Assert.Equal(new[] { "footnote" }, XhtmlDocument.EpubType(XhtmlDocument.FirstByName(html, "p")!));
    }

    [Fact]
    public void Elements_of_a_document_include_its_root_and_of_an_element_only_its_descendants()
    {
        var doc = XhtmlDocument.Parse("<r><a><b/></a></r>", CancellationToken.None)!;

        Assert.Equal(new[] { "r", "a", "b" }, XhtmlDocument.Elements(doc).Select(XhtmlDocument.NameOf));
        Assert.Equal(new[] { "b" }, XhtmlDocument.Elements(XhtmlDocument.FirstByName(doc, "a")!).Select(XhtmlDocument.NameOf));
    }
}
