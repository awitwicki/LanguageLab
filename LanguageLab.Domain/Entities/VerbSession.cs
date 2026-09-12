using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// One training session of the irregular-verbs trainer. The task queue is generated up
/// front and stored (VerbTask), so a page reload resumes where the learner was. Group and
/// Family are set for Learn sessions only.
/// </summary>
public class VerbSession : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public SessionMode Mode { get; set; }

    public int? Group { get; set; }

    public string? Family { get; set; }

    /// <summary>UTC.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC. Null while the session is open; set by finish or when a new session replaces it.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>Tasks answered by the time the session was finished.</summary>
    public int Total { get; set; }

    public int Correct { get; set; }

    public IList<VerbTask> Tasks { get; set; } = new List<VerbTask>();
}
