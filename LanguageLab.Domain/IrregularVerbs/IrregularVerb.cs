namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>One of a verb's three forms: the open card of a session, and the form a progress row grades.</summary>
public enum VerbForm
{
    V1 = 0,
    V2 = 1,
    V3 = 2,
}

/// <summary>
/// One irregular verb: the three forms and the Ukrainian translation. Step is
/// the 1-based pattern type from the learner's table (1 — all forms alike … 4 — all differ).
/// V1 is the stable key for the verb everywhere outside this table (progress rows,
/// the API) — verbs are never their own database row.
/// </summary>
public sealed record IrregularVerb(int Step, string V1, string V2, string V3, string Translation);
