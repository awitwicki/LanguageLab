namespace LanguageLab.Application.Books;

/// <summary>
/// fb2 or epub bytes → <see cref="ParsedBook"/>. The bytes decide the format, never a file name —
/// the same rule as web/src/books/format.ts. Throws <see cref="BookFormatException"/> for anything
/// it cannot read. The file is the uploader's book: an implementation keeps nothing of it.
/// </summary>
public interface IBookParser
{
    ParsedBook Parse(byte[] file);
}
