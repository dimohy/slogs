using System.Globalization;
using System.Linq;

namespace Slogs.Data;

public sealed class BlogPost
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string ThumbnailUrl { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDraft { get; set; }

    public int ReadTimeMinutes { get; set; }

    public int ViewCount { get; set; }

    public List<string> Tags { get; set; } = [];

    public List<string> Series { get; set; } = [];

    public List<BlogComment> Comments { get; set; } = [];

    public HashSet<string> LikedBy { get; set; } = [];

    public HashSet<string> BookmarkedBy { get; set; } = [];

    public int LikeCount => LikedBy.Count;

    public int CommentCount => Comments.Count;

    public bool IsLikedBy(string userName) => LikedBy.Contains(NormalizeUserName(userName));

    public bool IsBookmarkedBy(string userName) => BookmarkedBy.Contains(NormalizeUserName(userName));

    public bool IsAuthor(string userName) => string.Equals(Author, NormalizeUserName(userName), StringComparison.OrdinalIgnoreCase);

    public void ToggleLike(string userName)
    {
        var normalized = NormalizeUserName(userName);

        if (!LikedBy.Add(normalized))
        {
            _ = LikedBy.Remove(normalized);
        }
    }

    public void ToggleBookmark(string userName)
    {
        var normalized = NormalizeUserName(userName);

        if (!BookmarkedBy.Add(normalized))
        {
            _ = BookmarkedBy.Remove(normalized);
        }
    }

    public void AddComment(BlogComment comment)
    {
        Comments.Insert(0, comment);
    }

    public string[] GetSafeTags() => Tags.Select(t => t.Trim().ToLowerInvariant()).ToArray();

    private static string NormalizeUserName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("/@", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        else if (normalized.StartsWith('@'))
        {
            normalized = normalized[1..];
        }

        return normalized.Trim().ToLowerInvariant();
    }
}

public sealed class BlogComment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Author { get; set; } = string.Empty;

    public string AuthorNormalized { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid? ParentCommentId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsEdited => UpdatedAt > CreatedAt;

    public bool IsAuthor(string userName) => string.Equals(
        string.IsNullOrWhiteSpace(AuthorNormalized)
            ? NormalizeUserName(Author)
            : AuthorNormalized,
        NormalizeUserName(userName),
        StringComparison.OrdinalIgnoreCase);

    private static string NormalizeUserName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("/@", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        else if (normalized.StartsWith('@'))
        {
            normalized = normalized[1..];
        }

        return normalized.Trim().ToLowerInvariant();
    }
}
