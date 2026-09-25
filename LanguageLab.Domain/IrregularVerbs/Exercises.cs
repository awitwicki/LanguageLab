using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>The exercise catalog of Phase 1; each id is logged with every attempt.</summary>
public enum ExerciseType
{
    Card,
    GapChoice,
    OddOne,
    Match,
    FormPick,
    GapType,
    TripleType,
}

/// <summary>Which form a task asks for. Recognition = no form is produced (Card, OddOne, FormPick).</summary>
public enum FormAsked
{
    V2,
    V3,
    Both,
    Recognition,
}

/// <summary>Why an answer was wrong, in the order the checker tests them.</summary>
public enum ErrorKind
{
    EdSuffix,
    V2ForV3,
    V3ForV2,
    WrongFamily,
    Spelling,
    Other,
}

/// <summary>Neutral = a near-miss spelling on a typed task: neither a success nor a failure.</summary>
public enum AttemptOutcome
{
    Correct,
    Wrong,
    Neutral,
}

public enum SessionMode
{
    Learn,
    ErrorsOnly,
    Mixed,
}

public sealed record MatchPair(string Left, string Right);

/// <summary>
/// Everything a task needs beyond its verb, answers included; stored as JSON on the
/// task row. One flat shape for all exercise types keeps serialisation trivial — each
/// type fills only its own fields, and the API maps out the answer fields.
/// </summary>
public sealed class TaskPayload
{
    public string? Sentence { get; set; }
    public Tense? Tense { get; set; }
    public List<string>? Options { get; set; }
    public string? Correct { get; set; }
    public List<MatchPair>? Pairs { get; set; }
    public List<string>? RightOrder { get; set; }
    public List<MatchPair>? Matched { get; set; }
    public FormAsked? Form { get; set; }
    public string? Hint { get; set; }
    public bool? AutofillV3 { get; set; }
    public List<string>? CorrectV2 { get; set; }
    public List<string>? CorrectV3 { get; set; }

    /// <summary>Match only: wrong pairs so far — the task counts as a mistake when it completes with any.</summary>
    public int? WrongPairs { get; set; }

    /// <summary>Match only: every left already answered, right or wrong — the task completes when this covers every pair.</summary>
    public List<string>? AttemptedLefts { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static TaskPayload Deserialize(string json) =>
        JsonSerializer.Deserialize<TaskPayload>(json, Json) ?? new TaskPayload();
}
