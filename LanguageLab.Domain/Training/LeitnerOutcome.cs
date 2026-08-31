namespace LanguageLab.Domain.Training;

/// <summary>Результат оцінки одного слова за підсумком сесії.</summary>
public readonly record struct LeitnerOutcome(int Box, DateTime? DueAt, bool IsLearned);
