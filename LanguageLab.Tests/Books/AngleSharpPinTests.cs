using AngleSharp.Xml.Parser;

namespace LanguageLab.Tests.Books;

/// <summary>
/// What the port relies on in AngleSharp (rulings R1, R2, R7 of the C2 plan), pinned so that a
/// package update that changes it fails here first.
/// </summary>
public class AngleSharpPinTests
{
    [Fact]
    public void AngleSharp_xml_parser_throws_on_markup_that_is_not_well_formed()
    {
        Assert.Throws<XmlParseException>(() => new XmlParser().ParseDocument("<r><p>a<br>b</r>"));
        Assert.Throws<XmlParseException>(() => new XmlParser().ParseDocument("<r>a&nbsp;b</r>"));
    }

    [Fact]
    public void AngleSharp_xml_parser_resolves_no_external_entity()
    {
        const string xxe = """<?xml version="1.0"?><!DOCTYPE r [<!ENTITY x SYSTEM "file:///etc/passwd">]><r>a&x;b</r>""";

        Assert.Throws<XmlParseException>(() => new XmlParser().ParseDocument(xxe));
    }

    [Fact]
    public void AngleSharp_xml_parser_expands_internal_entities_which_is_why_they_never_reach_it()
    {
        const string declared = """<?xml version="1.0"?><!DOCTYPE r [<!ENTITY x "EXPANDED">]><r>&x;</r>""";

        Assert.Equal("EXPANDED", new XmlParser().ParseDocument(declared).DocumentElement.TextContent);
    }
}
