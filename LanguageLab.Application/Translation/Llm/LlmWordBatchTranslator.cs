using System.Text.Json;
using LanguageLab.Domain.Languages;

namespace LanguageLab.Application.Translation.Llm;

/// <summary>
/// One call to <see cref="ILlmClient"/> per <see cref="TranslateAsync"/>, translating the given
/// lemmas into <c>target</c> and keeping only the lemmas asked for — a key the model added on its
/// own is never read, and a key that differs from a requested lemma only in case is matched to it
/// (the prompt asks for exact casing, but nothing enforces that a model complies).
/// </summary>
public sealed class LlmWordBatchTranslator : IWordBatchTranslator
{
    private const int MaxOutputTokens = 8192;

    private static readonly JsonElement ResponseSchema =
        JsonDocument.Parse("""{"type":"object","additionalProperties":{"type":"string"}}""").RootElement.Clone();

    private readonly ILlmClient _client;

    public LlmWordBatchTranslator(ILlmClient client) => _client = client;

    public bool IsConfigured => _client.IsConfigured;

    public async Task<IReadOnlyDictionary<string, string>> TranslateAsync(
        IReadOnlyList<string> lemmas, LearnerLanguage target, CancellationToken cancellationToken)
    {
        var distinct = lemmas.Distinct().ToList();

        if (distinct.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        // Maps any casing the model echoes back to the lemma as it was requested; a case-variant
        // duplicate in the input (unexpected — callers pass already-normalized lemmas) keeps
        // whichever copy arrived first rather than throwing.
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lemma in distinct)
        {
            wanted.TryAdd(lemma, lemma);
        }

        var request = new LlmRequest(
            SystemInstruction(target), JsonSerializer.Serialize(distinct), ResponseSchema, MaxOutputTokens);
        var answer = await _client.CompleteJsonAsync(request, cancellationToken);

        var result = new Dictionary<string, string>();

        foreach (var property in answer.EnumerateObject())
        {
            if (!wanted.TryGetValue(property.Name, out var lemma) || property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = property.Value.GetString()!.Trim();

            // Not compared against the lemma: several supported languages legitimately spell
            // some words the same as English (German capitalises nouns — hand → Hand; Polish,
            // Spanish, Italian and others keep problem/idea/hospital unchanged). The model is
            // separately told to omit the key when it has nothing to say, so an echo here is a
            // real answer, not the "no translation" signal it was for MyMemory.
            if (text.Length > 0)
            {
                result[lemma] = text;
            }
        }

        return result;
    }

    private static string SystemInstruction(LearnerLanguage target) => $"""
        You are a translation engine for an English-vocabulary learning app. You will receive a
        JSON array of English words or lemmas. Translate each one into {target.EnglishName}.
        Respond with a single JSON object whose keys are exactly the input words, unchanged, and
        whose values are each word's translation as a short word or phrase in {target.EnglishName}.
        If a word has no good translation — a proper noun, an acronym, gibberish — omit its key.
        Do not add keys for words that were not in the input. Respond with JSON only, no other text.
        """;
}
