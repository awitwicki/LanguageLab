using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class DictionaryAccessServiceTests
{
    private const long Owner = 1;
    private const long Stranger = 2;

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Dictionaries.AddRange(
            new Domain.Entities.Dictionary { Id = 10, Name = "public", OwnerId = Owner, IsPublic = true },
            new Domain.Entities.Dictionary { Id = 20, Name = "private", OwnerId = Owner, IsPublic = false },
            new Domain.Entities.Dictionary { Id = 30, Name = "system", OwnerId = null, IsPublic = true });

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

        Assert.Equal(new long[] { 10, 20, 30 }, ids);
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
    [InlineData(999, false)]
    public async Task IsVisibleAsync_matches_the_query(long dictionaryId, bool expected)
    {
        await using var db = await SeedAsync();

        var visible = await new DictionaryAccessService(db)
            .IsVisibleAsync(dictionaryId, Stranger, UserRole.User);

        Assert.Equal(expected, visible);
    }
}
