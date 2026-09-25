namespace LanguageLab.Domain.Entities;

/// <summary>
/// What a role may do, where the answer is not a policy on an endpoint. Importing is open to
/// everyone; publishing without review is not, and that is the only difference an uploader has
/// from a plain user. Mirrored in web/src/auth/roles.ts.
/// </summary>
public static class UserRoles
{
    public static bool CanPublishDirectly(UserRole role) => role is UserRole.Uploader or UserRole.Admin;
}
