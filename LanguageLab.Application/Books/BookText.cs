using System.Text.RegularExpressions;

namespace LanguageLab.Application.Books;

/// <summary>
/// JavaScript's whitespace rules, so the port trims and collapses what the SPA's <c>/\s+/g</c> and
/// <c>String.prototype.trim</c> do: those also treat U+FEFF as whitespace, which .NET does not.
/// </summary>
internal static class BookText
{
    private const char ByteOrderMark = (char)0xFEFF;

    private static readonly Regex Whitespace = new($@"[\s{ByteOrderMark}]+", RegexOptions.CultureInvariant);

    /// <summary>Every whitespace run becomes one space, then the ends are trimmed.</summary>
    public static string Collapse(string text) => Trim(Whitespace.Replace(text, " "));

    public static string Trim(string text)
    {
        var start = 0;
        var end = text.Length;

        while (start < end && IsSpace(text[start]))
        {
            start++;
        }

        while (end > start && IsSpace(text[end - 1]))
        {
            end--;
        }

        return text[start..end];
    }

    private static bool IsSpace(char c) => char.IsWhiteSpace(c) || c == ByteOrderMark;
}
