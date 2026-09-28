using System.Xml;

namespace LanguageLab.Application.Books;

/// <summary>
/// fb2 or epub bytes → <see cref="ParsedBook"/>, deciding the format from the bytes alone — a port
/// of readBookSource in web/src/books/format.ts plus toParsedBook.ts; change both together. Anything
/// unreadable throws <see cref="BookFormatException"/>: a library's own exception is wrapped as
/// <see cref="BookFormatError.Invalid"/>, and so is a book that takes longer than the time limit.
/// </summary>
public sealed class BookParser : IBookParser
{
    /// <summary>A real epub parses in a second or two; this bounds a crafted one.</summary>
    public static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The two algorithms the epub spec and Adobe's own extension to it use to obfuscate embedded
    /// font files — not the book's text. Any other algorithm, or an encryption.xml that cannot be
    /// read at all, is treated as real DRM: better a false refusal than garbage text.
    /// </summary>
    private static readonly HashSet<string> FontObfuscationAlgorithms =
    [
        "http://www.idpf.org/2008/embedding",
        "http://ns.adobe.com/pdf/enc#RC",
    ];

    private readonly TimeSpan _timeLimit;

    public BookParser()
        : this(DefaultTimeLimit)
    {
    }

    internal BookParser(TimeSpan timeLimit) => _timeLimit = timeLimit;

    public ParsedBook Parse(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var deadline = new CancellationTokenSource(_timeLimit);

        try
        {
            return Read(file, deadline.Token);
        }
        catch (OperationCanceledException e)
        {
            throw new BookFormatException(BookFormatError.Invalid, "book parsing time limit exceeded", e);
        }
        catch (Exception e) when (e is not BookFormatException and not OutOfMemoryException)
        {
            throw new BookFormatException(BookFormatError.Invalid, "not a readable fb2 or epub", e);
        }
    }

    private static ParsedBook Read(byte[] file, CancellationToken cancellationToken)
    {
        if (!SafeZip.LooksZipped(file))
        {
            return Fb2Parser.Parse(XmlDecoding.Decode(file));
        }

        var entries = SafeZip.Read(file);

        // Adobe's and everyone else's DRM: the text is there but encrypted, so say so rather than
        // report an unreadable book. An encryption.xml that only obfuscates fonts is let through.
        if (entries.TryGetValue("META-INF/encryption.xml", out var encryption)
            && !IsFontObfuscationOnly(XmlDecoding.Decode(encryption)))
        {
            throw new BookFormatException(BookFormatError.Encrypted, "epub is DRM-protected");
        }

        if (entries.ContainsKey("META-INF/container.xml") || MimetypeOf(entries) == "application/epub+zip")
        {
            return EpubParser.ToParsedBook(EpubParser.Parse(entries, cancellationToken));
        }

        var fb2 = entries.Keys.Where(path => path.EndsWith(".fb2", StringComparison.OrdinalIgnoreCase)).ToList();

        // Exactly one: with several there is no telling which book the uploader meant.
        if (fb2.Count == 1)
        {
            return Fb2Parser.Parse(XmlDecoding.Decode(entries[fb2[0]]));
        }

        throw new BookFormatException(BookFormatError.Invalid, "zip holds no single fb2 or epub");
    }

    private static string MimetypeOf(IReadOnlyDictionary<string, byte[]> entries) =>
        entries.TryGetValue("mimetype", out var bytes) ? BookText.Trim(System.Text.Encoding.ASCII.GetString(bytes)) : "";

    /// <summary>True only when every EncryptionMethod in the file targets font obfuscation, never the text.</summary>
    private static bool IsFontObfuscationOnly(string xml)
    {
        try
        {
            var methods = SafeXml.Load(xml).Descendants()
                .Where(element => element.Name.LocalName.Equals("encryptionmethod", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return methods.Count > 0
                && methods.All(method => FontObfuscationAlgorithms.Contains((string?)method.Attribute("Algorithm") ?? ""));
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
