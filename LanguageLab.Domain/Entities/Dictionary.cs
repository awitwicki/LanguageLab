namespace LanguageLab.Domain.Entities;

public class Dictionary : BaseEntity
{
    public string Name { get; set; }
    
    public IList<WordPair> Words { get; set; }
}
