using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class DictionaryDeletionServiceTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task A_word_left_in_no_dictionary_goes_with_the_dictionary()
    {
        await using var db = NewContext();

        var dictionary = new Domain.Entities.Dictionary { Name = "Wool", WordsCount = 1 };
        dictionary.Words = [new WordPair { Word = "silo", Translation = "бункер" }];
        db.Dictionaries.Add(dictionary);
        await db.SaveChangesAsync();

        Assert.True(await new DictionaryDeletionService(db).DeleteAsync(dictionary.Id));

        Assert.False(await db.Words.AnyAsync(w => w.Word == "silo"));
        Assert.False(await db.Dictionaries.AnyAsync());
    }

    [Fact]
    public async Task A_word_another_dictionary_still_holds_stays()
    {
        await using var db = NewContext();

        var word = new WordPair { Word = "silo", Translation = "бункер" };
        var first = new Domain.Entities.Dictionary { Name = "Wool", WordsCount = 1, Words = [word] };
        var second = new Domain.Entities.Dictionary { Name = "Shift", WordsCount = 1, Words = [word] };
        db.Dictionaries.AddRange(first, second);
        await db.SaveChangesAsync();

        await new DictionaryDeletionService(db).DeleteAsync(first.Id);

        Assert.True(await db.Words.AnyAsync(w => w.Word == "silo"));
    }

    [Fact]
    public async Task A_word_somebody_is_still_learning_stays()
    {
        await using var db = NewContext();

        var word = new WordPair { Word = "silo", Translation = "бункер" };
        var dictionary = new Domain.Entities.Dictionary { Name = "Wool", WordsCount = 1, Words = [word] };
        db.Dictionaries.Add(dictionary);
        await db.SaveChangesAsync();

        db.UnknownWords.Add(new UnknownWord { UserId = 7, WordPairId = word.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await new DictionaryDeletionService(db).DeleteAsync(dictionary.Id);

        Assert.True(await db.Words.AnyAsync(w => w.Word == "silo"));
    }

    [Fact]
    public async Task A_personal_dictionary_is_never_deleted_by_id()
    {
        await using var db = NewContext();

        db.Dictionaries.Add(new Domain.Entities.Dictionary
        { Name = "My words", WordsCount = 0, OwnerId = 1, IsPersonal = true });
        await db.SaveChangesAsync();

        Assert.False(await new DictionaryDeletionService(db).DeleteAsync(1));
        Assert.True(await db.Dictionaries.AnyAsync());
    }

    [Fact]
    public async Task Deleting_a_users_dictionaries_leaves_their_personal_list_alone()
    {
        await using var db = NewContext();

        db.Dictionaries.Add(new Domain.Entities.Dictionary { Name = "Wool", WordsCount = 0, OwnerId = 5 });
        db.Dictionaries.Add(new Domain.Entities.Dictionary { Name = "Shift", WordsCount = 0, OwnerId = 5 });
        db.Dictionaries.Add(new Domain.Entities.Dictionary
        { Name = "My words", WordsCount = 0, OwnerId = 5, IsPersonal = true });
        await db.SaveChangesAsync();

        Assert.Equal(2, await new DictionaryDeletionService(db).DeleteOwnedAsync(ownerId: 5));
        Assert.True(await db.Dictionaries.AnyAsync(d => d.IsPersonal));
    }
}
