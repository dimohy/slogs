using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class RouteIdentitySmokeTests
{
    [Fact]
    public void PrimaryNavigationConnectsKnowledgeLogSurfaces()
    {
        var navMenu = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Layout", "NavMenu.razor"));

        foreach (var text in new[]
        {
            "로그 흐름",
            "지식 로그 홈",
            "내 지식 흐름",
            "내 지식 로그",
            "비공개 기억",
            "기억 연결 가이드",
            "의미 회상",
            "저장 회상",
            "공감 신호",
            "공개 흐름",
            "추천 단서",
            "전체 단서",
            "슬로거",
            "로그 시리즈"
        })
        {
            Assert.Contains(text, navMenu);
        }

        Assert.DoesNotContain("내 공개 로그", navMenu);
        Assert.DoesNotContain("저장 로그", navMenu);
        Assert.DoesNotContain("공감 로그", navMenu);

        foreach (var href in new[]
        {
            "href=\"/me\"",
            "href=\"/me/llm-wiki\"",
            "href=\"/me/llm-wiki/search\"",
            "href=\"/me/bookmarks\"",
            "href=\"/me/likes\"",
            "href=\"/tag\"",
            "href=\"/writer\"",
            "href=\"/series\""
        })
        {
            Assert.Contains(href, navMenu);
        }

        Assert.DoesNotContain("블로그", navMenu);
        Assert.DoesNotContain("글 관리", navMenu);
        Assert.DoesNotContain("글 목록", navMenu);
    }

    [Fact]
    public void PublicDiscoveryRoutesIncludeFeedAuthRedirect()
    {
        var home = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Home.razor"));

        foreach (var route in new[] { "/", "/recent", "/trending", "/recommended", "/feed" })
        {
            Assert.Contains($"@page \"{route}\"", home);
        }

        Assert.Contains("return \"feed\";", home);
        Assert.Contains("NavigateToLogin(GetCurrentPathWithQuery())", home);
        Assert.Contains("GetHomeQuery(sort: normalizedSort, feed: \"following\", query: Query)", home);
    }

    [Fact]
    public void PrivateKnowledgeLogRoutesPreserveLoginReturnUrls()
    {
        var routes = new[]
        {
            ("Profile.razor", "/me", "내 지식 로그 흐름"),
            ("MyBookmarks.razor", "/me/bookmarks", "저장 회상 흐름"),
            ("MyLikes.razor", "/me/likes", "공감 신호 흐름"),
            ("Settings.razor", "/me/settings", "연결"),
            ("LlmWiki.razor", "/me/llm-wiki", "LLM Wiki 기억 연결"),
            ("LlmWikiSearch.razor", "/me/llm-wiki/search", "의미 회상"),
            ("WritePost.razor", "/write", "새 로그 남기기"),
            ("EditPost.razor", "/edit/{Slug}", "로그 수정")
        };

        foreach (var (fileName, route, identityText) in routes)
        {
            var page = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", fileName));

            Assert.Contains($"@page \"{route}\"", page);
            Assert.Contains(identityText, page);
            Assert.Contains("GetLoginHref", page);
            Assert.Contains("returnUrl", page);
            Assert.Contains("Navigation.NavigateTo(GetLoginHref())", page);
        }
    }

    [Fact]
    public void WriterHomeUsesLogNodeCardsForPublicStream()
    {
        var writerPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WriterPage.razor"));

        Assert.Contains("공개 지식 로그 홈", writerPage);
        Assert.Contains("지식 흐름 요약", writerPage);
        Assert.Contains("공개 로그 스트림", writerPage);
        Assert.Contains("<PostFlowSignals Post=\"featuredPost\"", writerPage);
        Assert.Contains("<PostLogCard @key=\"post.Id\"", writerPage);
        Assert.Contains("ShowAuthor=\"false\"", writerPage);
        Assert.Contains("DraftActionText=\"게시전 로그 수정\"", writerPage);
        Assert.DoesNotContain("public knowledge-log home", writerPage);
        Assert.DoesNotContain("aria-label=\"@GetPostCardAriaLabel(post)\"", writerPage);
        Assert.DoesNotContain("<PostActionBar Post=\"post\"", writerPage);
    }

    [Theory]
    [InlineData("/me", "/me")]
    [InlineData("/me/llm-wiki/search?categoryPath=slogs%2Fproduct", "/me/llm-wiki/search?categoryPath=slogs%2Fproduct")]
    [InlineData("https://localhost:5117/me/settings?view=connection", "/me/settings?view=connection")]
    public void ReturnUrlNormalizerAcceptsOnlyLocalKnowledgeLogPaths(string returnUrl, string expected)
    {
        var normalized = ReturnUrlNormalizer.NormalizeLocalPath(returnUrl, "/me", "https://localhost:5117/");

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/me")]
    [InlineData("http://localhost:5117/me")]
    [InlineData("https://localhost:5999/me")]
    [InlineData("//example.com/me")]
    [InlineData("/\\example.com/me")]
    [InlineData("me")]
    public void ReturnUrlNormalizerRejectsExternalOrAmbiguousTargets(string? returnUrl)
    {
        var normalized = ReturnUrlNormalizer.NormalizeLocalPath(returnUrl, "/me", "https://localhost:5117/");

        Assert.Equal("/me", normalized);
    }

    [Fact]
    public void AuthSurfacesUseSharedReturnUrlNormalization()
    {
        var serverEndpoints = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "SlogsApiEndpoints.cs"));
        var login = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Login.razor"));
        var register = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Register.razor"));
        var writePost = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WritePost.razor"));
        var editPost = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "EditPost.razor"));

        Assert.Contains("ReturnUrlNormalizer.NormalizeLocalPath(request.ReturnUrl, \"/me\")", serverEndpoints);

        foreach (var page in new[] { login, register, writePost, editPost })
        {
            Assert.Contains("ReturnUrlNormalizer.TryNormalizeLocalPath(returnUrl, out safeUrl, Navigation.BaseUri)", page);
        }
    }

    private static string FindRepoFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file: {Path.Combine(relativeSegments)}");
    }
}
