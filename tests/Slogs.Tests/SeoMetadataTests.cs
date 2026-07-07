using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class SeoMetadataTests
{
    [Fact]
    public void BuildRobotsTxtDoesNotBlockPublicFeedFiles()
    {
        var robots = SeoMetadata.BuildRobotsTxt("https://slogs.dev/");
        var lines = robots.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.DoesNotContain("Disallow: /feed", lines);
        Assert.Contains("# RSS feed: https://slogs.dev/feed.xml", lines);
        Assert.Contains("# JSON feed: https://slogs.dev/feed.json", lines);
    }

    [Fact]
    public void PublicCrawlerMetadataUsesKnowledgeLogWording()
    {
        var post = new BlogPost
        {
            Title = "검증 흐름을 남기는 로그",
            Slug = "verified-flow-log",
            Author = "devin",
            Summary = "작업 판단과 검증 단서를 이어 남긴 공개 로그입니다.",
            Body = "검증한 결정과 다음 단서를 기록합니다.",
            Tags = ["verification", "agent"],
            Series = ["identity flow"],
            PublishedAt = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc),
            ReadTimeMinutes = 2
        };

        var llmsText = SeoMetadata.BuildLlmsTxt(
            "https://slogs.dev/",
            [post],
            [("verification", 1)],
            [("identity flow", 1)],
            [("devin", 1)]);
        var llmsFullText = SeoMetadata.BuildLlmsFullTxt("https://slogs.dev/", [post]);
        var jsonLd = SeoMetadata.ArticleJsonLd("https://slogs.dev/", post, "/@devin/verified-flow-log", null);

        Assert.Contains("지식 로그 플랫폼", SeoMetadata.DefaultDescription);
        Assert.Contains("knowledge-log platform", llmsText);
        Assert.Contains("Public logs", llmsText);
        Assert.Contains("Clues", llmsText);
        Assert.Contains("Log series", llmsText);
        Assert.Contains("public knowledge-log Markdown export", llmsFullText);
        Assert.Contains("\"@type\":\"Article\"", jsonLd);
        Assert.DoesNotContain("개발 블로그 서비스", SeoMetadata.DefaultDescription);
        Assert.DoesNotContain("developer blogging service", llmsText);
        Assert.DoesNotContain("Public posts", llmsText);
        Assert.DoesNotContain("BlogPosting", jsonLd);
    }

    [Fact]
    public void PublicLogCollectionJsonLdExposesKnowledgeLogItemList()
    {
        var post = new BlogPost
        {
            Title = "회상 가능한 작업 로그",
            Slug = "recallable-work-log",
            Author = "mina",
            Summary = "작업 판단과 다음 단서가 이어지는 공개 지식 로그입니다.",
            Body = "본문",
            Tags = ["recall", "workflow"],
            Series = ["knowledge flow"],
            PublishedAt = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 7, 1, 0, 0, DateTimeKind.Utc),
            ReadTimeMinutes = 3,
            ViewCount = 7
        };
        post.LikedBy.Add("junho");
        post.AddComment(new BlogComment { Author = "junho", Content = "좋은 단서입니다." });

        var jsonLd = SeoMetadata.PublicLogCollectionJsonLd(
            "https://slogs.dev/",
            "/tag/recall",
            "#recall 단서 흐름",
            "slogs에서 #recall 단서로 이어진 공개 로그 흐름을 회상합니다.",
            [post]);

        Assert.Contains("\"@type\":\"ItemList\"", jsonLd);
        Assert.Contains("\"@type\":\"CreativeWork\"", jsonLd);
        Assert.Contains("\"genre\":\"knowledge log\"", jsonLd);
        Assert.Contains("\"name\":\"#recall\"", jsonLd);
        Assert.Contains("\"name\":\"knowledge flow\"", jsonLd);
        Assert.Contains("\"interactionType\":\"https://schema.org/CommentAction\"", jsonLd);
        Assert.Contains("\"userInteractionCount\":7", jsonLd);
        Assert.Contains("https://slogs.dev/@mina/recallable-work-log", jsonLd);
        Assert.DoesNotContain("BlogPosting", jsonLd);
        Assert.DoesNotContain("Public posts", jsonLd);
    }
}
