using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class BlogService(IDbContextFactory<SlogsDbContext> dbFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<BlogPost>> GetLatestAsync(int count)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var records = await db.Posts
            .AsNoTracking()
            .Include(x => x.Comments)
            .OrderByDescending(x => x.PublishedAt)
            .Take(count)
            .ToListAsync();

        return records.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<BlogPost>> SearchPostsAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetLatestAsync(15);
        }

        var lowered = query.Trim().ToLowerInvariant();
        var hashed = lowered.TrimStart('#');
        var bySeries = lowered.StartsWith("#", StringComparison.Ordinal) ? hashed : lowered;

        var posts = await LoadPostModelsAsync();
        var filtered = posts
            .Where(p =>
                p.Title.Contains(lowered, StringComparison.OrdinalIgnoreCase)
                || p.Summary.Contains(lowered, StringComparison.OrdinalIgnoreCase)
                || p.Body.Contains(lowered, StringComparison.OrdinalIgnoreCase)
                || p.Author.Contains(lowered, StringComparison.OrdinalIgnoreCase)
                || p.Tags.Any(t => t.Contains(lowered, StringComparison.OrdinalIgnoreCase))
                || p.Tags.Any(t => t.Equals(hashed, StringComparison.OrdinalIgnoreCase))
                || p.Series.Any(t => t.Contains(bySeries, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.PublishedAt)
            .ToList();

        return filtered;
    }

    public async Task<BlogPost?> GetBySlugAsync(string slug)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var record = await FindPostBySlugAsync(db, slug, tracking: false, includeComments: true);
        return record is null ? null : ToModel(record);
    }

    public async Task<BlogPost?> GetBySlugForReadAsync(string slug)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var record = await FindPostBySlugAsync(db, slug, tracking: true, includeComments: true);
        if (record is null)
        {
            return null;
        }

        record.ViewCount += 1;
        await db.SaveChangesAsync();
        return ToModel(record);
    }

    public async Task<BlogPost?> UpdatePostAsync(string slug, string userName, string title, string summary, string body, string tags, string? series, string? thumbnailUrl = null)
    {
        var user = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(user))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var post = await FindPostBySlugAsync(db, slug, tracking: true, includeComments: true);
        if (post is null || !post.Author.Equals(user, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var finalTitle = string.IsNullOrWhiteSpace(title) ? "제목 없음" : title.Trim();
        var finalSummary = string.IsNullOrWhiteSpace(summary)
            ? (string.IsNullOrWhiteSpace(body)
                ? "내용을 요약하지 못했어요."
                : (body.Trim().Length > 140 ? body.Trim()[..140] + "..." : body.Trim()))
            : summary.Trim();
        var finalBody = string.IsNullOrWhiteSpace(body) ? "내용이 없습니다." : body.Trim();
        var parsedTags = ParseTags(tags).Take(5).ToList();

        if (parsedTags.Count == 0)
        {
            parsedTags.Add("general");
        }

        post.Title = finalTitle;
        post.Summary = finalSummary;
        post.ThumbnailUrl = NormalizeOptionalUrl(thumbnailUrl);
        post.Body = finalBody;
        post.TagsJson = ToJson(parsedTags);
        post.SeriesJson = ToJson(ParseSeries(series));
        post.UpdatedAt = DateTime.UtcNow;
        post.ReadTimeMinutes = Math.Max(1, (int)Math.Ceiling(finalBody.Length / 250.0));

        await db.SaveChangesAsync();
        return ToModel(post);
    }

    public async Task<bool> DeletePostAsync(string slug, string userName)
    {
        var user = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(user))
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var post = await FindPostBySlugAsync(db, slug, tracking: true, includeComments: true);
        if (post is null || !post.Author.Equals(user, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        db.Posts.Remove(post);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<BlogPost>> GetRelatedPostsAsync(string slug, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Array.Empty<BlogPost>();
        }

        var posts = await LoadPostModelsAsync(includeComments: false);
        var source = posts.FirstOrDefault(x => x.Slug.Equals(slug.Trim(), StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return Array.Empty<BlogPost>();
        }

        var sourceTags = source.Tags.Select(t => t.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var sourceSeries = source.Series.Select(s => s.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        return posts
            .Where(p => !p.Slug.Equals(source.Slug, StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var score = 0;
                score += p.Tags.Count(t => sourceTags.Contains(t.ToLowerInvariant())) * 3;
                score += p.Series.Count(s => sourceSeries.Contains(s.ToLowerInvariant())) * 2;
                if (p.Author.Equals(source.Author, StringComparison.OrdinalIgnoreCase))
                {
                    score += 1;
                }

                return (Post: p, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Post.PublishedAt)
            .ThenBy(x => x.Post.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .Select(x => x.Post)
            .ToList();
    }

    public async Task<(BlogPost? Previous, BlogPost? Next)> GetAdjacentPostsAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return (null, null);
        }

        var ordered = (await LoadPostModelsAsync(includeComments: false))
            .OrderByDescending(x => x.PublishedAt)
            .ThenBy(x => x.Slug, StringComparer.Ordinal)
            .ToArray();

        var index = Array.FindIndex(ordered, x => x.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return (null, null);
        }

        var previous = index + 1 < ordered.Length ? ordered[index + 1] : null;
        var next = index - 1 >= 0 ? ordered[index - 1] : null;
        return (previous, next);
    }

    public async Task<IReadOnlyList<BlogPost>> GetByTagAsync(string tag)
    {
        var normalized = tag.Trim().ToLowerInvariant();
        var posts = await LoadPostModelsAsync();
        return posts
            .Where(p => p.Tags.Any(t => t.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.PublishedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<BlogPost>> GetByAuthorAsync(string author)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var normalized = NormalizeUser(author);
        var records = await db.Posts
            .AsNoTracking()
            .Include(x => x.Comments)
            .Where(p => p.Author == normalized)
            .OrderByDescending(x => x.PublishedAt)
            .ToListAsync();

        return records.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<BlogPost>> GetByAuthorsAsync(IEnumerable<string> authors)
    {
        var normalizedAuthors = authors
            .Select(NormalizeUser)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (normalizedAuthors.Count == 0)
        {
            return Array.Empty<BlogPost>();
        }

        var posts = await LoadPostModelsAsync();
        return posts
            .Where(p => normalizedAuthors.Contains(p.Author))
            .OrderByDescending(x => x.PublishedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetTrendingTagsAsync(int topCount)
    {
        var posts = await LoadPostModelsAsync(includeComments: false);
        return posts
            .SelectMany(p => p.Tags)
            .GroupBy(t => t.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(topCount)
            .Select(g => g.Key)
            .ToList();
    }

    public async Task<IReadOnlyList<(string Tag, int Count)>> GetTagCloudAsync(int topCount)
    {
        var posts = await LoadPostModelsAsync(includeComments: false);
        return posts
            .SelectMany(p => p.Tags)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(t => t.Trim())
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(topCount)
            .Select(g => (Tag: g.Key, Count: g.Count()))
            .ToList();
    }

    public async Task<IReadOnlyList<(string Author, int Count)>> GetAuthorCloudAsync(int topCount)
    {
        var posts = await LoadPostModelsAsync(includeComments: false);
        return posts
            .GroupBy(p => p.Author.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(topCount)
            .Select(g => (Author: g.Key, Count: g.Count()))
            .ToList();
    }

    public async Task<IReadOnlyList<(string Series, int Count)>> GetSeriesCloudAsync(int topCount)
    {
        var posts = await LoadPostModelsAsync(includeComments: false);
        return posts
            .SelectMany(p => p.Series)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(s => s.Trim())
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(topCount)
            .Select(g => (Series: g.Key, Count: g.Count()))
            .ToList();
    }

    public async Task<IReadOnlyList<(string Series, int Count, int LikeCount)>> GetPopularSeriesAsync(int topCount)
    {
        var posts = await LoadPostModelsAsync(includeComments: false);
        return BuildSeriesSummaries(posts)
            .OrderByDescending(x => x.LikeCount)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.Series, StringComparer.OrdinalIgnoreCase)
            .Take(topCount)
            .ToList();
    }

    public async Task<IReadOnlyList<(string Series, int Count, int LikeCount)>> GetSeriesByAuthorAsync(string author, int topCount)
    {
        var normalizedAuthor = NormalizeUser(author);
        if (string.IsNullOrWhiteSpace(normalizedAuthor))
        {
            return Array.Empty<(string Series, int Count, int LikeCount)>();
        }

        var posts = await LoadPostModelsAsync(includeComments: false);
        return BuildSeriesSummaries(posts.Where(x => x.Author.Equals(normalizedAuthor, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.LikeCount)
            .ThenBy(x => x.Series, StringComparer.OrdinalIgnoreCase)
            .Take(topCount)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetSeriesAsync(int topCount)
    {
        var posts = await LoadPostModelsAsync(includeComments: false);
        return posts
            .SelectMany(p => p.Series)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x.Trim())
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(topCount)
            .Select(g => g.Key)
            .ToList();
    }

    public async Task<IReadOnlyList<BlogPost>> GetBySeriesAsync(string series)
    {
        var normalized = series.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<BlogPost>();
        }

        var posts = await LoadPostModelsAsync();
        return posts
            .Where(p => p.Series.Any(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.PublishedAt)
            .ToList();
    }

    public IReadOnlyList<string> ParseTagsForDisplay(string tags)
    {
        return ParseTags(tags).ToList();
    }

    public async Task<BlogPost> CreatePostAsync(string title, string author, string summary, string body, string tags, string? series, string? thumbnailUrl = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var finalTitle = string.IsNullOrWhiteSpace(title) ? "제목 없음" : title.Trim();
        var finalAuthor = string.IsNullOrWhiteSpace(author) ? "guest" : NormalizeUser(author);
        var finalSummary = string.IsNullOrWhiteSpace(summary)
            ? (string.IsNullOrWhiteSpace(body)
                ? "내용을 요약하지 못했어요."
                : (body.Trim().Length > 140 ? body.Trim()[..140] + "..." : body.Trim()))
            : summary.Trim();
        var finalBody = string.IsNullOrWhiteSpace(body) ? "내용이 없습니다." : body.Trim();
        var parsedTags = ParseTags(tags).Take(5).ToList();

        if (parsedTags.Count == 0)
        {
            parsedTags.Add("general");
        }

        var slugs = await db.Posts.AsNoTracking().Select(x => x.Slug).ToListAsync();
        var newPost = new PostRecord
        {
            Title = finalTitle,
            Author = finalAuthor,
            Summary = finalSummary,
            ThumbnailUrl = NormalizeOptionalUrl(thumbnailUrl),
            Body = finalBody,
            PublishedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Slug = CreateUniqueSlug(finalTitle, slugs),
            ReadTimeMinutes = Math.Max(1, (int)Math.Ceiling(finalBody.Length / 250.0)),
            TagsJson = ToJson(parsedTags),
            SeriesJson = ToJson(ParseSeries(series))
        };

        db.Posts.Add(newPost);
        await db.SaveChangesAsync();
        return ToModel(newPost);
    }

    public async Task<bool> ToggleLikeAsync(string slug, string userName)
    {
        var normalizedUser = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(normalizedUser))
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var post = await FindPostBySlugAsync(db, slug, tracking: true, includeComments: false);
        if (post is null)
        {
            return false;
        }

        var likedBy = DeserializeSet(post.LikedByJson);
        var shouldAdd = likedBy.Add(normalizedUser);
        if (!shouldAdd)
        {
            _ = likedBy.Remove(normalizedUser);
        }

        post.LikedByJson = ToJson(likedBy);
        await db.SaveChangesAsync();
        return likedBy.Contains(normalizedUser);
    }

    public async Task<bool> ToggleBookmarkAsync(string slug, string userName)
    {
        var normalizedUser = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(normalizedUser))
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var post = await FindPostBySlugAsync(db, slug, tracking: true, includeComments: false);
        if (post is null)
        {
            return false;
        }

        var bookmarkedBy = DeserializeSet(post.BookmarkedByJson);
        var shouldAdd = bookmarkedBy.Add(normalizedUser);
        if (!shouldAdd)
        {
            _ = bookmarkedBy.Remove(normalizedUser);
        }

        post.BookmarkedByJson = ToJson(bookmarkedBy);
        await db.SaveChangesAsync();
        return bookmarkedBy.Contains(normalizedUser);
    }

    public async Task<IReadOnlyList<BlogPost>> GetLikedPostsAsync(string userName)
    {
        var normalizedUser = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(normalizedUser))
        {
            return Array.Empty<BlogPost>();
        }

        var posts = await LoadPostModelsAsync();
        return posts
            .Where(p => p.IsLikedBy(normalizedUser))
            .OrderByDescending(x => x.PublishedAt)
            .ToList();
    }

    public Task<BlogComment?> AddCommentAsync(string slug, string author, string content)
    {
        return AddCommentAsync(slug, author, string.Empty, content, null);
    }

    public Task<BlogComment?> AddCommentAsync(string slug, string userName, string displayName, string content)
    {
        return AddCommentAsync(slug, userName, displayName, content, null);
    }

    public async Task<BlogComment?> AddCommentAsync(string slug, string userName, string displayName, string content, Guid? parentCommentId)
    {
        var normalizedUser = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(normalizedUser) || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var post = await FindPostBySlugAsync(db, slug, tracking: true, includeComments: true);
        if (post is null)
        {
            return null;
        }

        if (parentCommentId.HasValue && !post.Comments.Any(comment => comment.Id == parentCommentId.Value))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var comment = new CommentRecord
        {
            PostId = post.Id,
            Author = string.IsNullOrWhiteSpace(displayName) ? userName.Trim() : displayName.Trim(),
            AuthorNormalized = normalizedUser,
            Content = content.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            ParentCommentId = parentCommentId
        };

        db.Comments.Add(comment);
        await db.SaveChangesAsync();
        return ToModel(comment);
    }

    public async Task<bool> RemoveCommentAsync(string slug, Guid commentId, string userName)
    {
        var normalizedUser = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(normalizedUser))
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var post = await FindPostBySlugAsync(db, slug, tracking: true, includeComments: true);
        if (post is null)
        {
            return false;
        }

        var comment = post.Comments.FirstOrDefault(c => c.Id == commentId);
        if (comment is null || !CanDeleteComment(comment, normalizedUser))
        {
            return false;
        }

        var idsToDelete = new HashSet<Guid> { comment.Id };
        var queue = new Queue<Guid>(idsToDelete);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var childComments = post.Comments.Where(x => x.ParentCommentId == id).ToList();
            foreach (var child in childComments)
            {
                if (idsToDelete.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        db.Comments.RemoveRange(post.Comments.Where(c => idsToDelete.Contains(c.Id)));
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateCommentAsync(string slug, Guid commentId, string userName, string content)
    {
        var normalizedUser = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(normalizedUser) || string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var post = await FindPostBySlugAsync(db, slug, tracking: true, includeComments: true);
        if (post is null)
        {
            return false;
        }

        var comment = post.Comments.FirstOrDefault(c => c.Id == commentId);
        if (comment is null || !CanDeleteComment(comment, normalizedUser))
        {
            return false;
        }

        var normalizedContent = content.Trim();
        if (normalizedContent.Length > 1000)
        {
            return false;
        }

        comment.Content = normalizedContent;
        comment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<BlogPost>> GetBookmarkedPostsAsync(string userName)
    {
        var normalizedUser = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(normalizedUser))
        {
            return Array.Empty<BlogPost>();
        }

        var posts = await LoadPostModelsAsync();
        return posts
            .Where(p => p.IsBookmarkedBy(normalizedUser))
            .OrderByDescending(x => x.PublishedAt)
            .ToList();
    }

    private async Task<List<BlogPost>> LoadPostModelsAsync(bool includeComments = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        IQueryable<PostRecord> query = db.Posts.AsNoTracking();
        if (includeComments)
        {
            query = query.Include(x => x.Comments);
        }

        var records = await query.ToListAsync();
        return records.Select(ToModel).ToList();
    }

    private static Task<PostRecord?> FindPostBySlugAsync(SlogsDbContext db, string slug, bool tracking, bool includeComments)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        IQueryable<PostRecord> query = tracking ? db.Posts : db.Posts.AsNoTracking();
        if (includeComments)
        {
            query = query.Include(x => x.Comments);
        }

        return query.FirstOrDefaultAsync(x => x.Slug == normalizedSlug);
    }

    private static bool CanDeleteComment(CommentRecord comment, string normalizedUser)
    {
        return string.Equals(normalizedUser, "admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                string.IsNullOrWhiteSpace(comment.AuthorNormalized)
                    ? NormalizeUser(comment.Author)
                    : comment.AuthorNormalized,
                normalizedUser,
                StringComparison.OrdinalIgnoreCase);
    }

    private static BlogPost ToModel(PostRecord record)
    {
        return new BlogPost
        {
            Id = record.Id,
            Title = record.Title,
            Slug = record.Slug,
            Author = record.Author,
            Summary = record.Summary,
            ThumbnailUrl = record.ThumbnailUrl,
            Body = record.Body,
            PublishedAt = record.PublishedAt,
            UpdatedAt = record.UpdatedAt,
            ReadTimeMinutes = record.ReadTimeMinutes,
            ViewCount = record.ViewCount,
            Tags = DeserializeList(record.TagsJson),
            Series = DeserializeList(record.SeriesJson),
            LikedBy = DeserializeSet(record.LikedByJson),
            BookmarkedBy = DeserializeSet(record.BookmarkedByJson),
            Comments = record.Comments
                .OrderByDescending(x => x.CreatedAt)
                .Select(ToModel)
                .ToList()
        };
    }

    private static BlogComment ToModel(CommentRecord record)
    {
        return new BlogComment
        {
            Id = record.Id,
            Author = record.Author,
            AuthorNormalized = record.AuthorNormalized,
            Content = record.Content,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            ParentCommentId = record.ParentCommentId
        };
    }

    private static IEnumerable<string> ParseTags(string tags)
    {
        return tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.TrimStart('#').ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParseSeries(string? series)
    {
        if (string.IsNullOrWhiteSpace(series))
        {
            return [];
        }

        return [series.Trim()];
    }

    private static IReadOnlyList<(string Series, int Count, int LikeCount)> BuildSeriesSummaries(IEnumerable<BlogPost> posts)
    {
        return posts
            .SelectMany(post => post.Series
                .Where(series => !string.IsNullOrWhiteSpace(series))
                .Select(series => (Series: series.Trim(), LikeCount: post.LikeCount)))
            .GroupBy(x => x.Series, StringComparer.OrdinalIgnoreCase)
            .Select(group => (
                Series: group.Key,
                Count: group.Count(),
                LikeCount: group.Sum(x => x.LikeCount)))
            .ToList();
    }

    private static string NormalizeOptionalUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("/", StringComparison.Ordinal) && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return trimmed.Length <= 500 ? trimmed : trimmed[..500];
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? (trimmed.Length <= 500 ? trimmed : trimmed[..500])
            : string.Empty;
    }

    private static string CreateUniqueSlug(string title, IReadOnlyCollection<string> existingSlugs)
    {
        var baseSlug = CreateSlug(title);
        var slug = baseSlug;
        var index = 2;

        while (existingSlugs.Any(p => p.Equals(slug, StringComparison.OrdinalIgnoreCase)))
        {
            slug = $"{baseSlug}-{index}";
            index++;
        }

        return slug;
    }

    private static string CreateSlug(string title)
    {
        var normalized = title.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c);
        }

        var ascii = builder.ToString();
        ascii = Regex.Replace(ascii, "[^a-z0-9\\s-]", string.Empty);
        ascii = Regex.Replace(ascii, "[\\s-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(ascii) ? "post" : ascii;
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static HashSet<string> DeserializeSet(string? json)
    {
        return DeserializeList(json)
            .Select(NormalizeUser)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string ToJson(IEnumerable<string> values)
    {
        return JsonSerializer.Serialize(
            values
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            JsonOptions);
    }

    private static string NormalizeUser(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
