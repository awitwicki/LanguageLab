namespace LanguageLab.Application.Translation.Llm;

/// <summary>
/// Which language model answers <see cref="ILlmClient"/> calls, and how to reach it. Bound from the
/// "Translation" section (<see cref="SectionName"/>, Translation__* in Docker) and validated at
/// startup by <see cref="LlmServiceCollectionExtensions.AddLlmClient"/>: a typo in
/// <see cref="Provider"/> stops the app rather than the first translation.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Translation";

    public LlmProvider Provider { get; set; } = LlmProvider.Gemini;
    public GeminiOptions Gemini { get; set; } = new();
    public OpenAiCompatibleOptions OpenAi { get; set; } = new();
}

public enum LlmProvider
{
    Gemini,
    OpenAiCompatible,
}

/// <summary>Translation:Gemini. Without <see cref="ApiKey"/> the Gemini client is unconfigured.</summary>
public sealed class GeminiOptions
{
    public string? ApiKey { get; set; }

    /// <summary>GA Flash-Lite, chosen over the cheaper gemini-3.1-flash-lite for translation quality.</summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";
}

/// <summary>
/// Translation:OpenAi — any OpenAI-compatible chat-completions endpoint; DeepSeek by default.
/// Without <see cref="ApiKey"/> the client is unconfigured.
/// </summary>
public sealed class OpenAiCompatibleOptions
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com/";
    public string? ApiKey { get; set; }

    /// <summary>deepseek-chat was retired on 2026-07-24.</summary>
    public string Model { get; set; } = "deepseek-flash";
}
