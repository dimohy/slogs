namespace Slogs.Data;

public sealed record ApiErrorResponse(string Error);

public sealed record AuthRequest(string UserName, string Password, string? ReturnUrl);

public sealed record RegisterRequest(string UserName, string DisplayName, string Password, string ConfirmPassword, string? ReturnUrl);

public sealed record ProfileUpdateRequest(string DisplayName, string? Email, string? ProfileImageUrl, string? Bio);

public sealed record AuthResponse(AuthUser User, string ReturnUrl);

public sealed record PostUpsertRequest(string Title, string Summary, string Body, string Tags, string? Series, string? ThumbnailUrl, bool? IsDraft = null, string? Slug = null);

public sealed record PostRevisionSummaryResponse(
    int RevisionNumber,
    DateTime CreatedAt,
    IReadOnlyList<string> ChangedFields);

public sealed record PostRevisionResponse(
    int RevisionNumber,
    DateTime CreatedAt,
    string Title,
    string Summary,
    string Body,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Series,
    string ThumbnailUrl,
    IReadOnlyList<string> ChangedFields,
    IReadOnlyList<PostRevisionFieldDiff> Diffs);

public sealed record PostRevisionFieldDiff(
    string Field,
    string Label,
    IReadOnlyList<PostRevisionDiffLine> Lines);

public sealed record PostRevisionDiffLine(string Kind, string Text);

public sealed record AuthorsRequest(IReadOnlyList<string> Authors);

public sealed record UserNamesRequest(IReadOnlyList<string> UserNames);

public sealed record AdminUserNameUpdateRequest(string UserName);

public sealed record AdminUserUsageResponse(
    int TotalUsers,
    int LlmWikiUserCount,
    int TotalLlmWikiEntries,
    int TotalLlmWikiActivityCount,
    int Recent7DayLlmWikiActivityCount,
    int Recent30DayLlmWikiActivityCount,
    int ObsidianSyncUserCount,
    int TotalObsidianVaults,
    int TotalObsidianFiles,
    int TotalObsidianActiveFiles,
    int TotalObsidianDeletedFiles,
    int TotalObsidianClients,
    long TotalObsidianSizeBytes,
    long ObsidianPerAccountStorageLimitBytes,
    long ObsidianTotalStorageCapacityBytes,
    long ObsidianTotalStorageRemainingBytes,
    int ObsidianTotalStorageUsagePercent,
    bool ObsidianTotalStorageCapacityConfigured,
    long ObsidianPhysicalStorageRemainingBytes,
    AdminLlmWikiMcpQualitySummary LlmWikiMcpQuality,
    IReadOnlyList<AdminUserUsageSummary> Users);

public sealed record AdminObsidianStorageSettingsUpdateRequest(long TotalCapacityBytes);

public sealed record AdminObsidianStorageSettingsResponse(
    long PerAccountStorageLimitBytes,
    long TotalCapacityBytes,
    long TotalUsedBytes,
    long TotalRemainingBytes,
    int TotalUsagePercent,
    bool TotalCapacityConfigured,
    long PhysicalStorageRemainingBytes);

public sealed record AdminLlmWikiMcpQualitySummary(
    DateTime WindowStartedAt,
    int CallCount,
    int Recent7DayCallCount,
    int SearchRecallCallCount,
    int SearchRecallSuccessRatePercent,
    int SearchRecallEmptyResultRatePercent,
    int SearchRecallRepeatQueryRatePercent,
    int SearchRecallAverageElapsedMs,
    int SearchRecallP95ElapsedMs,
    int SearchRecallSlowCallCount,
    int MutationCallCount,
    int MutationSharePercent,
    DateTime? LastCallAt,
    IReadOnlyList<AdminLlmWikiMcpToolUsageSummary> Tools);

public sealed record AdminLlmWikiMcpToolUsageSummary(
    string ToolName,
    int CallCount,
    int Recent7DayCallCount,
    int SuccessRatePercent,
    int EmptyResultCount,
    int AverageElapsedMs,
    int P95ElapsedMs,
    int SlowCallCount,
    DateTime? LastCallAt);

