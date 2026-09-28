using System.Text.Json;

namespace LanguageLab.Application.Translation;

/// <summary>
/// One call to a language model that must answer with JSON. <see cref="SystemInstruction"/> is
/// ours and fixed per use; <see cref="UserContent"/> is the data to work on — anything that came
/// from a user goes here, never into the instruction. <see cref="ResponseSchema"/> is a JSON
/// Schema object describing the answer. The client guarantees only that the answer is a JSON
/// object; the consumer checks the fields it reads. A provider with native schema support (Gemini)
/// enforces the schema; one without (an OpenAI-compatible endpoint) is shown it in its prompt.
/// </summary>
public sealed record LlmRequest(
    string SystemInstruction,
    string UserContent,
    JsonElement ResponseSchema,
    int MaxOutputTokens);
