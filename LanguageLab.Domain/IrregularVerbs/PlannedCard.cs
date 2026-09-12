namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>One verb of a session and which of its forms is shown open; the other two are the closed cards.</summary>
public sealed record PlannedCard(IrregularVerb Verb, VerbForm Open);

/// <summary>IsReview when the step had nothing left to learn, so the cards are already-learned verbs.</summary>
public sealed record SessionPlan(bool IsReview, IReadOnlyList<PlannedCard> Cards);
