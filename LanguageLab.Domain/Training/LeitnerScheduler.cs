namespace LanguageLab.Domain.Training;

/// <summary>
/// Класичний Leitner на 5 боксів. Чистий: час подається ззовні, стану немає.
/// Оцінка застосовується раз на сесію по слову, на агрегаті всіх відповідей на нього.
/// </summary>
public static class LeitnerScheduler
{
    public const int MinBox = 1;
    public const int MaxBox = 5;

    /// <summary>Скільки днів чекати до наступного показу. Індекс = Box - 1.</summary>
    public static readonly IReadOnlyList<int> IntervalDays = Array.AsReadOnly(new[] { 1, 3, 7, 14, 30 });

    public static LeitnerOutcome Grade(int box, bool allCorrect, DateTime nowUtc)
    {
        if (box is < MinBox or > MaxBox)
        {
            throw new ArgumentOutOfRangeException(
                nameof(box), box, $"Box має бути в межах {MinBox}..{MaxBox}.");
        }

        if (!allCorrect)
        {
            var demoted = Math.Max(MinBox, box - 1);
            return new LeitnerOutcome(demoted, nowUtc.AddDays(1), IsLearned: false);
        }

        if (box == MaxBox)
        {
            return new LeitnerOutcome(MaxBox, DueAt: null, IsLearned: true);
        }

        var promoted = box + 1;
        return new LeitnerOutcome(promoted, nowUtc.AddDays(IntervalDays[promoted - 1]), IsLearned: false);
    }
}
