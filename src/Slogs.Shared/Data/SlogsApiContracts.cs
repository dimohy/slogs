namespace Slogs.Data;

public sealed record ApiErrorResponse(string Error);

public sealed record AuthRequest(string UserName, string Password, string? ReturnUrl);

public sealed record RegisterRequest(string UserName, string DisplayName, string Password, string ConfirmPassword, string? ReturnUrl);

public sealed record ProfileUpdateRequest(string DisplayName, string? Email, string? ProfileImageUrl, string? Bio);

public sealed record AuthResponse(AuthUser User, string ReturnUrl);

public sealed record PostUpsertRequest(string Title, string Summary, string Body, string Tags, string? Series, string? ThumbnailUrl, bool? IsDraft = null);

public sealed record AuthorsRequest(IReadOnlyList<string> Authors);

public sealed record UserNamesRequest(IReadOnlyList<string> UserNames);

public sealed record CommentRequest(string Content, Guid? ParentCommentId);

public sealed record EmptyRequest();

public sealed record LogoutResponse(bool LoggedOut);

public sealed record ActionStateResponse(bool Active);

public sealed record UpdateStateResponse(bool Updated);

public sealed record EditorImageResponse(string Url, string AltText);

public sealed record LlmWikiRememberRequest(string Prompt, string? Content, string? Title, string? Tags);

public sealed record LlmWikiUpdateRequest(string Prompt, string? Content, string? Title, string? Tags);

public sealed record LlmWikiEntryResponse(
    Guid Id,
    string Slug,
    string Title,
    string Summary,
    string Content,
    string SourcePrompt,
    IReadOnlyList<string> Tags,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastAccessedAt,
    int AccessCount);

public sealed record LlmWikiSearchResult(
    Guid Id,
    string Slug,
    string Title,
    string Summary,
    IReadOnlyList<string> Tags,
    DateTime UpdatedAt,
    int AccessCount);

public sealed record LlmWikiTokenCreateRequest(string Name);

public sealed record LlmWikiTokenResponse(Guid Id, string Name, string TokenPrefix, DateTime CreatedAt, DateTime? LastUsedAt, bool IsRevoked);

public sealed record LlmWikiTokenCreatedResponse(Guid Id, string Name, string TokenPrefix, string Token, DateTime CreatedAt);

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
