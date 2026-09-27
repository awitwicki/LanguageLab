namespace LanguageLab.Application.Books;

/// <summary>
/// A book as the server's import sees it: the book's own title and author, and its chapters in
/// reading order as plain text — markup gone, paragraphs separated by a newline. Leaf chapters
/// only; a chapter too long for one piece is already cut into parts.
/// </summary>
public sealed record ParsedBook(string Title, string? Author, IReadOnlyList<ParsedChapter> Chapters);

/// <summary>One chapter; <see cref="Order"/> is its 0-based position in <see cref="ParsedBook.Chapters"/>.</summary>
public sealed record ParsedChapter(int Order, string Title, string Text);
