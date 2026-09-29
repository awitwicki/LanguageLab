namespace LanguageLab.Domain.Entities;

/// <summary>
/// Where a TranslationJob's current pass stands. There is no "running": the worker takes one
/// batch at a time and holds nothing between batches, so a restart has nothing to recover.
/// </summary>
public enum TranslationJobStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
}
