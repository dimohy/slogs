using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;

namespace Slogs.Data;

public sealed record SitemapEntry(string Path, DateTime? LastModified = null, string ChangeFrequency = "weekly", decimal Priority = 0.5m);

public static class SeoMetadata
{
    public const string SiteName = "slogs";
    public const string DefaultDescription = "slogs는 개발자의 글쓰기, 태그 탐색, 슬로거 팔로우를 위한 한국어 개발 블로그 서비스입니다.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static string RequestBaseUri(HttpRequest request)
    {
        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(scheme))
        {
            scheme = request.Scheme;
        }

        var host = request.Headers["X-Forwarded-Host"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(host))
        {
            host = request.Host.Value;
        }

        var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        return $"{scheme}://{host}{pathBase}/";
    }

    public static string AbsoluteUrl(string baseUri, string? pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        var baseAddress = new Uri(EnsureTrailingSlash(baseUri), UriKind.Absolute);
        var relative = string.IsNullOrWhiteSpace(pathOrUrl)
            ? string.Empty
            : pathOrUrl.TrimStart('/');
        return new Uri(baseAddress, relative).ToString();
    }

    public static string BuildRobotsTxt(string baseUri)
    {
        var sitemapUrl = AbsoluteUrl(baseUri, "/sitemap.xml");
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
            string.Empty
        ]);
    }

    public static string BuildSitemapXml(string baseUri, IEnumerable<SitemapEntry> entries)
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var uniqueEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .GroupBy(entry => AbsoluteUrl(baseUri, entry.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entry => entry.Priority).First())
            .OrderByDescending(entry => entry.Priority)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal);

        var document = new XDocument(
            new XElement(ns + "urlset",
                uniqueEntries.Select(entry =>
                    new XElement(ns + "url",
                        new XElement(ns + "loc", AbsoluteUrl(baseUri, entry.Path)),
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
            ["url"] = AbsoluteUrl(baseUri, "/"),
            ["description"] = DefaultDescription,
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
            ["url"] = AbsoluteUrl(baseUri, path),
            ["description"] = description,
            ["inLanguage"] = "ko-KR",
            ["isPartOf"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebSite",
                ["name"] = SiteName,
                ["url"] = AbsoluteUrl(baseUri, "/")
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
            ["url"] = AbsoluteUrl(baseUri, path),
            ["description"] = description,
            ["inLanguage"] = "ko-KR",
            ["mainEntity"] = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = displayName,
                ["image"] = string.IsNullOrWhiteSpace(imageUrl) ? null : AbsoluteUrl(baseUri, imageUrl)
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
            ["url"] = AbsoluteUrl(baseUri, path),
            ["image"] = string.IsNullOrWhiteSpace(imageUrl) ? null : AbsoluteUrl(baseUri, imageUrl),
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = post.Author,
                ["url"] = AbsoluteUrl(baseUri, $"/@{Uri.EscapeDataString(post.Author)}")
            },
            ["publisher"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = SiteName,
                ["url"] = AbsoluteUrl(baseUri, "/")
            },
            ["datePublished"] = FormatDateTime(post.PublishedAt),
            ["dateModified"] = FormatDateTime(post.UpdatedAt),
            ["keywords"] = post.Tags,
            ["inLanguage"] = "ko-KR",
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = AbsoluteUrl(baseUri, path)
            }
        });
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

    private static DateTime NormalizeUtc(DateTime dateTime)
    {
        return dateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : dateTime.ToUniversalTime();
    }
}
