namespace LanguageLab.Domain.Entities;

public class WordPair : BaseEntity
{
    public required string Word { get; set; }
    public required string Translation { get; set; }
}
