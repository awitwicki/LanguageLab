namespace LanguageLab.Domain.Entities;

/// <summary>
/// Two levels: a regular learner, and someone who curates dictionaries and the user list.
/// Stored as int, so new values go at the end; crosses the wire as "user" / "admin".
/// </summary>
public enum UserRole
{
    User = 0,
    Admin = 1,
}
