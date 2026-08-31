namespace LanguageLab.Domain.Training;

/// <summary>
/// Одне заплановане питання. OptionIds — це завжди id WordPair, незалежно від напрямку;
/// напрямок вирішує лише, який бік пари показувати в тілі питання, а який на кнопках.
/// </summary>
public sealed record PlannedQuestion(
    long WordPairId,
    QuestionDirection Direction,
    IReadOnlyList<long> OptionIds);
