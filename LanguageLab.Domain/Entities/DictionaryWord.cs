namespace LanguageLab.Domain.Entities;

/// <summary>
/// Join «словник × слово» з навантаженням. Таблиця називається так само, як
/// раніше створювала конвенція (DictionaryWords), тому міграція лише додає
/// колонку, а наявні рядки нікуди не переїжджають.
/// </summary>
public class DictionaryWord
{
    public Dictionary Dictionary { get; set; } = null!;
    public long DictionaryId { get; set; }

    public WordPair WordPair { get; set; } = null!;
    public long WordPairId { get; set; }

    /// <summary>
    /// Сума Count по всіх главах. Зберігається, а не рахується запитом:
    /// у пласких імпортів глав немає, і без цього поля черга сортування
    /// мала б дві різні гілки замість однієї.
    /// 0 означає «частота невідома» — так виглядають словники, залиті через бота.
    /// </summary>
    public int Frequency { get; set; }
}
