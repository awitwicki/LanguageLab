using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.Languages;
using LanguageLab.Domain.Training;

namespace LanguageLab.Domain.Entities;

public class Training : BaseEntity
{
    public DateTime CreatedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public TrainingMode Mode { get; set; }

    /// <summary>
    /// The language the session was built in. Questions, options, cards and the summary are
    /// rendered in it, so switching languages mid-session never shows an empty button.
    /// </summary>
    public string Language { get; set; } = LearnerLanguages.DefaultCode;

    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    /// <summary>null in review mode — it pulls words from every dictionary at once.</summary>
    public Dictionary? Dictionary { get; set; }
    [ForeignKey(nameof(Dictionary))]
    public long? DictionaryId { get; set; }

    /// <summary>
    /// The chapter the session was started in, or null for the whole book — the scope a
    /// "Repeat" on the home screen reopens. A scope is only ever one chapter or the whole
    /// book, so one column covers it; a session started over several chapters at once stores
    /// null and reads back as the book's.
    /// </summary>
    public Chapter? Chapter { get; set; }
    [ForeignKey(nameof(Chapter))]
    public long? ChapterId { get; set; }

    public IList<TrainingQuestion> Questions { get; set; } = new List<TrainingQuestion>();
}
