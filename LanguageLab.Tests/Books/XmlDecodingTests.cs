using System.Text;
using LanguageLab.Application.Books;

namespace LanguageLab.Tests.Books;

/// <summary>Ported from web/src/books/decode.test.ts.</summary>
public class XmlDecodingTests
{
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [Fact]
    public void Reads_utf8_when_the_prolog_says_so()
    {
        var text = """<?xml version="1.0" encoding="utf-8"?><FictionBook>тест</FictionBook>""";

        Assert.Contains("тест", XmlDecoding.Decode(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void Reads_windows_1251_declared_in_the_prolog()
    {
        // Ц is 0xD6 in cp1251, while in utf-8 the same byte would yield the U+FFFD replacement.
        byte[] bytes = [.. Ascii("<?xml version='1.0' encoding='windows-1251'?><b>"), 0xd6, .. Ascii("</b>")];

        Assert.Contains("Ц", XmlDecoding.Decode(bytes));
    }

    [Fact]
    public void Falls_back_to_utf8_when_the_declared_encoding_is_unknown()
    {
        var text = """<?xml version="1.0" encoding="totally-made-up"?><FictionBook>ok</FictionBook>""";

        Assert.Contains("ok", XmlDecoding.Decode(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void Defaults_to_utf8_when_there_is_no_prolog()
    {
        Assert.Contains("ok", XmlDecoding.Decode(Encoding.UTF8.GetBytes("<FictionBook>ok</FictionBook>")));
    }

    [Fact]
    public void Decodes_a_bare_entry_exactly()
    {
        Assert.Equal("<p>ok</p>", XmlDecoding.Decode(Encoding.UTF8.GetBytes("<p>ok</p>")));
    }

    [Fact]
    public void Honours_a_charset_declared_by_an_xhtml_meta_element()
    {
        byte[] bytes = [.. Ascii("""<html><head><meta charset="windows-1251"/></head><body><p>"""), 0xd6, .. Ascii("</p></body></html>")];

        Assert.Contains("Ц", XmlDecoding.Decode(bytes));
    }

    [Fact]
    public void Drops_a_utf8_byte_order_mark()
    {
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("<p>ok</p>")];

        Assert.Equal("<p>ok</p>", XmlDecoding.Decode(bytes));
    }

    [Fact]
    public void Reads_an_empty_file_as_empty_text()
    {
        Assert.Equal("", XmlDecoding.Decode([]));
    }
}
