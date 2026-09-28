namespace LanguageLab.Application.Books;

/// <summary>
/// A book as the server's import sees it: the book's own title and author, and its section tree
/// in reading order. <see cref="BookChapters.Flatten"/> turns the tree into chapters for a chosen
/// <see cref="ChapterMode"/> — the same rule as the import screen's preview
/// (web/src/fb2/chapters.ts), so the server sees the chapters the uploader picked.
/// <see cref="Title"/> is "" when the book names none — the caller falls back to the file name.
/// <see cref="MaxDepth"/> is the deepest section's depth, 0 for a book with none.
/// </summary>
public sealed record ParsedBook(string Title, string? Author, IReadOnlyList<BookSection> Sections, int MaxDepth);

/// <summary>
/// One section of the tree. <see cref="Depth"/> is 1-based. <see cref="OwnText"/> is this
/// section's own text, without its title and without nested sections.
/// </summary>
public sealed record BookSection(string Title, int Depth, string OwnText, IReadOnlyList<BookSection> Children);

/// <summary>One chapter; <see cref="Order"/> is its 0-based position in <see cref="BookChapters.Flatten"/>'s result.</summary>
public sealed record ParsedChapter(int Order, string Title, string Text);
