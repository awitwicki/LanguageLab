using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// A book the user reads in the web reader. The file itself never reaches the server — it stays
/// in the browser that opened it. This row is only what another device needs to continue: the
/// file's hash (to recognise the same file), its title, and where the reader stopped.
/// </summary>
public class ReaderBook : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    /// <summary>See ReaderHash.</summary>
    [MaxLength(ReaderHash.Length)]
    public string FileHash { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Empty when the fb2 names no author.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>For "Chapter 7 of 30" on a device that does not have the file.</summary>
    public int ChaptersCount { get; set; }

    /// <summary>0-based, in the reader's chapter list (not the import's).</summary>
    public int ChapterIndex { get; set; }

    public int ParagraphIndex { get; set; }

    public int SentenceIndex { get; set; }

    /// <summary>0..1 over the whole book, counted in sentences.</summary>
    public double Progress { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>UTC. When the position last moved, by the client's clock (capped at the server's now): the later device wins.</summary>
    public DateTime UpdatedAt { get; set; }
}
