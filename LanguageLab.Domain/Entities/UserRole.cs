namespace LanguageLab.Domain.Entities;

/// <summary>
/// Three levels: a regular learner, someone trusted to import books, and someone who
/// curates dictionaries and the user list. Stored as int, so new values go at the end;
/// crosses the wire as "user" / "uploader" / "admin".
/// </summary>
public enum UserRole
{
    User = 0,
    Admin = 1,

    /// <summary>May import books. Cannot re-publish or delete them afterwards — that stays with Admin.</summary>
    Uploader = 2,
}
