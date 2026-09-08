namespace LanguageLab.Api.Endpoints;

internal static class QueryParsing
{
    /// <summary>«1,2,3» → [1, 2, 3]. Порожній або кривий рядок означає «вся книжка».</summary>
    public static List<long>? ParseChapterIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var ids = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => long.TryParse(part, out var id) ? id : (long?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        return ids.Count == 0 ? null : ids;
    }
}
