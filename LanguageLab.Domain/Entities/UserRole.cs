namespace LanguageLab.Domain.Entities;

/// <summary>
/// Two levels is all the app needs: a regular learner, and someone who curates
/// dictionaries and the user list. Stored as int; crosses the wire as "user" / "admin".
/// </summary>
public enum UserRole
{
    User = 0,
    Admin = 1,
}
