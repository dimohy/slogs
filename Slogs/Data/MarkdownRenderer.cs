using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Slogs.Data;

public sealed record MarkdownImage(string Url, string AltText);

public static class MarkdownRenderer
{
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

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var html = new StringBuilder();
        var paragraph = new StringBuilder();
        var inList = false;
        var inCode = false;

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
            {
                return;
            }

            html.Append("<p>");
            html.Append(RenderInline(paragraph.ToString()));
            html.Append("</p>\n");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (inList)
            {
                html.Append("</ul>\n");
                inList = false;
            }
        }

        foreach (var rawLine in lines)
        {
            if (rawLine.StartsWith("```"))
            {
                FlushParagraph();
                CloseList();

                if (inCode)
                {
                    html.Append("</code></pre>\n");
                    inCode = false;
                }
                else
                {
                    html.Append("<pre><code>\n");
                    inCode = true;
                }

                continue;
            }

            if (inCode)
            {
                html.Append(WebUtility.HtmlEncode(rawLine)).Append('\n');
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                FlushParagraph();
                CloseList();
                continue;
            }

            var line = rawLine.Trim();
            if (line.StartsWith("# "))
            {
                FlushParagraph();
                CloseList();
                html.Append("<h1>").Append(RenderInline(line[2..].Trim())).Append("</h1>\n");
                continue;
            }

            if (line.StartsWith("## "))
            {
                FlushParagraph();
                CloseList();
                html.Append("<h2>").Append(RenderInline(line[3..].Trim())).Append("</h2>\n");
                continue;
            }

            if (line.StartsWith("### "))
            {
                FlushParagraph();
                CloseList();
                html.Append("<h3>").Append(RenderInline(line[4..].Trim())).Append("</h3>\n");
                continue;
            }

            if (line.StartsWith("> "))
            {
                FlushParagraph();
                CloseList();
                html.Append("<blockquote>").Append(RenderInline(line[2..].Trim())).Append("</blockquote>\n");
                continue;
            }

            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                if (!inList)
                {
                    html.Append("<ul>\n");
                    inList = true;
                }

                html.Append("<li>").Append(RenderInline(line[2..].Trim())).Append("</li>\n");
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append('\n');
            }

            paragraph.Append(line);
        }

        FlushParagraph();
        CloseList();

        if (inCode)
        {
            html.Append("</code></pre>\n");
        }

        return html.ToString();
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

    private static string RenderInline(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var encoded = WebUtility.HtmlEncode(markdown);

        var withImages = MarkdownImageRegex.Replace(
            encoded,
            match =>
            {
                var image = CreateImage(match.Groups["url"].Value, match.Groups["alt"].Value);
                if (image is null)
                {
                    return match.Value;
                }

                var url = WebUtility.HtmlEncode(image.Url);
                var alt = WebUtility.HtmlEncode(image.AltText);
                return $"<img src=\"{url}\" alt=\"{alt}\" loading=\"lazy\" decoding=\"async\" />";
            });

        var withCode = Regex.Replace(
            withImages,
            @"`([^`]+?)`",
            "<code>$1</code>",
            RegexOptions.Singleline);

        var withBold = Regex.Replace(
            withCode,
            @"\*\*(.+?)\*\*",
            "<strong>$1</strong>",
            RegexOptions.Singleline);

        var withItalic = Regex.Replace(
            withBold,
            @"\*(.+?)\*",
            "<em>$1</em>",
            RegexOptions.Singleline);

        var withLink = Regex.Replace(
            withItalic,
            @"\[(.+?)\]\((.+?)\)",
            "<a href=\"$2\" target=\"_blank\" rel=\"noopener noreferrer\">$1</a>");

        return withLink.Replace("\n", "<br />\n", StringComparison.Ordinal);
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
