using System.Text;
using System.Text.RegularExpressions;

namespace LanguageLab.Application.Books;

/// <summary>
/// A port of decodeXml in web/src/books/decode.ts — change both together. fb2 files are very often
/// windows-1251, and an epub's XHTML documents may declare an encoding of their own, so the bytes
/// are decoded with the encoding the document declares in its first 256 bytes, else UTF-8.
/// </summary>
internal static class XmlDecoding
{
    private static readonly Regex Declared = new(
        @"(?:encoding|charset)\s*=\s*[""']?([\w-]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static XmlDecoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var head = Encoding.ASCII.GetString(bytes[..Math.Min(256, bytes.Length)]);
        var match = Declared.Match(head);
        var encoding = (match.Success ? Known(match.Groups[1].Value) : null) ?? Encoding.UTF8;
        var text = encoding.GetString(bytes);

        // TextDecoder drops a byte-order mark; Encoding.GetString keeps it as U+FEFF.
        return text.Length > 0 && text[0] == (char)0xFEFF ? text[1..] : text;
    }

    /// <summary>An unknown label reads as UTF-8 rather than failing, as in the SPA.</summary>
    private static Encoding? Known(string name)
    {
        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