public sealed record AdminUserUsageSummary(
    string UserName,
    string DisplayName,
    string Email,
    DateTime RegisteredAt,
    DateTime? ProfileUpdatedAt,
    int PostCount,
    int PublishedPostCount,
    int DraftPostCount,
    bool UsesLlmWiki,
    int LlmWikiEntryCount,
    int LlmWikiSourceRecordCount,
    int LlmWikiActivityCount,
    int LlmWikiRecent7DayActivityCount,
    int LlmWikiRecent30DayActivityCount,
    int LlmWikiRememberCount,
    int LlmWikiMergeCount,
    int LlmWikiUpdateCount,
    int LlmWikiAccessCount,
    int ActiveMcpTokenCount,
    int RevokedMcpTokenCount,
    bool UsesObsidianSync,
    int ObsidianVaultCount,
    int ObsidianFileCount,
    int ObsidianActiveFileCount,
    int ObsidianDeletedFileCount,
    int ObsidianClientCount,
    long ObsidianTotalSizeBytes,
    long ObsidianCurrentVersionTotal,
    long ObsidianStorageLimitBytes,
    long ObsidianStorageRemainingBytes,
    int ObsidianStorageUsagePercent,
    DateTime? LastLlmWikiActivityAt,
    DateTime? LastLlmWikiEntryUpdatedAt,
    DateTime? LastLlmWikiAccessedAt,
    DateTime? LastMcpTokenUsedAt,
    DateTime? LastObsidianVaultUpdatedAt,
    DateTime? LastObsidianClientSeenAt);

public sealed record CommentRequest(string Content, Guid? ParentCommentId);

public sealed record EmptyRequest();

public sealed record LogoutResponse(bool LoggedOut);

public sealed record ActionStateResponse(bool Active);

public sealed record UpdateStateResponse(bool Updated);

public sealed record EditorImageResponse(string Url, string AltText);

public sealed record ObsidianVaultCreateRequest(string Name);

public sealed record ObsidianVaultDeleteRequest(string Name);

