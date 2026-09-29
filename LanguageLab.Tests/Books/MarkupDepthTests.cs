using LanguageLab.Application.Books;

namespace LanguageLab.Tests.Books;

public class MarkupDepthTests
{
    [Theory]
    [InlineData("<a><b><c/></b></a>", 2)]
    [InlineData("<?xml version=\"1.0\"?><!-- <x><y><z> --><a><![CDATA[<q><r>]]></a>", 1)]
    [InlineData("<a title=\"x > y\"><b title='<c>'/></a>", 1)]
    [InlineData("</stray></stray><a><b></b></a>", 2)]
    [InlineData("<p>one<p>two<p>three", 3)]
    [InlineData("a < b and no tags", 0)]
    public void Markup_depth_counts_nesting_from_the_raw_text(string markup, int depth)
    {
        Assert.Equal(depth, MarkupDepth.Of(markup));
    }

    [Fact]
    public void A_processing_instruction_containing_a_fake_closing_tag_does_not_hide_real_nesting()
    {
        // The old ">"-only search for a PI's end stopped at the accidental ">" right after "p ",
        // leaving "</z>" to be read as a real closing tag and cancel out a real <div> open.
        Assert.Equal(2, MarkupDepth.Of("<div><?p ></z>?><div><?p ></z>?>"));
    }

    [Fact]
    public void An_element_name_outside_ascii_letters_still_counts_as_nesting()
    {
        // U+2160 (ROMAN NUMERAL ONE) is not char.IsLetter, but AngleSharp.Xml accepts it as a tag
        // name — the counter must not silently skip past a tag it fails to recognise as one.
        Assert.Equal(2, MarkupDepth.Of("<Ⅰ><Ⅰ></Ⅰ></Ⅰ>"));
    }
}
