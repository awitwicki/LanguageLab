using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class DictionaryPublicationServiceTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<Domain.Entities.Dictionary> Seed(
        ApplicationDbContext db, PublicationStatus status, long ownerId = 1, bool personal = false)
    {
        var dictionary = new Domain.Entities.Dictionary
        { Name = "Wool", WordsCount = 1, OwnerId = ownerId, PublicationStatus = status, IsPersonal = personal };

        db.Dictionaries.Add(dictionary);
        await db.SaveChangesAsync();

        return dictionary;
    }

    [Fact]
    public async Task A_private_dictionary_can_be_offered_for_publication()
    {
        await using var db = NewContext();
        var dictionary = await Seed(db, PublicationStatus.Private);

        Assert.Equal(PublicationActionResult.Ok,
            await new DictionaryPublicationService(db).RequestAsync(userId: 1, dictionary.Id));

        Assert.Equal(PublicationStatus.Pending, (await db.Dictionaries.SingleAsync()).PublicationStatus);
    }

    [Theory]
    [InlineData(PublicationStatus.Pending)]
    [InlineData(PublicationStatus.Published)]
    [InlineData(PublicationStatus.Rejected)]
    public async Task Only_a_private_dictionary_can_be_offered(PublicationStatus status)
    {
        await using var db = NewContext();
        var dictionary = await Seed(db, status);

        Assert.Equal(PublicationActionResult.WrongState,
            await new DictionaryPublicationService(db).RequestAsync(userId: 1, dictionary.Id));
    }

    [Fact]
    public async Task Somebody_elses_dictionary_is_not_found()
    {
        await using var db = NewContext();
        var dictionary = await Seed(db, PublicationStatus.Private, ownerId: 2);

        Assert.Equal(PublicationActionResult.NotFound,
            await new DictionaryPublicationService(db).RequestAsync(userId: 1, dictionary.Id));
    }

    [Fact]
    public async Task A_personal_list_is_not_publishable()
    {
        await using var db = NewContext();
        var dictionary = await Seed(db, PublicationStatus.Private, personal: true);

        Assert.Equal(PublicationActionResult.NotFound,
            await new DictionaryPublicationService(db).RequestAsync(userId: 1, dictionary.Id));
    }

    [Fact]
    public async Task A_pending_request_can_be_withdrawn()
    {
        await using var db = NewContext();
        var dictionary = await Seed(db, PublicationStatus.Pending);

        Assert.Equal(PublicationActionResult.Ok,
            await new DictionaryPublicationService(db).WithdrawAsync(userId: 1, dictionary.Id));

        Assert.Equal(PublicationStatus.Private, (await db.Dictionaries.SingleAsync()).PublicationStatus);
    }

    [Fact]
    public async Task Withdrawing_what_was_never_offered_is_a_wrong_state()
    {
        await using var db = NewContext();
        var dictionary = await Seed(db, PublicationStatus.Published);

        Assert.Equal(PublicationActionResult.WrongState,
            await new DictionaryPublicationService(db).WithdrawAsync(userId: 1, dictionary.Id));
    }

    [Fact]
    public async Task An_admin_can_set_any_status_including_back_to_private()
    {
        await using var db = NewContext();
        var dictionary = await Seed(db, PublicationStatus.Rejected);
        var service = new DictionaryPublicationService(db);

        Assert.Equal(PublicationActionResult.Ok,
            await service.SetStatusAsync(dictionary.Id, PublicationStatus.Private));

        Assert.Equal(PublicationStatus.Private, (await db.Dictionaries.SingleAsync()).PublicationStatus);
    }

    [Fact]
    public async Task The_queue_lists_pending_dictionaries_oldest_first_with_their_top_words()
    {
        await using var db = NewContext();

        var first = await Seed(db, PublicationStatus.Pending);
        var second = await Seed(db, PublicationStatus.Pending);
        await Seed(db, PublicationStatus.Private);

        var rare = new WordPair { Word = "abide", Translation = "дотримуватися" };
        var common = new WordPair { Word = "silo", Translation = "бункер" };
        db.Words.AddRange(rare, common);
        await db.SaveChangesAsync();

        db.DictionaryWords.Add(new DictionaryWord { DictionaryId = first.Id, WordPairId = common.Id, Frequency = 90 });
        db.DictionaryWords.Add(new DictionaryWord { DictionaryId = first.Id, WordPairId = rare.Id, Frequency = 2 });
        await db.SaveChangesAsync();

        var page = await new DictionaryPublicationService(db)
            .ListAsync(PublicationStatus.Pending, page: 1, pageSize: 25);

        Assert.Equal(2, page.Total);
        Assert.Equal(first.Id, page.Items[0].Id);
        Assert.Equal(second.Id, page.Items[1].Id);
        Assert.Equal(["silo", "abide"], page.Items[0].TopWords);
    }

    [Fact]
    public async Task The_queue_never_shows_a_personal_list()
    {
        await using var db = NewContext();
        await Seed(db, PublicationStatus.Pending, personal: true);

        var page = await new DictionaryPublicationService(db)
            .ListAsync(PublicationStatus.Pending, page: 1, pageSize: 25);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Paging_is_clamped_rather_than_refused()
    {
        await using var db = NewContext();
        await Seed(db, PublicationStatus.Pending);

        var page = await new DictionaryPublicationService(db)
            .ListAsync(PublicationStatus.Pending, page: 0, pageSize: 10_000);

        Assert.Equal(1, page.Page);
        Assert.Equal(DictionaryPublicationService.MaxPageSize, page.PageSize);
    }
}
