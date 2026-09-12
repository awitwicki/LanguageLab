using LanguageLab.Domain.Entities;

namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>One form's standing inside a VerbProgress; unseen with zero counters when there is no row.</summary>
public sealed record FormProgress(VerbForm Form, FormState State, int Streak, int Correct, int Wrong);

/// <summary>
/// A user's standing on one verb, folded from its up-to-three progress rows. The planner
/// and the overview both read verbs through this, so "learned", "has a miss" and the
/// open-form choice are decided in one place.
/// </summary>
public sealed record VerbProgress(IrregularVerb Verb, IReadOnlyList<FormProgress> Forms, DateTime? LastAnsweredAt)
{
    public static readonly IReadOnlyList<VerbForm> AllForms = [VerbForm.V1, VerbForm.V2, VerbForm.V3];

    public int LearnedForms => Forms.Count(f => f.State == FormState.Learned);

    public bool IsLearned => LearnedForms == Forms.Count;

    public bool HasMiss => Forms.Any(f => f.State == FormState.Missed);

    public bool HasRows => Forms.Any(f => f.State != FormState.Unseen);

    public int StreakSum => Forms.Sum(f => f.Streak);

    /// <summary>
    /// The card to show open: the form with the highest streak, the lowest form on a tie —
    /// so a new verb opens V1 and, once V2 and V3 are learned, V1 itself gets asked.
    /// </summary>
    public VerbForm OpenForm => Forms.OrderByDescending(f => f.Streak).ThenBy(f => f.Form).First().Form;

    /// <summary>Rows may be any subset of the verb's forms; rows of other verbs are ignored.</summary>
    public static VerbProgress Of(IrregularVerb verb, IEnumerable<IrregularVerbFormProgress> rows)
    {
        var own = rows.Where(r => r.Verb == verb.V1).ToDictionary(r => r.Form);

        var forms = AllForms
            .Select(form => own.TryGetValue(form, out var row)
                ? new FormProgress(form, FormStates.Of(row), row.Streak, row.Correct, row.Wrong)
                : new FormProgress(form, FormState.Unseen, 0, 0, 0))
            .ToList();

        DateTime? last = own.Count == 0 ? null : own.Values.Max(r => r.LastAnsweredAt);

        return new VerbProgress(verb, forms, last);
    }
}
