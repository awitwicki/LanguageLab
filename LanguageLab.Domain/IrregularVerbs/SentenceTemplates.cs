namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// Universal sentence frames the drill reuses across verbs, so a learner cannot pass by
/// remembering one sentence per verb. The cue is the time marker, not the content; the frames
/// carry no object, so some verbs read a little bare in them, which the drill accepts. Present
/// frames use I / you / we / they only, so V1 never needs -s.
/// </summary>
public static class SentenceTemplates
{
    public const string Blank = "___";

    private static readonly IReadOnlyList<string> Present =
    [
        "Every morning we ___.",
        "I usually ___ after work.",
        "They often ___ on Sundays.",
        "We ___ every day.",
        "You always ___ too early.",
        "I ___ every week.",
        "They never ___ on Mondays.",
        "We sometimes ___ together.",
        "I often ___ in the evening.",
        "You ___ every weekend.",
    ];

    private static readonly IReadOnlyList<string> Past =
    [
        "Yesterday she ___.",
        "Last week we ___.",
        "Two days ago he ___.",
        "Last night I ___.",
        "They ___ an hour ago.",
        "She ___ last Monday.",
        "We ___ in 2020.",
        "He ___ this morning.",
        "Last summer they ___.",
        "I ___ a minute ago.",
    ];

    private static readonly IReadOnlyList<string> Perfect =
    [
        "They have already ___.",
        "She has just ___.",
        "I have never ___.",
        "We have ___ twice this week.",
        "He has ___ many times.",
        "Have you ever ___?",
        "You have already ___.",
        "I have just ___.",
        "She has never ___.",
        "They have ___ before.",
    ];

    public static IReadOnlyList<string> Of(Tense tense) => tense switch
    {
        Tense.Present => Present,
        Tense.Past => Past,
        _ => Perfect,
    };

    /// <summary><paramref name="count"/> distinct frames of the tense, in random order.</summary>
    public static IReadOnlyList<string> Pick(Tense tense, int count, Random dice) =>
        Of(tense).OrderBy(_ => dice.Next()).Take(count).ToList();
}
