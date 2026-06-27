namespace Slogs.Data;

public sealed class ServerSlogsApiBackend(
    BlogService blogService,
    AuthService authService,
    LlmWikiService llmWikiService,
    IHttpContextAccessor httpContextAccessor) : ISlogsApiBackend
{
    public Task<AuthUser?> GetCurrentUserAsync()
        => Task.FromResult(GetCurrentUser());

    public Task<AuthUser> UpdateProfileAsync(string userName, ProfileUpdateRequest request)
        => authService.UpdateProfileAsync(
            ResolveUserName(userName),
            request.DisplayName,
            request.Email,
            request.ProfileImageUrl,
            request.Bio);

    public Task<IReadOnlyList<BlogPost>> GetLatestAsync(int count)
        => blogService.GetLatestAsync(count);

    public Task<IReadOnlyList<BlogPost>> SearchPostsAsync(string? query)
        => blogService.SearchPostsAsync(query);

    public Task<BlogPost?> GetBySlugAsync(string slug)
        => blogService.GetBySlugAsync(slug, GetCurrentUser()?.UserName);

    public Task<BlogPost?> GetBySlugForReadAsync(string slug)
        => blogService.GetBySlugForReadAsync(slug, GetCurrentUser()?.UserName);

    public Task<BlogPost?> UpdatePostAsync(string slug, string userName, string title, string summary, string body, string tags, string? series, string? thumbnailUrl = null, bool? isDraft = null, string? newSlug = null)
        => blogService.UpdatePostAsync(slug, ResolveUserName(userName), title, summary, body, tags, series, thumbnailUrl, isDraft, newSlug);

    public Task<bool> DeletePostAsync(string slug, string userName)
        => blogService.DeletePostAsync(slug, ResolveUserName(userName));

    public Task<IReadOnlyList<BlogPost>> GetRelatedPostsAsync(string slug, int maxCount)
        => blogService.GetRelatedPostsAsync(slug, maxCount);

    public Task<(BlogPost? Previous, BlogPost? Next)> GetAdjacentPostsAsync(string slug)
        => blogService.GetAdjacentPostsAsync(slug);

    public Task<IReadOnlyList<PostRevisionResponse>> GetPostRevisionsAsync(string slug)
        => blogService.GetPostRevisionsAsync(slug, GetCurrentUser()?.UserName);

    public Task<IReadOnlyList<BlogPost>> GetByTagAsync(string tag)
        => blogService.GetByTagAsync(tag);

    public Task<IReadOnlyList<BlogPost>> GetMyPostsAsync(string userName)
        => blogService.GetManageByAuthorAsync(ResolveUserName(userName));

    public Task<IReadOnlyList<BlogPost>> GetByAuthorAsync(string author)
        => blogService.GetByAuthorAsync(author);

    public Task<IReadOnlyList<BlogPost>> GetByAuthorsAsync(IEnumerable<string> authors)
        => blogService.GetByAuthorsAsync(authors);

    public Task<IReadOnlyList<string>> GetTrendingTagsAsync(int topCount)
        => blogService.GetTrendingTagsAsync(topCount);

    public Task<IReadOnlyList<(string Tag, int Count)>> GetTagCloudAsync(int topCount)
        => blogService.GetTagCloudAsync(topCount);

    public Task<IReadOnlyList<(string Author, int Count)>> GetAuthorCloudAsync(int topCount)
        => blogService.GetAuthorCloudAsync(topCount);

    public Task<IReadOnlyList<(string Series, int Count)>> GetSeriesCloudAsync(int topCount)
        => blogService.GetSeriesCloudAsync(topCount);

    public Task<IReadOnlyList<(string Series, int Count, int LikeCount)>> GetPopularSeriesAsync(int topCount)
        => blogService.GetPopularSeriesAsync(topCount);

    public Task<IReadOnlyList<(string Series, int Count, int LikeCount)>> GetSeriesByAuthorAsync(string author, int topCount)
        => blogService.GetSeriesByAuthorAsync(author, topCount);

    public Task<IReadOnlyList<string>> GetSeriesAsync(int topCount)
        => blogService.GetSeriesAsync(topCount);

    public Task<IReadOnlyList<BlogPost>> GetBySeriesAsync(string series)
        => blogService.GetBySeriesAsync(series);

    public Task<BlogPost> CreatePostAsync(string title, string author, string summary, string body, string tags, string? series, string? thumbnailUrl = null, bool isDraft = false, string? slug = null)
        => blogService.CreatePostAsync(title, ResolveUserName(author), summary, body, tags, series, thumbnailUrl, isDraft, slug);

    public Task<bool> ToggleLikeAsync(string slug, string userName)
        => blogService.ToggleLikeAsync(slug, ResolveUserName(userName));

    public Task<bool> ToggleBookmarkAsync(string slug, string userName)
        => blogService.ToggleBookmarkAsync(slug, ResolveUserName(userName));

    public Task<IReadOnlyList<BlogPost>> GetLikedPostsAsync(string userName)
        => blogService.GetLikedPostsAsync(ResolveUserName(userName));

    public Task<IReadOnlyList<BlogPost>> GetBookmarkedPostsAsync(string userName)
        => blogService.GetBookmarkedPostsAsync(ResolveUserName(userName));

    public Task<BlogComment?> AddCommentAsync(string slug, string userName, string displayName, string content, Guid? parentCommentId)
    {
        var user = GetCurrentUser();
        return blogService.AddCommentAsync(
            slug,
            ResolveUserName(userName),
            string.IsNullOrWhiteSpace(user?.DisplayName) ? displayName : user.DisplayName,
            content,
            parentCommentId);
    }

    public Task<bool> RemoveCommentAsync(string slug, Guid commentId, string userName)
        => blogService.RemoveCommentAsync(slug, commentId, ResolveUserName(userName), GetCurrentUser()?.IsAdmin == true);

    public Task<bool> UpdateCommentAsync(string slug, Guid commentId, string userName, string content)
        => blogService.UpdateCommentAsync(slug, commentId, ResolveUserName(userName), content, GetCurrentUser()?.IsAdmin == true);

    public Task<bool> ToggleFollowAsync(string followerUser, string targetUser)
        => authService.ToggleFollowAsync(ResolveUserName(followerUser), targetUser);

    public Task<bool> IsFollowingAsync(string followerUser, string targetUser)
        => authService.IsFollowingAsync(ResolveUserName(followerUser), targetUser);

    public Task<bool> IsKnownUserAsync(string userName)
        => authService.IsKnownUserAsync(userName);

    public Task<AuthUser?> GetUserAsync(string userName)
        => authService.GetUserAsync(userName);

    public Task<IReadOnlyList<AuthUser>> GetUsersAsync(IEnumerable<string> userNames)
        => authService.GetUsersAsync(userNames);

    public Task<AuthUser> ChangeAdminUserNameAsync(string userName, AdminUserNameUpdateRequest request)
        => GetCurrentUser()?.IsAdmin == true
            ? authService.ChangeAdminUserNameAsync(userName, request.UserName)
            : throw new InvalidOperationException("adminRequired");

    public Task<AdminUserUsageResponse> GetAdminUserUsageAsync()
        => GetCurrentUser()?.IsAdmin == true
            ? authService.GetAdminUserUsageAsync()
            : throw new InvalidOperationException("adminRequired");

    public Task<IReadOnlyList<string>> GetFollowingAsync(string followerUser)
        => authService.GetFollowingAsync(ResolveUserName(followerUser));

    public Task<IReadOnlyList<string>> GetFollowersAsync(string targetUser)
        => authService.GetFollowersAsync(targetUser);

    public Task<int> GetFollowerCountAsync(string targetUser)
        => authService.GetFollowerCountAsync(targetUser);

    public Task<IReadOnlyList<LlmWikiSearchResult>> SearchLlmWikiAsync(
        string userName,
        string? query,
        int limit,
        int offset = 0,
        int minRelevancePercent = 50,
        string? categoryPath = null)
        => llmWikiService.SearchAsync(ResolveUserName(userName), query, limit, offset, minRelevancePercent, categoryPath);

    public Task<IReadOnlyList<LlmWikiCategorySummary>> GetLlmWikiCategoriesAsync(string userName)
        => llmWikiService.GetCategoriesAsync(ResolveUserName(userName));

    public Task<LlmWikiEntryResponse?> GetLlmWikiEntryAsync(string userName, string idOrSlug)
        => llmWikiService.GetEntryAsync(ResolveUserName(userName), idOrSlug);

    public Task<LlmWikiEntryResponse> RememberLlmWikiAsync(string userName, LlmWikiRememberRequest request)
        => llmWikiService.RememberAsync(ResolveUserName(userName), request);

    public Task<LlmWikiEntryResponse?> UpdateLlmWikiAsync(string userName, string idOrSlug, LlmWikiUpdateRequest request)
        => llmWikiService.UpdateAsync(ResolveUserName(userName), idOrSlug, request);

    public Task<string> GetLlmWikiLlmsTextAsync(string userName, int limit)
        => llmWikiService.BuildLlmsTextAsync(ResolveUserName(userName), limit);

    public Task<IReadOnlyList<LlmWikiTokenResponse>> GetLlmWikiTokensAsync(string userName)
        => llmWikiService.GetTokensAsync(ResolveUserName(userName));

    public Task<LlmWikiTokenCreatedResponse> CreateLlmWikiTokenAsync(string userName, string? name)
        => llmWikiService.CreateTokenAsync(ResolveUserName(userName), name);

    public Task<bool> RevokeLlmWikiTokenAsync(string userName, Guid tokenId)
        => llmWikiService.RevokeTokenAsync(ResolveUserName(userName), tokenId);

    private string ResolveUserName(string fallback)
        => GetCurrentUser()?.UserName ?? fallback;

    private AuthUser? GetCurrentUser()
        => SlogsAuthentication.TryCreateUser(httpContextAccessor.HttpContext?.User) ?? authService.CurrentUser;
}
