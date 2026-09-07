using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LanguageLab.Api;

/// <summary>
/// Хто зараз працює. Авторизації немає — юзер один і заданий у конфігу.
/// Інтерфейс існує саме для того, щоб поява акаунтів змінила одну реалізацію,
/// а не кожен ендпоінт.
/// </summary>
public interface ICurrentUser
{
    Task<long> GetIdAsync();
}

public class ConfigCurrentUser : ICurrentUser
{
    private readonly ApplicationDbContext _dbContext;
    private readonly long _telegramUserId;

    public ConfigCurrentUser(ApplicationDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;

        var raw = configuration["WebUser:TelegramId"];

        if (!long.TryParse(raw, out _telegramUserId) || _telegramUserId == 0)
        {
            throw new InvalidOperationException(
                "WebUser:TelegramId не задано або воно не число. " +
                "Це telegram id того самого юзера, під яким ти працюєш у боті.");
        }
    }

    public async Task<long> GetIdAsync()
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.TelegramUserId == _telegramUserId);

        if (user != null)
        {
            return user.Id;
        }

        // Веб може стартувати раніше, ніж юзер напише боту.
        user = new TelegramUser { TelegramUserId = _telegramUserId };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return user.Id;
    }
}
