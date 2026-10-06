namespace LanguageLab.Domain.Grammar;

/// <summary>
/// A CEFR level, in ascending order, so a later goal filter can compare with <c>&lt;=</c>. C1 and C2
/// join once content for them is planned (docs/grammar-roadmap.md).
/// </summary>
public enum GrammarLevel
{
    A1,
    A2,
    B1,
    B2,
}
