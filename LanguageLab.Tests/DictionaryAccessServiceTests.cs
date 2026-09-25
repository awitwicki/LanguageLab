using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class DictionaryAccessServiceTests
{
    private const long Owner = 1;
    private const long Stranger = 2;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Dictionaries.AddRange(
            new Domain.Entities.Dictionary { Id = 10, Name = "public", OwnerId = Owner, PublicationStatus = PublicationStatus.Published },
            new Domain.Entities.Dictionary { Id = 20, Name = "private", OwnerId = Owner, PublicationStatus = PublicationStatus.Private },
            new Domain.Entities.Dictionary { Id = 30, Name = "system", OwnerId = null, PublicationStatus = PublicationStatus.Published },
            new Domain.Entities.Dictionary { Id = 40, Name = "My words", OwnerId = Owner, PublicationStatus = PublicationStatus.Private, IsPersonal = true });

        await db.SaveChangesAsync();

        return db;
    }

    [Fact]
    public async Task A_regular_user_sees_public_and_system_dictionaries_only()
    {
        await using var db = await SeedAsync();

        var ids = await new DictionaryAccessService(db)
            .Visible(Stranger, UserRole.User)
            .Select(d => d.Id)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(new long[] { 10, 30 }, ids);
    }

    [Fact]
    public async Task An_owner_also_sees_their_private_dictionary()
    {
        await using var db = await SeedAsync();

        var ids = await new DictionaryAccessService(db)
            .Visible(Owner, UserRole.User)
            .Select(d => d.Id)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(new long[] { 10, 20, 30, 40 }, ids);
    }

    /// <summary>An uploader curates nothing: their view is a regular user's, own imports included.</summary>
    [Fact]
    public async Task An_uploader_sees_their_own_imports_but_not_other_peoples_private_dictionaries()
    {
        await using var db = await SeedAsync();
        var access = new DictionaryAccessService(db);

        var ownIds = await access.Visible(Owner, UserRole.Uploader).Select(d => d.Id).OrderBy(id => id).ToListAsync();
        var strangerIds = await access.Visible(Stranger, UserRole.Uploader).Select(d => d.Id).OrderBy(id => id).ToListAsync();

        Assert.Equal(new long[] { 10, 20, 30, 40 }, ownIds);
        Assert.Equal(new long[] { 10, 30 }, strangerIds);
    }

    [Fact]
    public async Task An_admin_sees_everything_including_other_peoples_private_dictionaries()
    {
        await using var db = await SeedAsync();

        var ids = await new DictionaryAccessService(db)
            .Visible(Stranger, UserRole.Admin)
            .Select(d => d.Id)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(new long[] { 10, 20, 30 }, ids);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(20, false)]
    [InlineData(30, true)]
    [InlineData(40, false)]
    [InlineData(999, false)]
    public async Task IsVisibleAsync_matches_the_query(long dictionaryId, bool expected)
    {
        await using var db = await SeedAsync();

        var visible = await new DictionaryAccessService(db)
            .IsVisibleAsync(dictionaryId, Stranger, UserRole.User);

        Assert.Equal(expected, visible);
    }

    /// <summary>A personal dictionary is someone's private notes: not even an admin browses it.</summary>
    [Fact]
    public async Task A_personal_dictionary_is_visible_to_its_owner_only()
    {
        await using var db = await SeedAsync();
        var access = new DictionaryAccessService(db);

        Assert.True(await access.IsVisibleAsync(40, Owner, UserRole.User));
        Assert.True(await access.IsVisibleAsync(40, Owner, UserRole.Admin));
        Assert.False(await access.IsVisibleAsync(40, Stranger, UserRole.User));
        Assert.False(await access.IsVisibleAsync(40, Stranger, UserRole.Admin));
    }

    [Theory]
    [InlineData(PublicationStatus.Private, false)]
    [InlineData(PublicationStatus.Pending, false)]
    [InlineData(PublicationStatus.Rejected, false)]
    [InlineData(PublicationStatus.Published, true)]
    public async Task Only_a_published_dictionary_is_visible_to_a_stranger(PublicationStatus status, bool expected)
    {
        await using var db = NewContext();

        db.Dictionaries.Add(new Domain.Entities.Dictionary
        { Name = "Wool", WordsCount = 1, OwnerId = 2, PublicationStatus = status });
        await db.SaveChangesAsync();

        var visible = await new DictionaryAccessService(db).Visible(userId: 1, UserRole.User).AnyAsync();

        Assert.Equal(expected, visible);
    }

    [Theory]
    [InlineData(PublicationStatus.Private)]
    [InlineData(PublicationStatus.Pending)]
    [InlineData(PublicationStatus.Rejected)]
    public async Task The_owner_sees_their_own_dictionary_in_every_state(PublicationStatus status)
    {
        await using var db = NewContext();

        db.Dictionaries.Add(new Domain.Entities.Dictionary
        { Name = "Wool", WordsCount = 1, OwnerId = 1, PublicationStatus = status });
        await db.SaveChangesAsync();

        Assert.True(await new DictionaryAccessService(db).Visible(userId: 1, UserRole.User).AnyAsync());
    }
}
