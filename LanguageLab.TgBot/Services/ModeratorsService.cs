using LanguageLab.Domain.Interfaces;

namespace LanguageLab.TgBot.Services;

public class ModeratorsService : IModeratorsService
{
    private readonly HashSet<long> _usersList;
        
    public ModeratorsService(string input)
    {
        _usersList = new HashSet<long>();
        
        if (!string.IsNullOrEmpty(input))
        {
            var chatIdList = input.Split(",");
            foreach (var chatIdStr in chatIdList)
            {
                var chatId = long.Parse(chatIdStr);
                _usersList.Add(chatId);
            }
        }
    }

    public bool IsUserModerator(long userTelegramId)
    {
        return _usersList.Count == 0 || _usersList.Contains(userTelegramId);
    }
}
