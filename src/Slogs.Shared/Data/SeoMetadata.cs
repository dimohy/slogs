using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text;
using System.Xml.Linq;

namespace Slogs.Data;

public sealed record SitemapEntry(string Path, DateTime? LastModified = null, string ChangeFrequency = "weekly", decimal Priority = 0.5m);

public static class SeoMetadata
{
    public const string SiteName = "slogs";
    public const string SiteIconPath = "/favicon.svg";
    public const string DefaultDescription = "slogs는 개발자의 글쓰기, 태그 탐색, 슬로거 팔로우를 위한 한국어 개발 블로그 서비스입니다.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static string AbsoluteUrl(string baseUri, string? pathOrUrl)
        => CreateAbsoluteUri(baseUri, pathOrUrl).ToString();

    public static string EscapedAbsoluteUrl(string baseUri, string? pathOrUrl)
        => CreateAbsoluteUri(baseUri, pathOrUrl).AbsoluteUri;

    private static Uri CreateAbsoluteUri(string baseUri, string? pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute)
            && IsHttpUrl(absolute))
        {
            return absolute;
        }

        var baseAddress = new Uri(EnsureTrailingSlash(baseUri), UriKind.Absolute);
        var relative = string.IsNullOrWhiteSpace(pathOrUrl)
            ? string.Empty
            : pathOrUrl.TrimStart('/');
        return new Uri(baseAddress, relative);
    }

    private static bool IsHttpUrl(Uri uri)
        => string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    public static string BuildRobotsTxt(string baseUri)
    {
        var sitemapUrl = EscapedAbsoluteUrl(baseUri, "/sitemap.xml");
        var llmsUrl = EscapedAbsoluteUrl(baseUri, "/llms.txt");
        var llmsFullUrl = EscapedAbsoluteUrl(baseUri, "/llms-full.txt");
        var rssUrl = EscapedAbsoluteUrl(baseUri, "/feed.xml");
        var atomUrl = EscapedAbsoluteUrl(baseUri, "/atom.xml");
        var jsonFeedUrl = EscapedAbsoluteUrl(baseUri, "/feed.json");
        return string.Join('\n', [
            "User-agent: *",
            "Allow: /",
            "Disallow: /feed",
            "Disallow: /me",
            "Disallow: /write",
            "Disallow: /edit",
            "Disallow: /login",
            "Disallow: /register",
            $"Sitemap: {sitemapUrl}",
            $"# LLM guide: {llmsUrl}",
            $"# Full LLM Markdown export: {llmsFullUrl}",
            $"# RSS feed: {rssUrl}",
            $"# Atom feed: {atomUrl}",
            $"# JSON feed: {jsonFeedUrl}",
            string.Empty
        ]);
    }

    public static string BuildSitemapXml(string baseUri, IEnumerable<SitemapEntry> entries)
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var uniqueEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .GroupBy(entry => EscapedAbsoluteUrl(baseUri, entry.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entry => entry.Priority).First())
            .OrderByDescending(entry => entry.Priority)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal);

        var document = new XDocument(
            new XElement(ns + "urlset",
                uniqueEntries.Select(entry =>
                    new XElement(ns + "url",
                        new XElement(ns + "loc", EscapedAbsoluteUrl(baseUri, entry.Path)),
                        entry.LastModified is null
                            ? null
                            : new XElement(ns + "lastmod", FormatDate(entry.LastModified.Value)),
                        new XElement(ns + "changefreq", entry.ChangeFrequency),
                        new XElement(ns + "priority", entry.Priority.ToString("0.0", CultureInfo.InvariantCulture))))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static string WebSiteJsonLd(string baseUri)
    {
        return SerializeJsonLd(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "WebSite",
            ["name"] = SiteName,
            ["url"] = EscapedAbsoluteUrl(baseUri, "/"),
            ["description"] = DefaultDescription,
            ["image"] = EscapedAbsoluteUrl(baseUri, SiteIconPath),
            ["inLanguage"] = "ko-KR",
            ["potentialAction"] = new Dictionary<string, object?>
            {
                ["@type"] = "SearchAction",
                ["target"] = AbsoluteUrl(baseUri, "/post?q={search_term_string}"),
                ["queryInput"] = "required name=search_term_string"
            }
        });
    }

    public static string CollectionPageJsonLd(string baseUri, string path, string name, string description)
    {
        return SerializeJsonLd(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "CollectionPage",
            ["name"] = name,
            ["url"] = EscapedAbsoluteUrl(baseUri, path),
            ["description"] = description,
            ["inLanguage"] = "ko-KR",
            ["isPartOf"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebSite",
                ["name"] = SiteName,
                ["url"] = EscapedAbsoluteUrl(baseUri, "/")
            }
        });
    }

    public static string ProfilePageJsonLd(string baseUri, string path, string displayName, string description, string? imageUrl)
    {
        return SerializeJsonLd(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "ProfilePage",
            ["name"] = displayName,
            ["url"] = EscapedAbsoluteUrl(baseUri, path),
            ["description"] = description,
            ["inLanguage"] = "ko-KR",
            ["mainEntity"] = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = displayName,
                ["image"] = string.IsNullOrWhiteSpace(imageUrl) ? null : EscapedAbsoluteUrl(baseUri, imageUrl)
            }
        });
    }

    public static string BlogPostingJsonLd(string baseUri, BlogPost post, string path, string? imageUrl)
    {
        return SerializeJsonLd(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["headline"] = post.Title,
            ["description"] = post.Summary,
            ["url"] = EscapedAbsoluteUrl(baseUri, path),
            ["image"] = string.IsNullOrWhiteSpace(imageUrl) ? null : EscapedAbsoluteUrl(baseUri, imageUrl),
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = post.Author,
                ["url"] = EscapedAbsoluteUrl(baseUri, WriterPath(post.Author))
            },
            ["publisher"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = SiteName,
                ["url"] = EscapedAbsoluteUrl(baseUri, "/"),
                ["logo"] = new Dictionary<string, object?>
                {
                    ["@type"] = "ImageObject",
                    ["url"] = EscapedAbsoluteUrl(baseUri, SiteIconPath)
                }
            },
            ["datePublished"] = FormatDateTime(post.PublishedAt),
            ["dateModified"] = FormatDateTime(post.UpdatedAt),
            ["keywords"] = post.Tags,
            ["inLanguage"] = "ko-KR",
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = EscapedAbsoluteUrl(baseUri, path)
            }
        });
    }

    public static string WriterPath(string author)
        => $"/@{Uri.EscapeDataString(author)}";

    public static string PostPath(BlogPost post)
        => $"{WriterPath(post.Author)}/{Uri.EscapeDataString(post.Slug)}";

    public static string PostMarkdownPath(BlogPost post)
        => $"{PostPath(post)}.md";

    public static string BuildLlmsTxt(
        string baseUri,
        IEnumerable<BlogPost> posts,
        IEnumerable<(string Tag, int Count)> tags,
        IEnumerable<(string Series, int Count)> series,
        IEnumerable<(string Author, int Count)> authors)
    {
        var publicPosts = posts
            .Where(post => !post.IsDraft)
            .OrderByDescending(post => post.PublishedAt)
            .ThenBy(post => post.Title, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
        var tagList = tags.OrderByDescending(tag => tag.Count).ThenBy(tag => tag.Tag, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        var seriesList = series.OrderByDescending(item => item.Count).ThenBy(item => item.Series, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        var authorList = authors.OrderByDescending(author => author.Count).ThenBy(author => author.Author, StringComparer.OrdinalIgnoreCase).Take(40).ToList();

        var builder = new StringBuilder();
        builder.AppendLine("# slogs");
        builder.AppendLine();
        builder.AppendLine("> slogs is a Korean developer blogging service for Markdown posts, Slogger home pages, tag and series discovery, social reading, Slogs MCP, and LLM Wiki workflows.");
        builder.AppendLine();
        builder.AppendLine("Primary language: ko-KR.");
        builder.AppendLine($"Canonical site: {EscapedAbsoluteUrl(baseUri, "/")}");
        builder.AppendLine("Only public, published content is listed here. Authenticated pages, drafts, editor routes, and account pages are intentionally excluded.");
        builder.AppendLine();
        builder.AppendLine("## AI-readable exports");
        builder.AppendLine();
        AppendMarkdownLink(builder, "Full public Markdown export", EscapedAbsoluteUrl(baseUri, "/llms-full.txt"), "Single Markdown export containing the current public post corpus.");
        AppendMarkdownLink(builder, "Sitemap", EscapedAbsoluteUrl(baseUri, "/sitemap.xml"), "Complete public URL set for conventional crawlers.");
        AppendMarkdownLink(builder, "Robots", EscapedAbsoluteUrl(baseUri, "/robots.txt"), "Crawler access guidance.");
        AppendMarkdownLink(builder, "RSS feed", EscapedAbsoluteUrl(baseUri, "/feed.xml"), "Latest public posts in RSS format.");
        AppendMarkdownLink(builder, "Atom feed", EscapedAbsoluteUrl(baseUri, "/atom.xml"), "Latest public posts in Atom format.");
        AppendMarkdownLink(builder, "JSON feed", EscapedAbsoluteUrl(baseUri, "/feed.json"), "Latest public posts in JSON Feed format.");
        AppendMarkdownLink(builder, "Slogs MCP prompt", EscapedAbsoluteUrl(baseUri, "/prompts/slogs-mcp.md"), "Korean Agent policy prompt for connecting Slogs MCP and LLM Wiki.");
        builder.AppendLine();
        builder.AppendLine("## Core pages");
        builder.AppendLine();
        AppendMarkdownLink(builder, "Home", EscapedAbsoluteUrl(baseUri, "/"), "Latest public posts and discovery navigation.");
        AppendMarkdownLink(builder, "Recent posts", EscapedAbsoluteUrl(baseUri, "/recent"), "Newest public posts.");
        AppendMarkdownLink(builder, "Trending posts", EscapedAbsoluteUrl(baseUri, "/trending"), "Popular public posts.");
        AppendMarkdownLink(builder, "Recommended posts", EscapedAbsoluteUrl(baseUri, "/recommended"), "Recommended public posts.");
        AppendMarkdownLink(builder, "Tags", EscapedAbsoluteUrl(baseUri, "/tag"), "Public tag discovery.");
        AppendMarkdownLink(builder, "Series", EscapedAbsoluteUrl(baseUri, "/series"), "Public series discovery.");
        AppendMarkdownLink(builder, "Writers", EscapedAbsoluteUrl(baseUri, "/writer"), "Public Slogger directory.");

        if (publicPosts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Public posts");
            builder.AppendLine();
            foreach (var post in publicPosts)
            {
                var description = $"{NormalizePlainText(post.Summary, 260)} Published {FormatDate(post.PublishedAt)} by @{post.Author}.";
                if (post.Tags.Count > 0)
                {
                    description += $" Tags: {string.Join(", ", post.Tags.Select(tag => $"#{tag}"))}.";
                }

                AppendMarkdownLink(
                    builder,
                    post.Title,
                    EscapedAbsoluteUrl(baseUri, PostMarkdownPath(post)),
                    description);
            }
        }

        AppendTopicSection(builder, "Tags", tagList.Select(tag => (tag.Tag, EscapedAbsoluteUrl(baseUri, $"/tag/{Uri.EscapeDataString(tag.Tag)}"), $"{tag.Count} public posts.")));
        AppendTopicSection(builder, "Series", seriesList.Select(item => (item.Series, EscapedAbsoluteUrl(baseUri, $"/series/{Uri.EscapeDataString(item.Series)}"), $"{item.Count} public posts.")));
        AppendTopicSection(builder, "Authors", authorList.Select(author => ($"@{author.Author}", EscapedAbsoluteUrl(baseUri, WriterPath(author.Author)), $"{author.Count} public posts.")));

        return builder.ToString();
    }

    public static string BuildLlmsFullTxt(string baseUri, IEnumerable<BlogPost> posts)
    {
        var publicPosts = posts
            .Where(post => !post.IsDraft)
            .OrderByDescending(post => post.PublishedAt)
            .ThenBy(post => post.Title, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("# slogs public Markdown export");
        builder.AppendLine();
        builder.AppendLine("> Current public Markdown export for slogs. The site is primarily Korean and publishes developer-focused posts, Slogs MCP guidance, and LLM Wiki related content.");
        builder.AppendLine();
        builder.AppendLine($"Canonical site: {EscapedAbsoluteUrl(baseUri, "/")}");
        builder.AppendLine($"Source index: {EscapedAbsoluteUrl(baseUri, "/llms.txt")}");
        builder.AppendLine($"Generated: {DateTime.UtcNow:O}");
        builder.AppendLine();

        foreach (var post in publicPosts)
        {
            builder.AppendLine("---");
            builder.AppendLine();
            builder.Append(BuildPostMarkdown(baseUri, post));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string BuildRssFeedXml(string baseUri, IEnumerable<BlogPost> posts)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var publicPosts = GetPublicFeedPosts(posts, 100);
        var channelUpdatedAt = publicPosts.Count == 0
            ? DateTime.UtcNow
            : publicPosts.Max(post => NormalizeUtc(post.UpdatedAt));

        var document = new XDocument(
            new XElement("rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "atom", atom.NamespaceName),
                new XElement("channel",
                    new XElement("title", SiteName),
                    new XElement("link", EscapedAbsoluteUrl(baseUri, "/")),
                    new XElement("description", DefaultDescription),
                    new XElement("language", "ko-KR"),
                    new XElement("lastBuildDate", FormatRfc1123(channelUpdatedAt)),
                    new XElement(atom + "link",
                        new XAttribute("href", EscapedAbsoluteUrl(baseUri, "/feed.xml")),
                        new XAttribute("rel", "self"),
                        new XAttribute("type", "application/rss+xml")),
                    publicPosts.Select(post =>
                    {
                        var postUrl = EscapedAbsoluteUrl(baseUri, PostPath(post));
                        return new XElement("item",
                            new XElement("title", post.Title),
                            new XElement("link", postUrl),
                            new XElement("guid", new XAttribute("isPermaLink", "true"), postUrl),
                            new XElement("description", NormalizePlainText(post.Summary, 500)),
                            new XElement("author", $"@{post.Author}"),
                            new XElement("pubDate", FormatRfc1123(post.PublishedAt)),
                            post.Tags.Select(tag => new XElement("category", tag)));
                    }))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static string BuildAtomFeedXml(string baseUri, IEnumerable<BlogPost> posts)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var publicPosts = GetPublicFeedPosts(posts, 100);
        var feedUpdatedAt = publicPosts.Count == 0
            ? DateTime.UtcNow
            : publicPosts.Max(post => NormalizeUtc(post.UpdatedAt));

        var document = new XDocument(
            new XElement(atom + "feed",
                new XElement(atom + "id", EscapedAbsoluteUrl(baseUri, "/")),
                new XElement(atom + "title", SiteName),
                new XElement(atom + "subtitle", DefaultDescription),
                new XElement(atom + "updated", FormatDateTime(feedUpdatedAt)),
                new XElement(atom + "link",
                    new XAttribute("href", EscapedAbsoluteUrl(baseUri, "/")),
                    new XAttribute("rel", "alternate"),
                    new XAttribute("type", "text/html")),
                new XElement(atom + "link",
                    new XAttribute("href", EscapedAbsoluteUrl(baseUri, "/atom.xml")),
                    new XAttribute("rel", "self"),
                    new XAttribute("type", "application/atom+xml")),
                new XElement(atom + "generator", SiteName),
                publicPosts.Select(post =>
                {
                    var postUrl = EscapedAbsoluteUrl(baseUri, PostPath(post));
                    return new XElement(atom + "entry",
                        new XElement(atom + "id", postUrl),
                        new XElement(atom + "title", post.Title),
                        new XElement(atom + "link",
                            new XAttribute("href", postUrl),
                            new XAttribute("rel", "alternate"),
                            new XAttribute("type", "text/html")),
                        new XElement(atom + "published", FormatDateTime(post.PublishedAt)),
                        new XElement(atom + "updated", FormatDateTime(post.UpdatedAt)),
                        new XElement(atom + "author",
                            new XElement(atom + "name", post.Author),
                            new XElement(atom + "uri", EscapedAbsoluteUrl(baseUri, WriterPath(post.Author)))),
                        new XElement(atom + "summary", new XAttribute("type", "text"), NormalizePlainText(post.Summary, 500)),
                        post.Tags.Select(tag => new XElement(atom + "category", new XAttribute("term", tag))));
                })));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static string BuildJsonFeed(string baseUri, IEnumerable<BlogPost> posts)
    {
        var publicPosts = GetPublicFeedPosts(posts, 100);
        var feed = new Dictionary<string, object?>
        {
            ["version"] = "https://jsonfeed.org/version/1.1",
            ["title"] = SiteName,
            ["home_page_url"] = EscapedAbsoluteUrl(baseUri, "/"),
            ["feed_url"] = EscapedAbsoluteUrl(baseUri, "/feed.json"),
            ["description"] = DefaultDescription,
            ["language"] = "ko-KR",
            ["items"] = publicPosts.Select(post =>
            {
                var postUrl = EscapedAbsoluteUrl(baseUri, PostPath(post));
                return new Dictionary<string, object?>
                {
                    ["id"] = postUrl,
                    ["url"] = postUrl,
                    ["title"] = post.Title,
                    ["summary"] = NormalizePlainText(post.Summary, 500),
                    ["content_text"] = NormalizePlainText(post.Body, 2000),
                    ["date_published"] = FormatDateTime(post.PublishedAt),
                    ["date_modified"] = FormatDateTime(post.UpdatedAt),
                    ["image"] = string.IsNullOrWhiteSpace(post.ThumbnailUrl) ? null : EscapedAbsoluteUrl(baseUri, post.ThumbnailUrl),
                    ["authors"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["name"] = post.Author,
                            ["url"] = EscapedAbsoluteUrl(baseUri, WriterPath(post.Author))
                        }
                    },
                    ["tags"] = post.Tags
                };
            }).ToList()
        };

        return JsonSerializer.Serialize(feed, JsonOptions);
    }

    public static string BuildPostMarkdown(string baseUri, BlogPost post)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {NormalizePlainText(post.Title, 160)}");
        builder.AppendLine();
        builder.AppendLine($"- Canonical URL: {EscapedAbsoluteUrl(baseUri, PostPath(post))}");
        builder.AppendLine($"- Markdown URL: {EscapedAbsoluteUrl(baseUri, PostMarkdownPath(post))}");
        builder.AppendLine($"- Author: @{post.Author}");
        builder.AppendLine($"- Published: {FormatDate(post.PublishedAt)}");
        builder.AppendLine($"- Updated: {FormatDate(post.UpdatedAt)}");
        builder.AppendLine($"- Read time: {post.ReadTimeMinutes} minutes");
        if (post.Tags.Count > 0)
        {
            builder.AppendLine($"- Tags: {string.Join(", ", post.Tags.Select(tag => $"#{tag}"))}");
        }

        if (post.Series.Count > 0)
        {
            builder.AppendLine($"- Series: {string.Join(", ", post.Series)}");
        }

        if (!string.IsNullOrWhiteSpace(post.ThumbnailUrl))
        {
            builder.AppendLine($"- Representative image: {EscapedAbsoluteUrl(baseUri, post.ThumbnailUrl)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine(NormalizePlainText(post.Summary, 500));
        builder.AppendLine();
        builder.AppendLine("## Body");
        builder.AppendLine();
        builder.AppendLine(string.IsNullOrWhiteSpace(post.Body) ? "내용이 없습니다." : post.Body.Trim());
        builder.AppendLine();
        return builder.ToString();
    }

    public static string NormalizeDescription(string? value, int maxLength = 180)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = DefaultDescription;
        }

        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..Math.Max(0, maxLength - 1)].TrimEnd()}…";
    }

    private static string SerializeJsonLd<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string EnsureTrailingSlash(string value) => value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";

    private static string FormatDate(DateTime dateTime) => NormalizeUtc(dateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatDateTime(DateTime dateTime) => NormalizeUtc(dateTime).ToString("O", CultureInfo.InvariantCulture);

    private static string FormatRfc1123(DateTime dateTime) => NormalizeUtc(dateTime).ToString("R", CultureInfo.InvariantCulture);

    private static DateTime NormalizeUtc(DateTime dateTime)
    {
        return dateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : dateTime.ToUniversalTime();
    }

    private static List<BlogPost> GetPublicFeedPosts(IEnumerable<BlogPost> posts, int limit)
        => posts
            .Where(post => !post.IsDraft)
            .OrderByDescending(post => post.PublishedAt)
            .ThenBy(post => post.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

    private static void AppendTopicSection(StringBuilder builder, string heading, IEnumerable<(string Label, string Url, string Description)> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"## {heading}");
        builder.AppendLine();
        foreach (var item in list)
        {
            AppendMarkdownLink(builder, item.Label, item.Url, item.Description);
        }
    }

    private static void AppendMarkdownLink(StringBuilder builder, string label, string url, string description)
    {
        builder.Append("- [");
        builder.Append(EscapeMarkdownLinkLabel(NormalizePlainText(label, 120)));
        builder.Append("](");
        builder.Append(url);
        builder.Append("): ");
        builder.AppendLine(NormalizePlainText(description, 320));
    }

    private static string NormalizePlainText(string? value, int maxLength)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return $"{normalized[..Math.Max(0, maxLength - 1)].TrimEnd()}…";
    }

    private static string EscapeMarkdownLinkLabel(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
}
