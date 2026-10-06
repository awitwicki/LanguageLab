using LanguageLab.Domain.Grammar;
using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// An account. Identity comes from Telegram, so there is no password here —
/// TelegramUserId is the login, and the profile fields are a cache of whatever
/// the login widget last told us.
/// </summary>
public class TelegramUser : BaseEntity
{
    public long TelegramUserId { get; set; }

    /// <summary>Telegram only guarantees id and first_name, so every profile field is optional.</summary>
    public string? Username { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? PhotoUrl { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>A banned user keeps their data but cannot sign in, and an existing session dies on the next request.</summary>
    public bool IsBanned { get; set; }

    /// <summary>
    /// Every session cookie carries the version it was issued under; bumping this (logout) makes
    /// all of them invalid on their next request — the cookie alone cannot be revoked otherwise.
    /// </summary>
    public int SessionVersion { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC. Null for a row that was created before its owner ever logged in.</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// The learner's main language, a LearnerLanguages code: every translation they see is into
    /// it. Null only for a new account that has not picked yet — the SPA asks before anything else.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Telegram's language_code from the last Mini App sign-in, verbatim. Only feeds the
    /// picker's suggestion (LearnerLanguages.FromTelegram); never used as Language by itself.
    /// </summary>
    public string? TelegramLanguageCode { get; set; }

    /// <summary>
    /// How many words the learner last chose for an irregular-verbs round, kept across devices.
    /// Null for an account that has never started one — the client falls back to its own default.
    /// </summary>
    public int? VerbsWordCount { get; set; }

    /// <summary>
    /// The CEFR level the learner is aiming for in Grammar; the topic list shows topics up to it.
    /// Null until they pick one, which reads as A1.
    /// </summary>
    public GrammarLevel? GrammarGoal { get; set; }

    /// <summary>
    /// What to call this person on screen. Lives here rather than in a view mapper because
    /// both /api/auth/me and the admin list need the same answer, and the fallbacks matter:
    /// Telegram guarantees first_name at login, but a row can predate any login.
    /// </summary>
    [NotMapped]
    public string DisplayName
    {
        get
        {
            var name = string.Join(' ', new[] { FirstName, LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return string.IsNullOrWhiteSpace(Username) ? $"User {TelegramUserId}" : $"@{Username}";
        }
    }
}