public sealed record ObsidianVaultResponse(
    Guid Id,
    string Name,
    long CurrentVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ObsidianVaultFileListResponse(
    Guid VaultId,
    long CurrentVersion,
    IReadOnlyList<ObsidianVaultFileResponse> Files,
    bool HasMore = false,
    long? NextVersionCursor = null);

public sealed record ObsidianVaultFileResponse(
    Guid Id,
    Guid VaultId,
    string Path,
    string Content,
    string ContentHash,
    string MediaType,
    long SizeBytes,
    long Version,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? DeletedAt,
    string Scope = ObsidianSyncScopes.Markdown,
    string Kind = ObsidianVaultFileKinds.Markdown,
    string Encoding = ObsidianVaultContentEncodings.Utf8,
    string? MetadataJson = null,
    string? LastClientId = null);

public sealed record ObsidianVaultFileUpsertRequest(
    string Path,
    string Content,
    long? BaseVersion = null,
    string? MediaType = null,
    string? Scope = null,
    string? Kind = null,
    string? Encoding = null,
    string? MetadataJson = null);

public sealed record ObsidianVaultFileDeleteRequest(
    string Path,
    long? BaseVersion = null,
    string? Scope = null);

public sealed record ObsidianVaultConflictResponse(string Error, ObsidianVaultFileResponse RemoteFile);

public sealed record ObsidianVaultFileBatchUpsertRequest(
    IReadOnlyList<ObsidianVaultFileUpsertRequest> Files,
    string? ClientId = null,
    string? ClientName = null,
    string? ClientKind = null);

public sealed record ObsidianVaultFileBatchDeleteRequest(
    IReadOnlyList<ObsidianVaultFileDeleteRequest> Files,
    string? ClientId = null,
    string? ClientName = null,
    string? ClientKind = null);

public sealed record ObsidianVaultFileBatchMutationResponse(
    Guid VaultId,
    long CurrentVersion,
    IReadOnlyList<ObsidianVaultFileResponse> Files,
    IReadOnlyList<ObsidianVaultConflictResponse> Conflicts);

public sealed record ObsidianVaultClientHeartbeatRequest(
    string ClientId,
    string ClientName,
    string ClientKind,
    long LastSeenVersion);

public sealed record ObsidianVaultClientResponse(
    string ClientId,
    Guid VaultId,
    string ClientName,
    string ClientKind,
    long LastSeenVersion,
    DateTime CreatedAt,
    DateTime LastSeenAt);

public sealed record ObsidianVaultStatusResponse(
    Guid VaultId,
    string Name,
    long CurrentVersion,
    int ActiveFileCount,
    int DeletedFileCount,
    long TotalSizeBytes,
    long AccountStorageLimitBytes,
    long AccountStorageUsedBytes,
    long AccountStorageRemainingBytes,
    IReadOnlyList<ObsidianVaultClientResponse> Clients);

public sealed record ObsidianVaultFileVersionResponse(
    Guid FileId,
    Guid VaultId,
    string Path,
    string ContentHash,
    string MediaType,
    long SizeBytes,
    long Version,
    bool IsDeleted,
    DateTime UpdatedAt,
    DateTime? DeletedAt,
    string Scope,
    string Kind,
    string Encoding,
    string? MetadataJson);

public sealed record ObsidianVaultFileRestoreRequest(
    string Path,
    long? BaseVersion = null);

public sealed record ObsidianVaultPostMappingRequest(
    string Path,
    string? Slug = null,
    string? Title = null,
    string? Summary = null,
    string? Tags = null,
    string? Series = null,
    string? ThumbnailUrl = null,
    bool? IsDraft = null);

public sealed record ObsidianVaultPostMappingResponse(
    string Path,
    string Slug,
    BlogPost Post);

public sealed record ObsidianVaultLlmWikiMappingRequest(
    string Path,
    string? EntryIdOrSlug = null,
    string? Title = null,
    string? Tags = null,
    string? CategoryPath = null,
    bool? IsPublic = null);

public sealed record ObsidianVaultLlmWikiMappingResponse(
    string Path,
    Guid EntryId,
    string Slug,
    string CategoryPath,
    bool IsPublic);

public static class ObsidianSyncScopes
{
    public const string Markdown = "markdown";

    public const string Attachments = "attachments";

    public const string Settings = "settings";

    public static IReadOnlyList<string> All { get; } = [Markdown, Attachments, Settings];
}

public static class ObsidianVaultFileKinds
{
    public const string Markdown = "markdown";

    public const string Attachment = "attachment";

    public const string Setting = "setting";
}

public static class ObsidianVaultContentEncodings
{
    public const string Utf8 = "utf8";

    public const string Base64 = "base64";
}

public sealed record LlmWikiRememberRequest(string Prompt, string? Content, string? Title, string? Tags, string? CategoryPath = null);

public sealed record LlmWikiUpdateRequest(string Prompt, string? Content, string? Title, string? Tags, string? CategoryPath = null);

public sealed record LlmWikiEntryResponse(
    Guid Id,
    string Slug,
    string Title,
    string Summary,
    string Content,
    string SourcePrompt,
    IReadOnlyList<string> Tags,
    string CategoryPath,
    int CategoryDepth,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastAccessedAt,
    int AccessCount,
    bool IsPublic,
    DateTime? PublishedAt,
    IReadOnlyList<LlmWikiSourceResponse> Sources);

public sealed record LlmWikiSourceResponse(
    Guid Id,
    string Action,
    string Prompt,
    string? Content,
    string? Title,
    string? Tags,
    string? CategoryPath,
    DateTime CreatedAt);

public sealed record LlmWikiSearchResult(
    Guid Id,
    string Slug,
    string Title,
    string Summary,
    IReadOnlyList<string> Tags,
    string CategoryPath,
    int CategoryDepth,
    DateTime UpdatedAt,
    int AccessCount,
    bool IsPublic,
    DateTime? PublishedAt,
    int? RelevancePercent = null);

public sealed record LlmWikiCategorySummary(string CategoryPath, int CategoryDepth, int Count, DateTime UpdatedAt);

public static class SlogsTokenScopes
{
    public const string Mcp = "mcp";

    public const string ObsidianSync = "obsidian.sync";

    public static IReadOnlyList<string> DefaultMcpScopes { get; } = [Mcp];
}

public sealed record LlmWikiTokenCreateRequest(string Name, IReadOnlyList<string>? Scopes = null);

public sealed record LlmWikiTokenResponse(Guid Id, string Name, string TokenPrefix, IReadOnlyList<string> Scopes, DateTime CreatedAt, DateTime? LastUsedAt, bool IsRevoked);

public sealed record LlmWikiTokenCreatedResponse(Guid Id, string Name, string TokenPrefix, string Token, IReadOnlyList<string> Scopes, DateTime CreatedAt);

public sealed record TagSummary(string Tag, int Count);

public sealed record AuthorSummary(string Author, int Count);

public sealed record SeriesSummary(string Series, int Count, int LikeCount = 0);

public sealed record AdjacentPostsResponse(BlogPost? Previous, BlogPost? Next);

public sealed class HomePageState
{
    public string StateKey { get; set; } = string.Empty;

    public List<BlogPost> Posts { get; set; } = [];

    public int VisiblePostCount { get; set; }

    public int CurrentPage { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    public string? DisplayedSearchMessage { get; set; }

    public string? NormalizedSort { get; set; }

    public string? NormalizedFeed { get; set; }

    public string DiscoveryMode { get; set; } = "recent";
}

public sealed class ListPageState<TItem>
{
    public string StateKey { get; set; } = string.Empty;

    public List<TItem> Items { get; set; } = [];

    public List<TItem> AllItems { get; set; } = [];

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    public string? NormalizedSort { get; set; }

    public string? Message { get; set; }

    public bool IsKnown { get; set; } = true;
}

public sealed class WriterPageState
{
    public string StateKey { get; set; } = string.Empty;

    public List<BlogPost> Posts { get; set; } = [];

    public List<BlogPost> AllAuthorPosts { get; set; } = [];

    public List<BlogPost> AllVisiblePosts { get; set; } = [];

    public List<TagSummary> TagCloud { get; set; } = [];

    public List<SeriesSummary> SeriesCloud { get; set; } = [];

    public BlogPost? FeaturedPost { get; set; }

    public AuthUser? WriterProfile { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    public int TotalLikeCount { get; set; }

    public int TotalCommentCount { get; set; }

    public int TotalViewCount { get; set; }

    public int FollowerCount { get; set; }

    public int FollowingCount { get; set; }

    public bool IsWriterKnown { get; set; }

    public bool IsFollowing { get; set; }

    public string? NormalizedSort { get; set; }
}

public sealed class WriterConnectionsPageState
{
    public string StateKey { get; set; } = string.Empty;

    public List<string> Users { get; set; } = [];

    public List<string> AllUsers { get; set; } = [];

    public List<string> FollowingByCurrent { get; set; } = [];

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    public int FollowerCount { get; set; }

    public int FollowingCount { get; set; }

    public bool IsWriterKnown { get; set; }
}

public sealed class PostDetailsPageState
{
    public string StateKey { get; set; } = string.Empty;

    public BlogPost? Post { get; set; }

    public string? LoadedPostSlug { get; set; }

    public List<BlogPost> RelatedPosts { get; set; } = [];

    public BlogPost? PreviousPost { get; set; }

    public BlogPost? NextPost { get; set; }

    public List<PostRevisionSummaryResponse> Revisions { get; set; } = [];

    public List<BlogComment> TopLevelComments { get; set; } = [];

    public List<BlogComment> AllTopLevelComments { get; set; } = [];

    public int CommentCurrentPage { get; set; } = 1;

    public int CommentTotalPages { get; set; } = 1;

    public int CommentTotalCount { get; set; }

    public string CommentSortOrder { get; set; } = "latest";

    public int AuthorFollowerCount { get; set; }

    public bool IsFollowingAuthor { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class SideMenuState
{
    public string StateKey { get; set; } = string.Empty;

    public string FeaturedTagPath { get; set; } = "/tag";

    public List<string> TrendingTags { get; set; } = [];

    public List<SeriesSummary> Serieses { get; set; } = [];
}
