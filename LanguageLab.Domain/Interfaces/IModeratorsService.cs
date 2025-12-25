namespace LanguageLab.Domain.Interfaces;

public interface IModeratorsService
{
    public bool IsUserModerator(long userTelegramId);
}
