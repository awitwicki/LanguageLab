namespace LanguageLab.Domain.Entities;

/// <summary>
/// Глобальна пара «слово — переклад». Не належить конкретному словнику:
/// одне слово може бути в кількох словниках, і саме це дозволяє позначкам
/// «знаю» / «хочу вчити» переживати імпорт наступного словника.
/// Translation може бути порожнім у слів, імпортованих без перекладу.
/// </summary>
public class WordPair : BaseEntity
{
    public required string Word { get; set; }
    public required string Translation { get; set; }

    public IList<Dictionary> Dictionaries { get; set; } = new List<Dictionary>();
}
