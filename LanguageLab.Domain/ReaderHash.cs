namespace LanguageLab.Domain;

/// <summary>
/// The identity of a book file across devices: lowercase hex SHA-256 of its bytes, computed in
/// the browser. The server never sees the file, so this is all it can compare.
/// </summary>
public static class ReaderHash
{
    public const int Length = 64;

    /// <summary>Lowercased; null for anything that is not 64 hex characters.</summary>
    public static string? Normalize(string? raw)
    {
        if (raw == null)
        {
            return null;
        }

        var hash = raw.Trim().ToLowerInvariant();

        return hash.Length == Length && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? hash
            : null;
    }
}
