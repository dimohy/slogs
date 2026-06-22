using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class BlogService(
    IDbContextFactory<SlogsDbContext> dbFactory,
    PostImageService postImageService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<BlogPost>> GetLatestAsync(int count)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var records = await db.Posts
            .AsNoTracking()
            .Include(x => x.Comments)
            .Where(x => !x.IsDraft)
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

    public async Task<BlogPost?> GetBySlugAsync(string slug, string? viewerUserName = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var record = await FindPostBySlugAsync(db, slug, tracking: false, includeComments: true);
        if (record is null || !CanViewPost(record, viewerUserName))
        {
            return null;
        }

        return record is null ? null : ToModel(record);
    }

    public async Task<BlogPost?> GetBySlugForReadAsync(string slug, string? viewerUserName = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var record = await FindPostBySlugAsync(db, slug, tracking: true, includeComments: true);
        if (record is null || !CanViewPost(record, viewerUserName))
        {
            return null;
        }

        if (!record.IsDraft)
        {
            record.ViewCount += 1;
            await db.SaveChangesAsync();
        }

        return ToModel(record);
    }

    public async Task<BlogPost?> UpdatePostAsync(string slug, string userName, string title, string summary, string body, string tags, string? series, string? thumbnailUrl = null, bool? isDraft = null, string? newSlug = null)
    {
        var user = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(user))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var post = await FindPostBySlugAsync(db, slug, tracking: true, includeComments: true, includeRevisions: true);
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
        var parsedSeries = ParseSeries(series);
        var now = DateTime.UtcNow;

        if (!post.IsDraft)
        {
            if (isDraft == true)
            {
                return null;
            }

            EnsureBaselineRevision(db, post);
            ApplyPostContent(post, finalTitle, finalSummary, finalBody, parsedTags, parsedSeries, thumbnailUrl, now);
            var nextRevisionNumber = post.Revisions.Select(x => x.RevisionNumber).DefaultIfEmpty(0).Max() + 1;
            db.PostRevisions.Add(CreateRevisionRecord(post, nextRevisionNumber, now));
            await db.SaveChangesAsync();
            await postImageService.SyncPostImagesAsync(user, post.Id, finalBody);
            return ToModel(post);
        }

        if (!string.IsNullOrWhiteSpace(newSlug))
        {
            var slugs = await db.Posts
                .AsNoTracking()
                .Where(x => x.Id != post.Id)
                .Select(x => x.Slug)
                .ToListAsync();
            post.Slug = CreateUniqueSlug(newSlug, slugs);
        }

        ApplyPostContent(post, finalTitle, finalSummary, finalBody, parsedTags, parsedSeries, thumbnailUrl, now);
        if (isDraft.HasValue && !isDraft.Value)
        {
            post.IsDraft = false;
            post.PublishedAt = now;
            var nextRevisionNumber = post.Revisions.Select(x => x.RevisionNumber).DefaultIfEmpty(0).Max() + 1;
            db.PostRevisions.Add(CreateRevisionRecord(post, nextRevisionNumber, now));
        }

        await db.SaveChangesAsync();
        await postImageService.SyncPostImagesAsync(user, post.Id, finalBody);
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

        await postImageService.DeletePostImagesAsync(user, post.Id, post.Body);
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

    public async Task<IReadOnlyList<PostRevisionResponse>> GetPostRevisionsAsync(string slug, string? viewerUserName = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Array.Empty<PostRevisionResponse>();
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var record = await FindPostBySlugAsync(db, slug, tracking: false, includeComments: false, includeRevisions: true);
        if (record is null || !CanViewPost(record, viewerUserName) || record.IsDraft)
        {
            return Array.Empty<PostRevisionResponse>();
        }

        var revisions = record.Revisions
            .OrderBy(x => x.RevisionNumber)
            .ToList();
        if (revisions.Count == 0)
        {
            revisions.Add(CreateRevisionRecord(record, 1, record.PublishedAt));
        }

        var result = new List<PostRevisionResponse>();
        PostRevisionRecord? previous = null;
        foreach (var revision in revisions)
        {
            result.Add(ToRevisionResponse(revision, previous));
            previous = revision;
        }

        return result;
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
            .Where(p => !p.IsDraft)
            .OrderByDescending(x => x.PublishedAt)
            .ToListAsync();

        return records.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<BlogPost>> GetManageByAuthorAsync(string author)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var normalized = NormalizeUser(author);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<BlogPost>();
        }

        var records = await db.Posts
            .AsNoTracking()
            .Include(x => x.Comments)
            .Where(p => p.Author == normalized)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.PublishedAt)
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

    public async Task<BlogPost> CreatePostAsync(string title, string author, string summary, string body, string tags, string? series, string? thumbnailUrl = null, bool isDraft = false, string? slug = null)
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
        var parsedSeries = ParseSeries(series);
        var now = DateTime.UtcNow;

        var slugs = await db.Posts.AsNoTracking().Select(x => x.Slug).ToListAsync();
        var newPost = new PostRecord
        {
            Title = finalTitle,
            Author = finalAuthor,
            Summary = finalSummary,
            ThumbnailUrl = ResolveRepresentativeImageUrl(thumbnailUrl, finalBody),
            Body = finalBody,
            PublishedAt = now,
            UpdatedAt = now,
            IsDraft = isDraft,
            Slug = CreateUniqueSlug(string.IsNullOrWhiteSpace(slug) ? finalTitle : slug, slugs),
            ReadTimeMinutes = Math.Max(1, (int)Math.Ceiling(finalBody.Length / 250.0)),
            TagsJson = ToJson(parsedTags),
            SeriesJson = ToJson(parsedSeries)
        };

        db.Posts.Add(newPost);
        if (!isDraft)
        {
            db.PostRevisions.Add(CreateRevisionRecord(newPost, 1, now));
        }

        await db.SaveChangesAsync();
        await postImageService.SyncPostImagesAsync(finalAuthor, newPost.Id, finalBody);
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
        if (post is null || post.IsDraft)
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
        if (post is null || post.IsDraft)
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
        if (post is null || post.IsDraft)
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

    public async Task<bool> RemoveCommentAsync(string slug, Guid commentId, string userName, bool isAdmin = false)
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
        if (comment is null || !CanDeleteComment(comment, normalizedUser, isAdmin))
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

    public async Task<bool> UpdateCommentAsync(string slug, Guid commentId, string userName, string content, bool isAdmin = false)
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
        if (comment is null || !CanDeleteComment(comment, normalizedUser, isAdmin))
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

    private async Task<List<BlogPost>> LoadPostModelsAsync(bool includeComments = true, bool publishedOnly = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        IQueryable<PostRecord> query = db.Posts.AsNoTracking();
        if (publishedOnly)
        {
            query = query.Where(x => !x.IsDraft);
        }

        if (includeComments)
        {
            query = query.Include(x => x.Comments);
        }

        var records = await query.ToListAsync();
        return records.Select(ToModel).ToList();
    }

    private static Task<PostRecord?> FindPostBySlugAsync(
        SlogsDbContext db,
        string slug,
        bool tracking,
        bool includeComments,
        bool includeRevisions = false)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        IQueryable<PostRecord> query = tracking ? db.Posts : db.Posts.AsNoTracking();
        if (includeComments)
        {
            query = query.Include(x => x.Comments);
        }

        if (includeRevisions)
        {
            query = query.Include(x => x.Revisions);
        }

        return query.FirstOrDefaultAsync(x => x.Slug == normalizedSlug);
    }

    private static bool CanDeleteComment(CommentRecord comment, string normalizedUser, bool isAdmin)
    {
        return isAdmin
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
            IsDraft = record.IsDraft,
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

    private static PostRevisionResponse ToRevisionResponse(PostRevisionRecord record, PostRevisionRecord? previous)
    {
        return new PostRevisionResponse(
            record.RevisionNumber,
            record.CreatedAt,
            record.Title,
            record.Summary,
            record.Body,
            DeserializeList(record.TagsJson),
            DeserializeList(record.SeriesJson),
            record.ThumbnailUrl,
            GetChangedFields(record, previous));
    }

    private static IReadOnlyList<string> GetChangedFields(PostRevisionRecord current, PostRevisionRecord? previous)
    {
        if (previous is null)
        {
            return ["초기 게시"];
        }

        var changed = new List<string>();
        if (!string.Equals(current.Title, previous.Title, StringComparison.Ordinal))
        {
            changed.Add("제목");
        }

        if (!string.Equals(current.Summary, previous.Summary, StringComparison.Ordinal))
        {
            changed.Add("요약");
        }

        if (!string.Equals(current.Body, previous.Body, StringComparison.Ordinal))
        {
            changed.Add("본문");
        }

        if (!string.Equals(current.TagsJson, previous.TagsJson, StringComparison.Ordinal))
        {
            changed.Add("태그");
        }

        if (!string.Equals(current.SeriesJson, previous.SeriesJson, StringComparison.Ordinal))
        {
            changed.Add("시리즈");
        }

        if (!string.Equals(current.ThumbnailUrl, previous.ThumbnailUrl, StringComparison.Ordinal))
        {
            changed.Add("대표 이미지");
        }

        return changed.Count == 0 ? ["변경 없음"] : changed;
    }

    private static void ApplyPostContent(
        PostRecord post,
        string title,
        string summary,
        string body,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> series,
        string? thumbnailUrl,
        DateTime updatedAt)
    {
        post.Title = title;
        post.Summary = summary;
        post.ThumbnailUrl = ResolveRepresentativeImageUrl(thumbnailUrl, body);
        post.Body = body;
        post.TagsJson = ToJson(tags);
        post.SeriesJson = ToJson(series);
        post.UpdatedAt = updatedAt;
        post.ReadTimeMinutes = Math.Max(1, (int)Math.Ceiling(body.Length / 250.0));
    }

    private static void EnsureBaselineRevision(SlogsDbContext db, PostRecord post)
    {
        if (post.Revisions.Count > 0)
        {
            return;
        }

        var baseline = CreateRevisionRecord(post, 1, post.PublishedAt);
        db.PostRevisions.Add(baseline);
        post.Revisions.Add(baseline);
    }

    private static PostRevisionRecord CreateRevisionRecord(PostRecord post, int revisionNumber, DateTime createdAt)
        => new()
        {
            PostId = post.Id,
            RevisionNumber = revisionNumber,
            Title = post.Title,
            Summary = post.Summary,
            ThumbnailUrl = post.ThumbnailUrl,
            Body = post.Body,
            TagsJson = post.TagsJson,
            SeriesJson = post.SeriesJson,
            CreatedAt = createdAt,
            Author = post.Author
        };

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

    private static string ResolveRepresentativeImageUrl(string? explicitUrl, string body)
    {
        var normalizedUrl = NormalizeOptionalUrl(explicitUrl);
        if (!string.IsNullOrWhiteSpace(normalizedUrl))
        {
            var normalizedUploadUrl = EditorImageStorage.NormalizeUploadUrl(normalizedUrl);
            if (string.IsNullOrWhiteSpace(normalizedUploadUrl)
                || PostImageService.ExtractReferencedUploadUrls(body).Contains(normalizedUploadUrl, StringComparer.Ordinal))
            {
                return normalizedUrl;
            }
        }

        return NormalizeOptionalUrl(MarkdownRenderer.FindFirstImage(body)?.Url);
    }

    private static string CreateUniqueSlug(string value, IReadOnlyCollection<string> existingSlugs)
    {
        var baseSlug = SlugGenerator.Normalize(value);
        var slug = baseSlug;
        var index = 2;

        while (existingSlugs.Any(p => p.Equals(slug, StringComparison.OrdinalIgnoreCase)))
        {
            var suffix = $"-{index}";
            var safeBaseSlug = baseSlug.Length + suffix.Length > SlugGenerator.MaxLength
                ? baseSlug[..(SlugGenerator.MaxLength - suffix.Length)].Trim('-')
                : baseSlug;
            slug = SlugGenerator.Normalize($"{safeBaseSlug}{suffix}");
            index++;
        }

        return slug;
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(json, GetJsonTypeInfo<List<string>>()) ?? [];
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
        var normalizedValues = values
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return JsonSerializer.Serialize(normalizedValues, GetJsonTypeInfo<string[]>());
    }

    private static JsonTypeInfo<T> GetJsonTypeInfo<T>()
    {
        return (JsonTypeInfo<T>?)SlogsJsonSerializerContext.Default.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"JSON metadata for {typeof(T).FullName} is not registered.");
    }

    private static string NormalizeUser(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static bool CanViewPost(PostRecord post, string? viewerUserName)
        => !post.IsDraft
            || post.Author.Equals(NormalizeUser(viewerUserName ?? string.Empty), StringComparison.OrdinalIgnoreCase);
}
