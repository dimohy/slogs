using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace Slogs.Data;

public sealed record MarkdownImage(string Url, string AltText);

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static readonly Regex MarkdownImageRegex = new(
        @"!\[(?<alt>[^\]\n]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HtmlImageRegex = new(
        "<img\\b[^>]*\\bsrc\\s*=\\s*(?<quote>[\"']?)(?<url>[^\"'\\s>]+)\\k<quote>[^>]*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlAltRegex = new(
        "\\balt\\s*=\\s*(?<quote>[\"'])(?<alt>.*?)\\k<quote>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static string Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "<p class=\"text-slate-500\">내용이 없습니다.</p>";
        }

        return Markdown.ToHtml(markdown, Pipeline);
    }

    public static MarkdownImage? FindFirstImage(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var markdownMatch = MarkdownImageRegex.Match(markdown);
        var htmlMatch = HtmlImageRegex.Match(markdown);

        if (markdownMatch.Success && (!htmlMatch.Success || markdownMatch.Index <= htmlMatch.Index))
        {
            return CreateImage(markdownMatch.Groups["url"].Value, markdownMatch.Groups["alt"].Value);
        }

        if (!htmlMatch.Success)
        {
            return null;
        }

        var altMatch = HtmlAltRegex.Match(htmlMatch.Value);
        var altText = altMatch.Success ? altMatch.Groups["alt"].Value : string.Empty;
        return CreateImage(htmlMatch.Groups["url"].Value, altText);
    }

    private static MarkdownImage? CreateImage(string url, string altText)
    {
        var normalizedUrl = NormalizeImageUrl(WebUtility.HtmlDecode(url));
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return null;
        }

        return new MarkdownImage(normalizedUrl, WebUtility.HtmlDecode(altText).Trim());
    }

    private static string NormalizeImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 1000 || trimmed.Contains('\n', StringComparison.Ordinal) || trimmed.Contains('\r', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            return string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : string.Empty;
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.StartsWith("./", StringComparison.Ordinal)
            || trimmed.StartsWith("../", StringComparison.Ordinal)
            || !trimmed.Contains(':', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return string.Empty;
    }
}
