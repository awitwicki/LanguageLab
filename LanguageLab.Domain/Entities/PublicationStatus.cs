namespace LanguageLab.Domain.Entities;

/// <summary>
/// Where a dictionary stands on its way to being shared. Appended to, never renumbered — the
/// column is an int. Replaces the old IsPublic flag: Published is what that flag meant, and the
/// two states in between are what a moderation queue needs.
/// </summary>
public enum PublicationStatus
{
    /// <summary>Visible to its owner only. What an import produces unless its author may publish.</summary>
    Private = 0,

    /// <summary>The owner asked to share it; an admin has not answered yet. Still owner-only.</summary>
    Pending = 1,

    /// <summary>Visible to every signed-in user.</summary>
    Published = 2,

    /// <summary>An admin refused. Owner-only, and the owner cannot ask again.</summary>
    Rejected = 3,
}
