using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Slogs.Data;

public sealed class SlogsApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly HttpClient httpClient;
    private readonly ISlogsApiBackend? backend;

    public SlogsApiClient(HttpClient httpClient, ISlogsApiBackend? backend = null)
    {
        this.httpClient = httpClient;
        this.backend = backend;
    }

    public async Task<AuthUser?> GetCurrentUserAsync()
        => backend is not null ? await backend.GetCurrentUserAsync() : await GetJsonAsync<AuthUser>("api/auth/me");

    public async Task<AuthUser?> UpdateProfileAsync(string userName, ProfileUpdateRequest request)
    {
        if (backend is not null)
        {
            return await backend.UpdateProfileAsync(userName, request);
        }

        _ = userName;
        var response = await PutJsonAsync<AuthResponse, ProfileUpdateRequest>("api/auth/profile", request);
        return response?.User;
    }

    public async Task<IReadOnlyList<BlogPost>> GetLatestAsync(int count)
        => backend is not null ? await backend.GetLatestAsync(count) : await GetJsonAsync<List<BlogPost>>($"api/posts/latest?count={count}") ?? [];

    public async Task<IReadOnlyList<BlogPost>> SearchPostsAsync(string? query)
        => backend is not null ? await backend.SearchPostsAsync(query) : await GetJsonAsync<List<BlogPost>>($"api/posts/search?q={Escape(query)}") ?? [];

    public async Task<BlogPost?> GetBySlugAsync(string slug)
        => backend is not null ? await backend.GetBySlugAsync(slug) : await GetJsonAsync<BlogPost>($"api/posts/{EscapePath(slug)}");

    public async Task<BlogPost?> GetBySlugForReadAsync(string slug)
        => backend is not null ? await backend.GetBySlugForReadAsync(slug) : await GetJsonAsync<BlogPost>($"api/posts/{EscapePath(slug)}/read");

    public async Task<BlogPost?> UpdatePostAsync(string slug, string userName, string title, string summary, string body, string tags, string? series, string? thumbnailUrl = null, bool? isDraft = null, string? newSlug = null)
    {
        if (backend is not null)
        {
            return await backend.UpdatePostAsync(slug, userName, title, summary, body, tags, series, thumbnailUrl, isDraft, newSlug);
        }

        _ = userName;
        return await PutJsonAsync<BlogPost, PostUpsertRequest>(
            $"api/posts/{EscapePath(slug)}",
            new PostUpsertRequest(title, summary, body, tags, series, thumbnailUrl, isDraft, newSlug));
    }

    public async Task<bool> DeletePostAsync(string slug, string userName)
    {
        if (backend is not null)
        {
            return await backend.DeletePostAsync(slug, userName);
        }

        _ = userName;
        var response = await SendAsync(HttpMethod.Delete, $"api/posts/{EscapePath(slug)}");
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<BlogPost>> GetRelatedPostsAsync(string slug, int maxCount)
        => backend is not null ? await backend.GetRelatedPostsAsync(slug, maxCount) : await GetJsonAsync<List<BlogPost>>($"api/posts/{EscapePath(slug)}/related?maxCount={maxCount}") ?? [];

    public async Task<(BlogPost? Previous, BlogPost? Next)> GetAdjacentPostsAsync(string slug)
    {
        if (backend is not null)
        {
            return await backend.GetAdjacentPostsAsync(slug);
        }

        var adjacent = await GetJsonAsync<AdjacentPostsResponse>($"api/posts/{EscapePath(slug)}/adjacent");
        return (adjacent?.Previous, adjacent?.Next);
    }

    public async Task<IReadOnlyList<PostRevisionSummaryResponse>> GetPostRevisionsAsync(string slug)
        => backend is not null
            ? await backend.GetPostRevisionsAsync(slug)
            : await GetJsonAsync<List<PostRevisionSummaryResponse>>($"api/posts/{EscapePath(slug)}/revisions") ?? [];

    public async Task<PostRevisionResponse?> GetPostRevisionAsync(string slug, int revisionNumber)
        => backend is not null
            ? await backend.GetPostRevisionAsync(slug, revisionNumber)
            : await GetJsonAsync<PostRevisionResponse>($"api/posts/{EscapePath(slug)}/revisions/{revisionNumber}");

    public async Task<IReadOnlyList<BlogPost>> GetByTagAsync(string tag)
        => backend is not null ? await backend.GetByTagAsync(tag) : await GetJsonAsync<List<BlogPost>>($"api/tags/{EscapePath(tag)}/posts") ?? [];

    public async Task<IReadOnlyList<BlogPost>> GetMyPostsAsync(string userName)
    {
        if (backend is not null)
        {
            return await backend.GetMyPostsAsync(userName);
        }

        _ = userName;
        return await GetJsonAsync<List<BlogPost>>("api/me/posts") ?? [];
    }

    public async Task<IReadOnlyList<BlogPost>> GetByAuthorAsync(string author)
        => backend is not null ? await backend.GetByAuthorAsync(author) : await GetJsonAsync<List<BlogPost>>($"api/authors/{EscapePath(author)}/posts") ?? [];

    public async Task<IReadOnlyList<BlogPost>> GetByAuthorsAsync(IEnumerable<string> authors)
        => backend is not null
            ? await backend.GetByAuthorsAsync(authors)
            : await PostJsonAsync<List<BlogPost>, AuthorsRequest>("api/posts/by-authors", new AuthorsRequest(authors.ToArray())) ?? [];

    public async Task<IReadOnlyList<string>> GetTrendingTagsAsync(int topCount)
        => backend is not null ? await backend.GetTrendingTagsAsync(topCount) : await GetJsonAsync<List<string>>($"api/tags/trending?topCount={topCount}") ?? [];

    public async Task<IReadOnlyList<(string Tag, int Count)>> GetTagCloudAsync(int topCount)
        => backend is not null
            ? await backend.GetTagCloudAsync(topCount)
            : (await GetJsonAsync<List<TagSummary>>($"api/tags/cloud?topCount={topCount}") ?? [])
            .Select(x => (x.Tag, x.Count))
            .ToList();

    public async Task<IReadOnlyList<(string Author, int Count)>> GetAuthorCloudAsync(int topCount)
        => backend is not null
            ? await backend.GetAuthorCloudAsync(topCount)
            : (await GetJsonAsync<List<AuthorSummary>>($"api/authors/cloud?topCount={topCount}") ?? [])
            .Select(x => (x.Author, x.Count))
            .ToList();

    public async Task<IReadOnlyList<(string Series, int Count)>> GetSeriesCloudAsync(int topCount)
        => backend is not null
            ? await backend.GetSeriesCloudAsync(topCount)
            : (await GetJsonAsync<List<SeriesSummary>>($"api/series/cloud?topCount={topCount}") ?? [])
            .Select(x => (x.Series, x.Count))
            .ToList();

    public async Task<IReadOnlyList<(string Series, int Count, int LikeCount)>> GetPopularSeriesAsync(int topCount)
        => backend is not null
            ? await backend.GetPopularSeriesAsync(topCount)
            : (await GetJsonAsync<List<SeriesSummary>>($"api/series/popular?topCount={topCount}") ?? [])
            .Select(x => (x.Series, x.Count, x.LikeCount))
            .ToList();

    public async Task<IReadOnlyList<(string Series, int Count, int LikeCount)>> GetSeriesByAuthorAsync(string author, int topCount)
        => backend is not null
            ? await backend.GetSeriesByAuthorAsync(author, topCount)
            : (await GetJsonAsync<List<SeriesSummary>>($"api/authors/{EscapePath(author)}/series?topCount={topCount}") ?? [])
            .Select(x => (x.Series, x.Count, x.LikeCount))
            .ToList();

    public async Task<IReadOnlyList<string>> GetSeriesAsync(int topCount)
        => backend is not null ? await backend.GetSeriesAsync(topCount) : await GetJsonAsync<List<string>>($"api/series/names?topCount={topCount}") ?? [];

    public async Task<IReadOnlyList<BlogPost>> GetBySeriesAsync(string series)
        => backend is not null ? await backend.GetBySeriesAsync(series) : await GetJsonAsync<List<BlogPost>>($"api/series/{EscapePath(series)}/posts") ?? [];

    public IReadOnlyList<string> ParseTagsForDisplay(string tags)
        => tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.TrimStart('#').ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<BlogPost> CreatePostAsync(string title, string author, string summary, string body, string tags, string? series, string? thumbnailUrl = null, bool isDraft = false, string? slug = null)
    {
        if (backend is not null)
        {
            return await backend.CreatePostAsync(title, author, summary, body, tags, series, thumbnailUrl, isDraft, slug);
        }

        _ = author;
        var post = await PostJsonAsync<BlogPost, PostUpsertRequest>(
            "api/posts",
            new PostUpsertRequest(title, summary, body, tags, series, thumbnailUrl, isDraft, slug));
        return post ?? throw new InvalidOperationException("게시글 생성에 실패했습니다.");
    }

    public async Task<EditorImageResponse?> UploadEditorImageAsync(Stream imageStream, string fileName, string contentType)
    {
        using var request = CreateRequest(HttpMethod.Post, "editor/images");
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(imageStream);
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        form.Add(fileContent, "image", string.IsNullOrWhiteSpace(fileName) ? "image.png" : fileName);
        request.Content = form;

        using var response = await httpClient.SendAsync(request);
        return await ReadJsonAsync<EditorImageResponse>(response);
    }

    public async Task<bool> ToggleLikeAsync(string slug, string userName)
    {
        if (backend is not null)
        {
            return await backend.ToggleLikeAsync(slug, userName);
        }

        _ = userName;
        var response = await PostJsonAsync<ActionStateResponse, EmptyRequest>(
            $"api/posts/{EscapePath(slug)}/like",
            new EmptyRequest());
        return response?.Active ?? false;
    }

    public async Task<bool> ToggleBookmarkAsync(string slug, string userName)
    {
        if (backend is not null)
        {
            return await backend.ToggleBookmarkAsync(slug, userName);
        }

        _ = userName;
        var response = await PostJsonAsync<ActionStateResponse, EmptyRequest>(
            $"api/posts/{EscapePath(slug)}/bookmark",
            new EmptyRequest());
        return response?.Active ?? false;
    }

    public async Task<IReadOnlyList<BlogPost>> GetLikedPostsAsync(string userName)
    {
        if (backend is not null)
        {
            return await backend.GetLikedPostsAsync(userName);
        }

        _ = userName;
        return await GetJsonAsync<List<BlogPost>>("api/me/likes") ?? [];
    }

    public async Task<IReadOnlyList<BlogPost>> GetBookmarkedPostsAsync(string userName)
    {
        if (backend is not null)
        {
            return await backend.GetBookmarkedPostsAsync(userName);
        }

        _ = userName;
        return await GetJsonAsync<List<BlogPost>>("api/me/bookmarks") ?? [];
    }

    public Task<BlogComment?> AddCommentAsync(string slug, string author, string content)
        => AddCommentAsync(slug, author, string.Empty, content, null);

    public Task<BlogComment?> AddCommentAsync(string slug, string userName, string displayName, string content)
        => AddCommentAsync(slug, userName, displayName, content, null);

    public async Task<BlogComment?> AddCommentAsync(string slug, string userName, string displayName, string content, Guid? parentCommentId)
    {
        if (backend is not null)
        {
            return await backend.AddCommentAsync(slug, userName, displayName, content, parentCommentId);
        }

        _ = userName;
        _ = displayName;
        return await PostJsonAsync<BlogComment, CommentRequest>(
            $"api/posts/{EscapePath(slug)}/comments",
            new CommentRequest(content, parentCommentId));
    }

    public async Task<bool> RemoveCommentAsync(string slug, Guid commentId, string userName)
    {
        if (backend is not null)
        {
            return await backend.RemoveCommentAsync(slug, commentId, userName);
        }

        _ = userName;
        var response = await SendAsync(HttpMethod.Delete, $"api/posts/{EscapePath(slug)}/comments/{commentId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCommentAsync(string slug, Guid commentId, string userName, string content)
    {
        if (backend is not null)
        {
            return await backend.UpdateCommentAsync(slug, commentId, userName, content);
        }

        _ = userName;
        var response = await PutJsonAsync<UpdateStateResponse, CommentRequest>(
            $"api/posts/{EscapePath(slug)}/comments/{commentId}",
            new CommentRequest(content, null));
        return response?.Updated == true;
    }

    public async Task<bool> ToggleFollowAsync(string followerUser, string targetUser)
    {
        if (backend is not null)
        {
            return await backend.ToggleFollowAsync(followerUser, targetUser);
        }

        _ = followerUser;
        var response = await PostJsonAsync<ActionStateResponse, EmptyRequest>(
            $"api/users/{EscapePath(targetUser)}/follow",
            new EmptyRequest());
        return response?.Active ?? false;
    }

    public async Task<bool> IsFollowingAsync(string followerUser, string targetUser)
        => backend is not null ? await backend.IsFollowingAsync(followerUser, targetUser) : await GetJsonAsync<bool>($"api/users/{EscapePath(followerUser)}/is-following?targetUser={Escape(targetUser)}");

    public async Task<bool> IsKnownUserAsync(string userName)
        => backend is not null ? await backend.IsKnownUserAsync(userName) : await GetJsonAsync<bool>($"api/users/{EscapePath(userName)}/exists");

    public async Task<AuthUser?> GetUserAsync(string userName)
        => backend is not null ? await backend.GetUserAsync(userName) : await GetJsonAsync<AuthUser>($"api/users/{EscapePath(userName)}");

    public async Task<IReadOnlyList<AuthUser>> GetUsersAsync(IEnumerable<string> userNames)
        => backend is not null
            ? await backend.GetUsersAsync(userNames)
            : await PostJsonAsync<List<AuthUser>, UserNamesRequest>("api/users/by-names", new UserNamesRequest(userNames.ToArray())) ?? [];

    public async Task<AdminUserUsageResponse?> GetAdminUserUsageAsync()
        => backend is not null ? await backend.GetAdminUserUsageAsync() : await GetJsonAsync<AdminUserUsageResponse>("api/admin/users/llm-wiki-usage");

    public async Task<AdminObsidianStorageSettingsResponse?> GetAdminObsidianStorageSettingsAsync()
        => backend is not null
            ? await backend.GetAdminObsidianStorageSettingsAsync()
            : await GetJsonAsync<AdminObsidianStorageSettingsResponse>("api/admin/storage/obsidian");

    public async Task<AdminObsidianStorageSettingsResponse?> UpdateAdminObsidianStorageSettingsAsync(long totalCapacityBytes)
    {
        var requestPayload = new AdminObsidianStorageSettingsUpdateRequest(totalCapacityBytes);
        if (backend is not null)
        {
            return await backend.UpdateAdminObsidianStorageSettingsAsync(requestPayload);
        }

        using var request = CreateRequest(HttpMethod.Put, "api/admin/storage/obsidian");
        request.Content = JsonContent.Create(requestPayload, jsonTypeInfo: GetJsonTypeInfo<AdminObsidianStorageSettingsUpdateRequest>());
        using var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadApiErrorCodeAsync(response) ?? "obsidianStorageSettingsUpdateFailed");
        }

        return await ReadJsonAsync<AdminObsidianStorageSettingsResponse>(response);
    }

    public async Task<AuthUser?> ChangeAdminUserNameAsync(string currentUserName, string newUserName)
    {
        if (backend is not null)
        {
            return await backend.ChangeAdminUserNameAsync(currentUserName, new AdminUserNameUpdateRequest(newUserName));
        }

        using var request = CreateRequest(HttpMethod.Put, $"api/admin/users/{EscapePath(currentUserName)}/name");
        request.Content = JsonContent.Create(new AdminUserNameUpdateRequest(newUserName), jsonTypeInfo: GetJsonTypeInfo<AdminUserNameUpdateRequest>());
        using var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadApiErrorCodeAsync(response) ?? "adminUserNameChangeFailed");
        }

        return await ReadJsonAsync<AuthUser>(response);
    }

    public async Task<IReadOnlyList<string>> GetFollowingAsync(string followerUser)
        => backend is not null ? await backend.GetFollowingAsync(followerUser) : await GetJsonAsync<List<string>>($"api/users/{EscapePath(followerUser)}/following") ?? [];

    public async Task<IReadOnlyList<string>> GetFollowersAsync(string targetUser)
        => backend is not null ? await backend.GetFollowersAsync(targetUser) : await GetJsonAsync<List<string>>($"api/users/{EscapePath(targetUser)}/followers") ?? [];

    public async Task<int> GetFollowerCountAsync(string targetUser)
        => backend is not null ? await backend.GetFollowerCountAsync(targetUser) : await GetJsonAsync<int>($"api/users/{EscapePath(targetUser)}/follower-count");

    public async Task<IReadOnlyList<LlmWikiSearchResult>> SearchLlmWikiAsync(
        string userName,
        string? query,
        int limit = 20,
        int offset = 0,
        int minRelevancePercent = 50,
        string? categoryPath = null)
    {
        if (backend is not null)
        {
            return await backend.SearchLlmWikiAsync(userName, query, limit, offset, minRelevancePercent, categoryPath);
        }

        _ = userName;
        var categoryQuery = string.IsNullOrWhiteSpace(categoryPath)
            ? string.Empty
            : $"&categoryPath={Escape(categoryPath)}";
        return await GetJsonAsync<List<LlmWikiSearchResult>>(
            $"api/llm-wiki/entries?q={Escape(query)}&limit={limit}&offset={offset}&minRelevancePercent={minRelevancePercent}{categoryQuery}") ?? [];
    }

    public async Task<IReadOnlyList<LlmWikiCategorySummary>> GetLlmWikiCategoriesAsync(string userName)
    {
        if (backend is not null)
        {
            return await backend.GetLlmWikiCategoriesAsync(userName);
        }

        _ = userName;
        return await GetJsonAsync<List<LlmWikiCategorySummary>>("api/llm-wiki/categories") ?? [];
    }

    public async Task<LlmWikiEntryResponse?> GetLlmWikiEntryAsync(string userName, string idOrSlug)
    {
        if (backend is not null)
        {
            return await backend.GetLlmWikiEntryAsync(userName, idOrSlug);
        }

        _ = userName;
        return await GetJsonAsync<LlmWikiEntryResponse>($"api/llm-wiki/entries/{EscapePath(idOrSlug)}");
    }

    public async Task<LlmWikiEntryResponse> RememberLlmWikiAsync(string userName, LlmWikiRememberRequest request)
    {
        if (backend is not null)
        {
            return await backend.RememberLlmWikiAsync(userName, request);
        }

        _ = userName;
        var entry = await PostJsonAsync<LlmWikiEntryResponse, LlmWikiRememberRequest>("api/llm-wiki/entries", request);
        return entry ?? throw new InvalidOperationException("LLM Wiki 저장에 실패했습니다.");
    }

    public async Task<LlmWikiEntryResponse?> UpdateLlmWikiAsync(string userName, string idOrSlug, LlmWikiUpdateRequest request)
    {
        if (backend is not null)
        {
            return await backend.UpdateLlmWikiAsync(userName, idOrSlug, request);
        }

        _ = userName;
        return await PutJsonAsync<LlmWikiEntryResponse, LlmWikiUpdateRequest>(
            $"api/llm-wiki/entries/{EscapePath(idOrSlug)}",
            request);
    }

    public async Task<string> GetLlmWikiLlmsTextAsync(string userName, int limit = 50)
    {
        if (backend is not null)
        {
            return await backend.GetLlmWikiLlmsTextAsync(userName, limit);
        }

        _ = userName;
        using var response = await SendAsync(HttpMethod.Get, $"api/llm-wiki/llms.txt?limit={limit}");
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return string.Empty;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<IReadOnlyList<LlmWikiTokenResponse>> GetLlmWikiTokensAsync(string userName)
    {
        if (backend is not null)
        {
            return await backend.GetLlmWikiTokensAsync(userName);
        }

        _ = userName;
        return await GetJsonAsync<List<LlmWikiTokenResponse>>("api/llm-wiki/tokens") ?? [];
    }

    public async Task<LlmWikiTokenCreatedResponse?> CreateLlmWikiTokenAsync(string userName, string? name, IReadOnlyList<string>? scopes = null)
    {
        if (backend is not null)
        {
            return await backend.CreateLlmWikiTokenAsync(userName, name, scopes);
        }

        _ = userName;
        return await PostJsonAsync<LlmWikiTokenCreatedResponse, LlmWikiTokenCreateRequest>(
            "api/llm-wiki/tokens",
            new LlmWikiTokenCreateRequest(name ?? string.Empty, scopes));
    }

    public async Task<bool> RevokeLlmWikiTokenAsync(string userName, Guid tokenId)
    {
        if (backend is not null)
        {
            return await backend.RevokeLlmWikiTokenAsync(userName, tokenId);
        }

        _ = userName;
        var response = await SendAsync(HttpMethod.Delete, $"api/llm-wiki/tokens/{tokenId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<ObsidianVaultResponse>> GetObsidianVaultsAsync(string userName)
    {
        if (backend is not null)
        {
            return await backend.GetObsidianVaultsAsync(userName);
        }

        _ = userName;
        return await GetJsonAsync<List<ObsidianVaultResponse>>("api/obsidian/vaults") ?? [];
    }

    public async Task<ObsidianVaultResponse?> GetOrCreateObsidianVaultAsync(string userName, string vaultName)
    {
        if (backend is not null)
        {
            return await backend.GetOrCreateObsidianVaultAsync(userName, new ObsidianVaultCreateRequest(vaultName));
        }

        _ = userName;
        return await PostJsonAsync<ObsidianVaultResponse, ObsidianVaultCreateRequest>(
            "api/obsidian/vaults",
            new ObsidianVaultCreateRequest(vaultName));
    }

    public async Task<bool> DeleteObsidianVaultAsync(string userName, Guid vaultId, string vaultName)
    {
        var request = new ObsidianVaultDeleteRequest(vaultName);
        if (backend is not null)
        {
            return await backend.DeleteObsidianVaultAsync(userName, vaultId, request);
        }

        _ = userName;
        var response = await PostJsonAsync<UpdateStateResponse, ObsidianVaultDeleteRequest>(
            $"api/obsidian/vaults/{vaultId}/delete",
            request);
        return response?.Updated == true;
    }

    public async Task<ObsidianVaultStatusResponse?> GetObsidianVaultStatusAsync(string userName, Guid vaultId)
    {
        if (backend is not null)
        {
            return await backend.GetObsidianVaultStatusAsync(userName, vaultId);
        }

        _ = userName;
        return await GetJsonAsync<ObsidianVaultStatusResponse>($"api/obsidian/vaults/{vaultId}/status");
    }

    public async Task<IReadOnlyList<ObsidianVaultClientResponse>> GetObsidianVaultClientsAsync(string userName, Guid vaultId)
    {
        if (backend is not null)
        {
            return await backend.GetObsidianVaultClientsAsync(userName, vaultId);
        }

        _ = userName;
        return await GetJsonAsync<List<ObsidianVaultClientResponse>>($"api/obsidian/vaults/{vaultId}/clients") ?? [];
    }

    private async Task<T?> GetJsonAsync<T>(string relativeUrl)
    {
        using var response = await SendAsync(HttpMethod.Get, relativeUrl);
        return await ReadJsonAsync<T>(response);
    }

    private async Task<TResponse?> PostJsonAsync<TResponse, TPayload>(string relativeUrl, TPayload payload)
    {
        using var request = CreateRequest(HttpMethod.Post, relativeUrl);
        request.Content = JsonContent.Create(payload, jsonTypeInfo: GetJsonTypeInfo<TPayload>());
        using var response = await httpClient.SendAsync(request);
        return await ReadJsonAsync<TResponse>(response);
    }

    private async Task<TResponse?> PutJsonAsync<TResponse, TPayload>(string relativeUrl, TPayload payload)
    {
        using var request = CreateRequest(HttpMethod.Put, relativeUrl);
        request.Content = JsonContent.Create(payload, jsonTypeInfo: GetJsonTypeInfo<TPayload>());
        using var response = await httpClient.SendAsync(request);
        return await ReadJsonAsync<TResponse>(response);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl)
    {
        using var request = CreateRequest(method, relativeUrl);
        return await httpClient.SendAsync(request);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        return new HttpRequestMessage(method, relativeUrl);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.NoContent)
        {
            return default;
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, GetJsonTypeInfo<T>());
    }

    private static async Task<string?> ReadApiErrorCodeAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            return null;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            var apiError = await JsonSerializer.DeserializeAsync(stream, GetJsonTypeInfo<ApiErrorResponse>());
            return apiError?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.TypeInfoResolverChain.Insert(0, SlogsJsonSerializerContext.Default);
        return options;
    }

    private static JsonTypeInfo<T> GetJsonTypeInfo<T>()
    {
        return (JsonTypeInfo<T>?)SlogsJsonSerializerContext.Default.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"JSON metadata for {typeof(T).FullName} is not registered.");
    }

    private static string Escape(string? value)
        => Uri.EscapeDataString(value ?? string.Empty);

    private static string EscapePath(string value)
        => Uri.EscapeDataString(value.Trim());

}
