using LanguageLab.Application.Translation;
using LanguageLab.Domain.Languages;

namespace LanguageLab.Tests.Fakes;

/// <summary>
/// Translates from a fixed table, answering only for the lemmas asked for — as the real one
/// promises. A queued entry in <see cref="Failures"/> is thrown before the table is consulted.
/// </summary>
public sealed class FakeWordBatchTranslator : IWordBatchTranslator
{
    private readonly IReadOnlyDictionary<string, string> _table;

    public FakeWordBatchTranslator(IReadOnlyDictionary<string, string> table) => _table = table;

    public bool IsConfigured { get; set; } = true;
    public Queue<LlmException> Failures { get; } = new();
    public List<IReadOnlyList<string>> Batches { get; } = [];
    public List<LearnerLanguage> Targets { get; } = [];

    /// <summary>
    /// Runs after the batch is recorded and any queued failure thrown, before the answer — a test
    /// uses it to change the database "while the provider is thinking", or to throw.
    /// </summary>
    public Func<IReadOnlyList<string>, Task>? BeforeAnswer { get; set; }

    public async Task<IReadOnlyDictionary<string, string>> TranslateAsync(
        IReadOnlyList<string> lemmas, LearnerLanguage target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConfigured)
            throw new LlmUnavailableException("Not configured.");

        Batches.Add(lemmas);
        Targets.Add(target);
        if (Failures.TryDequeue(out var failure))
            throw failure;

        if (BeforeAnswer != null)
            await BeforeAnswer(lemmas);

        return lemmas
            .Distinct()
            .Where(_table.ContainsKey)
            .ToDictionary(lemma => lemma, lemma => _table[lemma]);
    }
}
