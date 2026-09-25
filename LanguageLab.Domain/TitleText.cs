namespace LanguageLab.Domain;

/// <summary>
/// The length a book title, an author and a dictionary name are stored at. The columns are
/// unbounded text, so this is the only cap there is; both the reader's library and the book
/// import cut to it rather than refuse, because an over-long title is still a usable book.
/// </summary>
public static class TitleText
{
    public const int MaxLength = 300;

    public static string Truncate(string value) => value.Length <= MaxLength ? value : value[..MaxLength];
}
