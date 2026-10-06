namespace LanguageLab.Application.Books;

/// <summary>
/// How deeply elements nest, and how deep the parsers may go. AngleSharp's XML parser, its element
/// enumeration, <c>XElement.Value</c> and this folder's own tree walks all recurse per level, so a
/// few thousand nested tags overflow the stack — which no <c>catch</c> survives: it ends the
/// process. Everything that builds or walks a tree checks the depth against <see cref="Limit"/>
/// first; no real book comes near it.
/// </summary>
internal static class MarkupDepth
{
    public const int Limit = 256;

    /// <summary>
    /// How many characters <see cref="TagEnd"/> may read in total, per character of markup. A real
    /// document reads each character about once (every found tag moves the scan past its '&gt;');
    /// only '&lt;'s with no '&gt;' after them reread the tail, and a crafted run of them made the
    /// scan quadratic. Past the budget the markup is reported as too deep — the safe direction:
    /// it skips the XML parser and goes to the HTML parser, which the caller's deadline bounds.
    /// </summary>
    private const int ScanBudgetPerChar = 4;

    private const int BudgetSpent = -2;

    /// <summary>
    /// The deepest nesting in raw markup, without building a tree: +1 at a start tag that does not
    /// close itself, -1 at an end tag (never below 0); comments, CDATA, processing instructions and
    /// declarations are skipped, and a quoted attribute value may hold '&gt;'. This is a safety
    /// gate, not an XML-name validator: this counter must never come in <em>under</em> what a real
    /// parser (AngleSharp.Xml or the HTML5 algorithm) would build, or a crafted document slips past
    /// it into a parser that can overflow the stack. So any character that could start a tag name in
    /// some parser's dialect — anything but whitespace — counts as one, and a tag with no matching
    /// '&gt;' is skipped as one stray character rather than aborting the rest of the scan, which
    /// would let real nesting after it go uncounted. Exact for well-formed XML; HTML with unclosed
    /// tags counts deeper than it parses, which only sends it to the HTML parser — where a document
    /// that is not XML goes anyway. Bounded to linear time: see <see cref="ScanBudgetPerChar"/>.
    /// </summary>
    public static int Of(string markup)
    {
        int depth = 0, max = 0, i = 0;
        var budget = (long)markup.Length * ScanBudgetPerChar + 4096;

        while (i < markup.Length && (i = markup.IndexOf('<', i)) >= 0)
        {
            if (At(markup, i, "<!--"))
            {
                i = After(markup, i + 4, "-->");
            }
            else if (At(markup, i, "<![CDATA["))
            {
                i = After(markup, i + 9, "]]>");
            }
            else if (At(markup, i, "<?"))
            {
                i = After(markup, i + 2, "?>");
            }
            else if (At(markup, i, "<!"))
            {
                i = After(markup, i + 2, ">");
            }
            else if (At(markup, i, "</"))
            {
                depth = Math.Max(0, depth - 1);
                i = After(markup, i + 2, ">");
            }
            else if (i + 1 < markup.Length && !char.IsWhiteSpace(markup[i + 1]))
            {
                var end = TagEnd(markup, i + 1, ref budget);

                if (end == BudgetSpent)
                {
                    return Limit + 1;
                }

                if (end < 0)
                {
                    // No closing '>' anywhere ahead: not a tag after all. Skip past just the '<' —
                    // aborting the whole scan here would hide any real nesting further in the text.
                    i++;
                    continue;
                }

                if (markup[end - 1] != '/')
                {
                    depth++;
                    max = Math.Max(max, depth);
                }

                i = end + 1;
            }
            else
            {
                i++;
            }
        }

        return max;
    }

    private static bool At(string text, int index, string token) =>
        string.CompareOrdinal(text, index, token, 0, token.Length) == 0;

    /// <summary>The index just past the next <paramref name="terminator"/>, or the end of the text.</summary>
    private static int After(string text, int from, string terminator)
    {
        var at = text.IndexOf(terminator, from, StringComparison.Ordinal);

        return at < 0 ? text.Length : at + terminator.Length;
    }

    /// <summary>The index of the tag's closing '&gt;', -1 when there is none, <see cref="BudgetSpent"/> past the budget.</summary>
    private static int TagEnd(string text, int from, ref long budget)
    {
        var quote = '\0';

        for (var i = from; i < text.Length; i++)
        {
            if (--budget < 0)
            {
                return BudgetSpent;
            }

            var c = text[i];

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }

        return -1;
    }
}
