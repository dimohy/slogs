namespace Slogs.Data;

public interface ISlogsApiBackend
{
    Task<AuthUser?> GetCurrentUserAsync();
    Task<IReadOnlyList<BlogPost>> GetLatestAsync(int count);
    Task<IReadOnlyList<BlogPost>> SearchPostsAsync(string? query);
    Task<BlogPost?> GetBySlugAsync(string slug);
    Task<BlogPost?> GetBySlugForReadAsync(string slug);
    Task<BlogPost?> UpdatePostAsync(string slug, string userName, string title, string summary, string body, string tags, string? series, string? thumbnailUrl = null);
    Task<bool> DeletePostAsync(string slug, string userName);
    Task<IReadOnlyList<BlogPost>> GetRelatedPostsAsync(string slug, int maxCount);
    Task<(BlogPost? Previous, BlogPost? Next)> GetAdjacentPostsAsync(string slug);
    Task<IReadOnlyList<BlogPost>> GetByTagAsync(string tag);
    Task<IReadOnlyList<BlogPost>> GetByAuthorAsync(string author);
    Task<IReadOnlyList<BlogPost>> GetByAuthorsAsync(IEnumerable<string> authors);
    Task<IReadOnlyList<string>> GetTrendingTagsAsync(int topCount);
    Task<IReadOnlyList<(string Tag, int Count)>> GetTagCloudAsync(int topCount);
    Task<IReadOnlyList<(string Author, int Count)>> GetAuthorCloudAsync(int topCount);
    Task<IReadOnlyList<(string Series, int Count)>> GetSeriesCloudAsync(int topCount);
    Task<IReadOnlyList<(string Series, int Count, int LikeCount)>> GetPopularSeriesAsync(int topCount);
    Task<IReadOnlyList<(string Series, int Count, int LikeCount)>> GetSeriesByAuthorAsync(string author, int topCount);
    Task<IReadOnlyList<string>> GetSeriesAsync(int topCount);
    Task<IReadOnlyList<BlogPost>> GetBySeriesAsync(string series);
    Task<BlogPost> CreatePostAsync(string title, string author, string summary, string body, string tags, string? series, string? thumbnailUrl = null);
    Task<bool> ToggleLikeAsync(string slug, string userName);
    Task<bool> ToggleBookmarkAsync(string slug, string userName);
    Task<IReadOnlyList<BlogPost>> GetLikedPostsAsync(string userName);
    Task<IReadOnlyList<BlogPost>> GetBookmarkedPostsAsync(string userName);
    Task<BlogComment?> AddCommentAsync(string slug, string userName, string displayName, string content, Guid? parentCommentId);
    Task<bool> RemoveCommentAsync(string slug, Guid commentId, string userName);
    Task<bool> UpdateCommentAsync(string slug, Guid commentId, string userName, string content);
    Task<bool> ToggleFollowAsync(string followerUser, string targetUser);
    Task<bool> IsFollowingAsync(string followerUser, string targetUser);
    Task<bool> IsKnownUserAsync(string userName);
    Task<AuthUser?> GetUserAsync(string userName);
    Task<IReadOnlyList<AuthUser>> GetUsersAsync(IEnumerable<string> userNames);
    Task<IReadOnlyList<string>> GetFollowingAsync(string followerUser);
    Task<IReadOnlyList<string>> GetFollowersAsync(string targetUser);
    Task<int> GetFollowerCountAsync(string targetUser);
}
