namespace LanguageLab.Domain.Training;

/// <summary>The grading result of one word at the end of a session.</summary>
public readonly record struct LeitnerOutcome(int Box, DateTime? DueAt, bool IsLearned);
