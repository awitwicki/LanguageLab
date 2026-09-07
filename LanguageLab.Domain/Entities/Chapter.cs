using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// Глава книжки. Існує лише у словників, залитих із fb2: пласкі імпорти
/// (на кшталт «топ-500 англійських слів») глав не мають узагалі, і саме
/// відсутність глав відрізняє їх від книжки — окремого прапорця немає.
/// </summary>
public class Chapter : BaseEntity
{
    public Dictionary Dictionary { get; set; } = null!;
    [ForeignKey(nameof(Dictionary))]
    public long DictionaryId { get; set; }

    /// <summary>Позиція в книжці, 0-based.</summary>
    public int Order { get; set; }

    /// <summary>Порожній, якщо секція fb2 не мала &lt;title&gt;.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Унікальних базових форм у главі.</summary>
    public int WordsCount { get; set; }

    public IList<ChapterWord> Words { get; set; } = new List<ChapterWord>();
}
