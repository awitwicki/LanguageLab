using System.Text.Json;

namespace LanguageLab.Application.Translation;

/// <summary>
/// One call to a language model that must answer with JSON. <see cref="SystemInstruction"/> is
/// ours and fixed per use; <see cref="UserContent"/> is the data to work on — anything that came
/// from a user goes here, never into the instruction. <see cref="ResponseSchema"/> is a JSON
/// Schema object the answer must satisfy; a provider that cannot enforce it natively is expected
/// to validate the answer against it before returning.
/// </summary>
public sealed record LlmRequest(
    string SystemInstruction,
    string UserContent,
    JsonElement ResponseSchema,
    int MaxOutputTokens);
