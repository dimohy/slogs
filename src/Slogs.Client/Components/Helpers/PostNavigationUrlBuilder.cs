using Slogs.Data;

namespace Slogs.Components.Helpers;

public static class PostNavigationUrlBuilder
{
    public const string MenuContextQueryName = "nav";
    public const string GlobalMenuContext = "global";
    public const string PersonalMenuContext = "personal";

    public static string BuildPostUrl(BlogPost post, string? menuContext = null, string? fragment = null)
    {
        var path = post.IsDraft
            ? $"/edit/{Uri.EscapeDataString(post.Slug)}"
            : BuildCanonicalPostUrl(post);

        return AddMenuContext(path, menuContext, fragment);
    }

    public static string BuildCanonicalPostUrl(BlogPost post)
        => $"/@{Uri.EscapeDataString(post.Author)}/{Uri.EscapeDataString(post.Slug)}";

    public static string BuildCommentsUrl(BlogPost post, string? menuContext = null)
        => BuildPostUrl(post, menuContext, "comments");

    public static string AddMenuContext(string path, string? menuContext, string? fragment = null)
    {
        var normalizedMenuContext = NormalizeMenuContext(menuContext);
        var query = string.IsNullOrWhiteSpace(normalizedMenuContext)
            ? string.Empty
            : $"?{MenuContextQueryName}={Uri.EscapeDataString(normalizedMenuContext)}";
        var hash = string.IsNullOrWhiteSpace(fragment)
            ? string.Empty
            : $"#{Uri.EscapeDataString(fragment.Trim().TrimStart('#'))}";

        return $"{path}{query}{hash}";
    }

    public static string? NormalizeMenuContext(string? menuContext)
        => menuContext?.Trim().ToLowerInvariant() switch
        {
            GlobalMenuContext => GlobalMenuContext,
            PersonalMenuContext => PersonalMenuContext,
            _ => null
        };
}
