namespace LanguageLab.Domain.Interfaces;

public interface IBotLogger
{
     public Task LogCreatedDictionary(string dictionaryName, int wordsCount);
     public Task LogException(Exception exception);
}
