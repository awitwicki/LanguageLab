namespace LanguageLab.Domain.Entities;

/// <summary>
/// What a role may do, where the answer is not a policy on an endpoint. Importing a book is open
/// to every signed-in user; publishing one without review is not. Kept as its own predicate
/// rather than an inline admin check: the question at the call sites is whether an import skips
/// the moderation queue, not whether the importer curates. Mirrored in web/src/auth/roles.ts.
/// </summary>
public static class UserRoles
{
    public static bool CanPublishDirectly(UserRole role) => role is UserRole.Admin;
}
