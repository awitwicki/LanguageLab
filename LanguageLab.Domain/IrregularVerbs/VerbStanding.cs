namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// Where one verb stands for one learner, as the selection rules need it — flattened out
/// of the <c>VerbKnowledge</c> row so that every rule stays pure and testable without a
/// database. A verb never answered has <see cref="Answers"/> of 0.
/// </summary>
public sealed record VerbStanding(string Verb, double Mastery, int Streak, int Answers);
