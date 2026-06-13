using System.Globalization;

namespace Slogs.Components.Helpers;

public static class ListQueryUrlBuilder
{
    public static string? NormalizeSortValue(string? sort, params string[] allowedSorts)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return null;
        }

        var normalizedSort = sort.Trim().ToLowerInvariant();
        return Array.Exists(allowedSorts, allowed =>
            string.Equals(allowed, normalizedSort, StringComparison.OrdinalIgnoreCase))
            ? normalizedSort
            : null;
    }

    public static string? NormalizeFeedValue(string? feed)
    {
        if (string.IsNullOrWhiteSpace(feed))
        {
            return null;
        }

        var normalizedFeed = feed.Trim().ToLowerInvariant();
        return normalizedFeed == "following" ? normalizedFeed : null;
    }

    public static int ParsePage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 1;
    }

    public static string Build(
        string basePath,
        string? sort,
        string? query,
        int? page,
        int totalPages,
        string? feed = null)
    {
        var normalizedSort = sort?.Trim().ToLowerInvariant();
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var normalizedFeed = feed?.Trim().ToLowerInvariant();
        var normalizedPage = Math.Max(1, page ?? 1);
        var maxPage = Math.Max(1, totalPages);
        if (normalizedPage > maxPage)
        {
            normalizedPage = maxPage;
        }

        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            parameters.Add($"q={Uri.EscapeDataString(normalizedQuery)}");
        }

        if (!string.IsNullOrWhiteSpace(normalizedSort))
        {
            parameters.Add($"sort={Uri.EscapeDataString(normalizedSort)}");
        }

        if (!string.IsNullOrWhiteSpace(normalizedFeed))
        {
            parameters.Add($"feed={Uri.EscapeDataString(normalizedFeed)}");
        }

        if (normalizedPage > 1)
        {
            parameters.Add($"page={normalizedPage}");
        }

        return parameters.Count == 0 ? basePath : $"{basePath}?{string.Join("&", parameters)}";
    }
}
