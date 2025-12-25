using LanguageLab.Domain.Interfaces;

namespace LanguageLab.TgBot.Services;

public class BotLogger : IBotLogger
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public Task LogCreatedDictionary(string dictionaryName, int wordsCount)
    {
        throw new NotImplementedException();
    }

    public Task LogException(Exception exception)
    {
        Logger.Error(exception, exception.Message);
        
        return Task.CompletedTask;
    }
}
