namespace LanguageLab.Application.Books;

/// <summary>Why a file could not be read as a book — mirrors web/src/books/formatError.ts.</summary>
public enum BookFormatError
{
    /// <summary>Not an fb2 or epub, or one too broken to read.</summary>
    Invalid,
    /// <summary>An epub locked by DRM.</summary>
    Encrypted,
}

/// <summary>The file is the uploader's book: <see cref="Exception.Message"/> must not embed raw file content.</summary>
public sealed class BookFormatException : Exception
{
    public BookFormatException(BookFormatError error, string message)
        : base(message) => Error = error;

    /// <summary>Wraps a library's exception; <paramref name="message"/> still names the problem only.</summary>
    public BookFormatException(BookFormatError error, string message, Exception? innerException)
        : base(message, innerException) => Error = error;

    public BookFormatError Error { get; }
}
