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
        Assert.Contains("# RSS knowledge-log flow: https://slogs.dev/feed.xml", lines);
        Assert.Contains("# JSON knowledge-log flow: https://slogs.dev/feed.json", lines);
        Assert.DoesNotContain("# RSS feed: https://slogs.dev/feed.xml", lines);
        Assert.DoesNotContain("# JSON feed: https://slogs.dev/feed.json", lines);
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
        var postMarkdown = SeoMetadata.BuildPostMarkdown("https://slogs.dev/", post);
        post.ViewCount = 5;
        post.LikedBy.Add("mina");
        post.AddComment(new BlogComment { Author = "junho", Content = "검증 단서가 이어집니다." });

        var jsonLd = SeoMetadata.PublicLogNodeJsonLd("https://slogs.dev/", post, "/@devin/verified-flow-log", null);

        Assert.Contains("지식 로그 플랫폼", SeoMetadata.DefaultDescription);
        Assert.Contains("knowledge-log platform", llmsText);
        Assert.Contains("Only public shared knowledge-log nodes are exposed here.", llmsText);
        Assert.Contains("Public knowledge-log flow", llmsText);
        Assert.Contains("Public sharing node from @devin", llmsText);
        Assert.Contains("Clue recall paths", llmsText);
        Assert.Contains("Log-series recall paths", llmsText);
        Assert.Contains("Slogger home recall paths", llmsText);
        Assert.Contains("RSS knowledge-log flow", llmsText);
        Assert.Contains("Public sharing nodes in RSS format.", llmsText);
        Assert.Contains("public knowledge-log Markdown export", llmsFullText);
        Assert.Contains("- Public sharing node: https://slogs.dev/@devin/verified-flow-log", postMarkdown);
        Assert.Contains("- Markdown recall node: https://slogs.dev/@devin/verified-flow-log.md", postMarkdown);
        Assert.Contains("- Slogger home: @devin", postMarkdown);
        Assert.Contains("- Public sharing point: 2026-07-07", postMarkdown);
        Assert.Contains("- Flow refresh: 2026-07-07", postMarkdown);
        Assert.Contains("- Reading span: 2 minutes", postMarkdown);
        Assert.Contains("- Recall clues: #verification, #agent", postMarkdown);
        Assert.Contains("- Log-series flow: identity flow", postMarkdown);
        Assert.Contains("## Knowledge-Log Body", postMarkdown);
        Assert.Contains("\"@type\":\"CreativeWork\"", jsonLd);
        Assert.Contains("\"name\":\"검증 흐름을 남기는 로그\"", jsonLd);
        Assert.Contains("\"genre\":\"knowledge log\"", jsonLd);
        Assert.Contains("\"interactionType\":\"https://schema.org/CommentAction\"", jsonLd);
        Assert.Contains("\"interactionType\":\"https://schema.org/LikeAction\"", jsonLd);
        Assert.Contains("\"interactionType\":\"https://schema.org/ViewAction\"", jsonLd);
        Assert.Contains("\"userInteractionCount\":5", jsonLd);
        Assert.DoesNotContain("개발 블로그 서비스", SeoMetadata.DefaultDescription);
        Assert.DoesNotContain("developer blogging service", llmsText);
        Assert.DoesNotContain("Public posts", llmsText);
        Assert.DoesNotContain("Public Slogger directory", llmsText);
        Assert.DoesNotContain("public logs.", llmsText);
        Assert.DoesNotContain("RSS feed", llmsText);
        Assert.DoesNotContain("Latest public logs", llmsText);
        Assert.DoesNotContain("- Canonical URL:", postMarkdown);
        Assert.DoesNotContain("- Markdown URL:", postMarkdown);
        Assert.DoesNotContain("- Shared:", postMarkdown);
        Assert.DoesNotContain("- Updated:", postMarkdown);
        Assert.DoesNotContain("- Read time:", postMarkdown);
        Assert.DoesNotContain("- Clues:", postMarkdown);
        Assert.DoesNotContain("- Log series:", postMarkdown);
        Assert.DoesNotContain("## Log Body", postMarkdown);
        Assert.DoesNotContain("\"@type\":\"Article\"", jsonLd);
        Assert.DoesNotContain("BlogPosting", jsonLd);
    }

    [Fact]
    public void PublicFeedsUseKnowledgeLogFlowMetadata()
    {
        var post = new BlogPost
        {
            Title = "공개 공유 흐름 로그",
            Slug = "public-sharing-flow-log",
            Author = "devin",
            Summary = "공개 공유된 지식 로그 노드가 이어집니다.",
            Body = "작업과 검증 흐름입니다.",
            Tags = ["flow"],
            PublishedAt = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 8, 1, 0, 0, DateTimeKind.Utc),
            ReadTimeMinutes = 1
        };

        var rss = SeoMetadata.BuildRssFeedXml("https://slogs.dev/", [post]);
        var atom = SeoMetadata.BuildAtomFeedXml("https://slogs.dev/", [post]);
        var jsonFeed = SeoMetadata.BuildJsonFeed("https://slogs.dev/", [post]);

        foreach (var feedText in new[] { rss, atom, jsonFeed })
        {
            Assert.Contains(SeoMetadata.PublicFeedTitle, feedText);
            Assert.Contains(SeoMetadata.PublicFeedDescription, feedText);
            Assert.DoesNotContain("<title>slogs</title>", feedText);
            Assert.DoesNotContain("\"title\":\"slogs\"", feedText);
        }
    }

    [Fact]
    public void WebSiteJsonLdFramesSearchAsMeaningRecallFlow()
    {
        var jsonLd = SeoMetadata.WebSiteJsonLd("https://slogs.dev/");

        Assert.Contains("\"@type\":\"SearchAction\"", jsonLd);
        Assert.Contains("\"name\":\"Meaning recall\"", jsonLd);
        Assert.Contains("\"description\":\"Recall public knowledge-log flow by meaning clue.\"", jsonLd);
        Assert.Contains("\"target\":\"https://slogs.dev/?q={search_term_string}\"", jsonLd);
        Assert.DoesNotContain("\"target\":\"https://slogs.dev/post?q={search_term_string}\"", jsonLd);
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

    [Fact]
    public void SloggerHomeJsonLdExposesPublicKnowledgeLogFlow()
    {
        var publicPost = new BlogPost
        {
            Title = "슬로거 홈에서 이어지는 작업 로그",
            Slug = "slogger-home-flow-log",
            Author = "devin",
            Summary = "슬로거 홈의 공개 지식 흐름을 보여 주는 로그입니다.",
            Body = "작업과 판단이 이어집니다.",
            Tags = ["slogger", "flow"],
            Series = ["home flow"],
            PublishedAt = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 8, 1, 0, 0, DateTimeKind.Utc),
            ReadTimeMinutes = 2,
            ViewCount = 3
        };
        publicPost.LikedBy.Add("mina");
        publicPost.AddComment(new BlogComment { Author = "junho", Content = "흐름이 이어집니다." });

        var draftPost = new BlogPost
        {
            Title = "공개되지 않은 게시전 로그",
            Slug = "private-draft-log",
            Author = "devin",
            Summary = "공개 구조화 데이터에 들어가면 안 됩니다.",
            Body = "draft",
            IsDraft = true
        };

        var jsonLd = SeoMetadata.SloggerHomeJsonLd(
            "https://slogs.dev/",
            "/@devin",
            "Devin",
            "Devin의 slogs 공개 지식 로그 홈입니다.",
            "/uploads/devin.png",
            "devin",
            [publicPost, draftPost]);

        Assert.Contains("\"@type\":\"ProfilePage\"", jsonLd);
        Assert.Contains("\"@type\":\"Person\"", jsonLd);
        Assert.Contains("\"@type\":\"ItemList\"", jsonLd);
        Assert.Contains("\"@id\":\"https://slogs.dev/@devin#public-log-flow\"", jsonLd);
        Assert.Contains("\"name\":\"Devin 지식 로그 홈\"", jsonLd);
        Assert.Contains("\"name\":\"Devin 공개 지식 로그 흐름\"", jsonLd);
        Assert.Contains("\"alternateName\":\"@devin\"", jsonLd);
        Assert.Contains("\"@type\":\"CreativeWork\"", jsonLd);
        Assert.Contains("\"genre\":\"knowledge log\"", jsonLd);
        Assert.Contains("\"name\":\"슬로거 홈에서 이어지는 작업 로그\"", jsonLd);
        Assert.Contains("\"userInteractionCount\":3", jsonLd);
        Assert.DoesNotContain("공개되지 않은 게시전 로그", jsonLd);
        Assert.DoesNotContain("BlogPosting", jsonLd);
    }
}
