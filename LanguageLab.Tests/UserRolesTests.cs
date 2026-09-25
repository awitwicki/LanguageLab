using LanguageLab.Domain.Entities;

namespace LanguageLab.Tests;

public class UserRolesTests
{
    [Theory]
    [InlineData(UserRole.User, false)]
    [InlineData(UserRole.Uploader, true)]
    [InlineData(UserRole.Admin, true)]
    public void Only_uploaders_and_admins_publish_without_review(UserRole role, bool expected) =>
        Assert.Equal(expected, UserRoles.CanPublishDirectly(role));
}
