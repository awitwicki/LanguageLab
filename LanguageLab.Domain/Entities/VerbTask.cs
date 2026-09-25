using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// One task in a session's queue. Exists before the learner answers: Outcome == null
/// means pending. Payload holds the exercise-specific data, answers included, as JSON.
/// </summary>
public class VerbTask : BaseEntity
{
    public VerbSession Session { get; set; } = null!;
    [ForeignKey(nameof(Session))]
    public long SessionId { get; set; }

    /// <summary>Position in the queue, 0-based; returns are inserted and the rest renumbered.</summary>
    public int Order { get; set; }

    public ExerciseType Type { get; set; }

    public required string Verb { get; set; }

    public FormAsked FormAsked { get; set; }

    /// <summary>1 = card, 2 = recognition, 3 = production.</summary>
    public int Level { get; set; }

    [Column(TypeName = "jsonb")]
    public string Payload { get; set; } = "{}";

    /// <summary>Inserted after a mistake on the same verb earlier in the session.</summary>
    public bool IsReturn { get; set; }

    public AttemptOutcome? Outcome { get; set; }

    public string? AnswerGiven { get; set; }

    public ErrorKind? ErrorKind { get; set; }

    /// <summary>Near-miss spellings so far on this task; the second one counts as a mistake.</summary>
    public int NeutralCount { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? AnsweredAt { get; set; }

    public TaskPayload GetPayload() => TaskPayload.Deserialize(Payload);

    public void SetPayload(TaskPayload payload) => Payload = payload.Serialize();
}
