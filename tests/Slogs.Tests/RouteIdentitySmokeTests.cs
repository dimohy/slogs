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
            "로그",
            "지식 로그 홈",
            "내 지식",
            "내 지식 로그",
            "비공개 기억",
            "LLM Wiki · MCP",
            "Slogs Skills",
            "내 기억 검색",
            "저장",
            "공감",
            "공개",
            "대표 태그",
            "태그",
            "슬로거 홈",
            "로그 시리즈"
        })
        {
            Assert.Contains(text, navMenu);
        }

        Assert.DoesNotContain("내 공개 로그", navMenu);
        Assert.DoesNotContain("저장 로그", navMenu);
        Assert.DoesNotContain("공감 로그", navMenu);
        Assert.DoesNotContain("추천 태그", navMenu);
        Assert.DoesNotContain("대표 태그 흐름", navMenu);
        Assert.DoesNotContain("태그 흐름", navMenu);
        Assert.DoesNotContain("슬로거 홈 흐름", navMenu);
        Assert.DoesNotContain("로그 시리즈 흐름", navMenu);

        foreach (var href in new[]
        {
            "href=\"/me\"",
            "href=\"/llm-wiki\"",
            "href=\"/skills\"",
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
            ("Profile.razor", "/me", "내 지식 로그"),
            ("MyBookmarks.razor", "/me/bookmarks", "저장한 로그"),
            ("MyLikes.razor", "/me/likes", "공감한 로그"),
            ("Settings.razor", "/me/settings", "연결"),
            ("LlmWikiSearch.razor", "/me/llm-wiki/search", "검색"),
            ("WritePost.razor", "/write", "게시전 로그 남기기"),
            ("EditPost.razor", "/edit/{Slug}", "로그 정리")
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
    public void LlmWikiGuideIsPublicAndLegacyRouteRedirects()
    {
        var guide = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWiki.razor"));
        var legacy = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWikiLegacy.razor"));

        Assert.Contains("@page \"/llm-wiki\"", guide);
        Assert.Contains("data-llm-wiki-public-guide=\"true\"", guide);
        Assert.DoesNotContain("Navigation.NavigateTo(GetLoginHref())", guide);
        Assert.Contains("@page \"/me/llm-wiki\"", legacy);
        Assert.Contains("Navigation.NavigateTo(target, replace: true)", legacy);
    }

    [Fact]
    public void SlogsSkillsGuideIsPublicAndKeepsRegistryApplicationAgentMediated()
    {
        var guide = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Skills.razor"));
        var llmWiki = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWiki.razor"));
        var styles = File.ReadAllText(FindRepoFile("src", "Slogs", "wwwroot", "app.css"));

        Assert.Contains("@page \"/skills\"", guide);
        Assert.Contains("data-slogs-skills-public-guide=\"true\"", guide);
        Assert.Contains("현재 프로젝트에만", guide);
        Assert.Contains("모든 프로젝트에서", guide);
        Assert.Contains("사용하지 않음", guide);
        Assert.Contains("후보가 바로 사용 가능한 Skill이 되지는 않습니다", guide);
        Assert.Contains("Slogs MCP가 연결되어 있으면 별도 설치 키는 필요하지 않습니다", guide);
        Assert.Contains("data-slogs-skills-catalog=\"true\"", guide);
        Assert.Contains("GetPublicSkillsAsync", guide);
        Assert.Contains("v@(skill.Version)", guide);
        Assert.DoesNotContain(">v@skill.Version<", guide);
        Assert.Contains("검증된 Skills 목록", guide);
        Assert.Contains("로그인하지 않아도 확인할 수 있습니다", guide);
        Assert.Contains("href=\"/skills#start\"", guide);
        Assert.Contains("href=\"/skills#catalog\"", guide);
        Assert.DoesNotContain("href=\"#start\"", guide);
        Assert.DoesNotContain("SkillRegistryService", guide);
        Assert.DoesNotContain("PackageJson", guide);
        Assert.Contains("slogs-product-example__cta", guide);
        Assert.Contains(".slogs-product-example__cta", styles);
        Assert.Contains("color: var(--theme-accent-dark) !important;", styles);
        Assert.DoesNotContain("font-black text-slate-500\">반복 사용", guide);
        Assert.DoesNotContain("font-black text-slate-500\">적용하지 않기", guide);
        Assert.Contains("href=\"/skills\"", llmWiki);
        Assert.Contains("data-llm-wiki-skills-bridge=\"true\"", llmWiki);

        foreach (var page in new[] { guide, llmWiki })
        {
            Assert.Contains("slogs-product-intro", page);
            Assert.Contains("slogs-product-steps", page);
            Assert.DoesNotContain("violet-", page);
            Assert.DoesNotContain("cyan-", page);
            Assert.DoesNotContain("amber-", page);
        }
    }

    [Fact]
    public void OrganizationOidcRegistrationUsesResponsiveLabeledForm()
    {
        var page = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Organizations.razor"));

        Assert.Contains("mt-5 max-w-5xl", page);
        Assert.Contains("md:grid-cols-2", page);
        Assert.Contains("md:col-span-2", page);
        Assert.Contains("sm:w-auto", page);
        Assert.Contains("클라이언트 ID", page);
        Assert.Contains("리디렉션 URI", page);
        Assert.Contains("허용 범위", page);
        Assert.DoesNotContain("bg-indigo-700", page);
    }

    [Fact]
    public void WriterHomeUsesLogNodeCardsForPublicStream()
    {
        var writerPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WriterPage.razor"));

        Assert.Contains("공개 지식 로그 홈", writerPage);
        Assert.Contains("지식 요약", writerPage);
        Assert.Contains("공개 로그 스트림", writerPage);
        Assert.Contains("<PostFlowSignals Post=\"featuredPost\"", writerPage);
        Assert.Contains("<PostLogCard @key=\"post.Id\"", writerPage);
        Assert.Contains("ShowAuthor=\"false\"", writerPage);
        Assert.Contains("DraftActionText=\"게시전 로그 정리\"", writerPage);
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
