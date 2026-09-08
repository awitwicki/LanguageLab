using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class AdminUserServiceTests
{
    private const long AdminId = 1;
    private const long OtherAdminId = 2;
    private const long MemberId = 3;

    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<ApplicationDbContext> SeedAsync(bool secondAdmin = false)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.AddRange(
            new TelegramUser { Id = AdminId, TelegramUserId = 101, Role = UserRole.Admin, FirstName = "Ada", CreatedAt = Now },
            new TelegramUser
            {
                Id = OtherAdminId, TelegramUserId = 102, FirstName = "Bo", CreatedAt = Now.AddDays(1),
                Role = secondAdmin ? UserRole.Admin : UserRole.User,
            },
            new TelegramUser { Id = MemberId, TelegramUserId = 103, Role = UserRole.User, FirstName = "Cy", CreatedAt = Now.AddDays(2) });

        await db.SaveChangesAsync();

        return db;
    }

    [Fact]
    public async Task Lists_users_oldest_first()
    {
        await using var db = await SeedAsync();

        var users = await new AdminUserService(db).ListAsync();

        Assert.Equal(new[] { AdminId, OtherAdminId, MemberId }, users.Select(u => u.Id));
        Assert.Equal("Ada", users[0].DisplayName);
        Assert.Equal(UserRole.Admin, users[0].Role);
    }

    [Fact]
    public async Task Ban_and_unban_round_trip()
    {
        await using var db = await SeedAsync();
        var service = new AdminUserService(db);

        Assert.Equal(AdminActionResult.Ok, await service.SetBannedAsync(AdminId, MemberId, true));
        Assert.True((await db.Users.FirstAsync(u => u.Id == MemberId)).IsBanned);

        Assert.Equal(AdminActionResult.Ok, await service.SetBannedAsync(AdminId, MemberId, false));
        Assert.False((await db.Users.FirstAsync(u => u.Id == MemberId)).IsBanned);
    }

    /// <summary>Locking yourself out of your own admin panel should not be one click away.</summary>
    [Theory]
    [InlineData("ban")]
    [InlineData("demote")]
    [InlineData("delete")]
    public async Task An_admin_cannot_act_on_themselves(string action)
    {
        await using var db = await SeedAsync(secondAdmin: true);
        var service = new AdminUserService(db);

        var result = action switch
        {
            "ban" => await service.SetBannedAsync(AdminId, AdminId, true),
            "demote" => await service.SetRoleAsync(AdminId, AdminId, UserRole.User),
            _ => await service.DeleteAsync(AdminId, AdminId),
        };

        Assert.Equal(AdminActionResult.SelfAction, result);
        Assert.Equal(3, await db.Users.CountAsync());
    }

    /// <summary>
    /// Unreachable over HTTP today — the only admin is necessarily the caller, and the
    /// self guard fires first — but the invariant is the service's, not the endpoint's.
    /// </summary>
    [Fact]
    public async Task The_last_admin_cannot_be_demoted_or_deleted()
    {
        await using var db = await SeedAsync();
        var service = new AdminUserService(db);

        Assert.Equal(AdminActionResult.LastAdmin, await service.SetRoleAsync(MemberId, AdminId, UserRole.User));
        Assert.Equal(AdminActionResult.LastAdmin, await service.DeleteAsync(MemberId, AdminId));
        Assert.Equal(UserRole.Admin, (await db.Users.FirstAsync(u => u.Id == AdminId)).Role);
    }

    [Fact]
    public async Task An_admin_can_be_demoted_while_another_admin_remains()
    {
        await using var db = await SeedAsync(secondAdmin: true);

        var result = await new AdminUserService(db).SetRoleAsync(AdminId, OtherAdminId, UserRole.User);

        Assert.Equal(AdminActionResult.Ok, result);
        Assert.Equal(UserRole.User, (await db.Users.FirstAsync(u => u.Id == OtherAdminId)).Role);
    }

    [Fact]
    public async Task Promoting_a_member_works()
    {
        await using var db = await SeedAsync();

        var result = await new AdminUserService(db).SetRoleAsync(AdminId, MemberId, UserRole.Admin);

        Assert.Equal(AdminActionResult.Ok, result);
        Assert.Equal(UserRole.Admin, (await db.Users.FirstAsync(u => u.Id == MemberId)).Role);
    }

    [Fact]
    public async Task Deleting_a_member_removes_the_row()
    {
        await using var db = await SeedAsync();

        Assert.Equal(AdminActionResult.Ok, await new AdminUserService(db).DeleteAsync(AdminId, MemberId));
        Assert.Null(await db.Users.FirstOrDefaultAsync(u => u.Id == MemberId));
    }

    [Fact]
    public async Task Acting_on_a_missing_user_reports_not_found()
    {
        await using var db = await SeedAsync();

        Assert.Equal(AdminActionResult.NotFound, await new AdminUserService(db).SetBannedAsync(AdminId, 999, true));
    }
}
