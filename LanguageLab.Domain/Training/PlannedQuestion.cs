namespace LanguageLab.Domain.Training;

/// <summary>
/// One planned question. OptionIds are always WordPair ids regardless of direction; the
/// direction only decides which side of the pair goes in the question body and which on the buttons.
/// </summary>
public sealed record PlannedQuestion(
    long WordPairId,
    QuestionDirection Direction,
    IReadOnlyList<long> OptionIds);
