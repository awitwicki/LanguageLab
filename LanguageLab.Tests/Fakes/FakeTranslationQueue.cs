using LanguageLab.Application.Translation;
using LanguageLab.Domain.Languages;

namespace LanguageLab.Tests.Fakes;

/// <summary>Records every enqueue, duplicates included — deduplication is the real queue's job.</summary>
public sealed class FakeTranslationQueue : ITranslationQueue
{
    public List<(long DictionaryId, string Language)> Enqueued { get; } = [];

    public Task EnqueueAsync(long dictionaryId, LearnerLanguage language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Enqueued.Add((dictionaryId, language.Code));
        return Task.CompletedTask;
    }
}
