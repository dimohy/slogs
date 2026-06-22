using System.Net;
using Markdig;

namespace Slogs.Data;

public sealed record MarkdownImage(string Url, string AltText);

public static class MarkdownRenderer
{
    private static readonly Lazy<MarkdownPipeline> Pipeline = new(() => new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()
            .Build());

    public static string Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "<p class=\"text-slate-500\">내용이 없습니다.</p>";
        }

        return Markdown.ToHtml(markdown, Pipeline.Value);
    }

    public static MarkdownImage? FindFirstImage(string? markdown)
        => FindImages(markdown).FirstOrDefault();

    public static IReadOnlyList<MarkdownImage> FindImages(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        return FindMarkdownImages(markdown)
            .Concat(FindHtmlImages(markdown))
            .OrderBy(x => x.Index)
            .Select(x => x.Image)
            .ToList();
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

    public static string NormalizeImageUrl(string? value)
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

        if (trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.StartsWith("./", StringComparison.Ordinal)
            || trimmed.StartsWith("../", StringComparison.Ordinal)
            || !trimmed.Contains(':', StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            return string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : string.Empty;
        }

        return string.Empty;
    }

    private static IReadOnlyList<(int Index, MarkdownImage Image)> FindMarkdownImages(string source)
    {
        var images = new List<(int Index, MarkdownImage Image)>();
        var searchStart = 0;
        while (searchStart < source.Length)
        {
            var imageStart = source.IndexOf("![", searchStart, StringComparison.Ordinal);
            if (imageStart < 0)
            {
                return images;
            }

            var altEnd = source.IndexOf(']', imageStart + 2);
            if (altEnd < 0 || altEnd + 1 >= source.Length || source[altEnd + 1] != '(')
            {
                searchStart = imageStart + 2;
                continue;
            }

            var urlStart = altEnd + 2;
            var urlEnd = FindMarkdownImageUrlEnd(source, urlStart);
            if (urlEnd < 0)
            {
                searchStart = imageStart + 2;
                continue;
            }

            var url = source[urlStart..urlEnd].Trim();
            if (url.Length > 0 && url[0] is '"' or '\'')
            {
                url = ReadQuotedAttribute(url, 0).Value;
            }
            else
            {
                var titleStart = url.IndexOfAny([' ', '\t', '\r', '\n']);
                if (titleStart >= 0)
                {
                    url = url[..titleStart];
                }
            }

            var image = CreateImage(url, source[(imageStart + 2)..altEnd]);
            if (image is not null)
            {
                images.Add((imageStart, image));
            }

            searchStart = imageStart + 2;
        }

        return images;
    }

    private static int FindMarkdownImageUrlEnd(string source, int urlStart)
    {
        var depth = 0;
        for (var index = urlStart; index < source.Length; index++)
        {
            var current = source[index];
            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current != ')')
            {
                continue;
            }

            if (depth == 0)
            {
                return index;
            }

            depth--;
        }

        return -1;
    }

    private static IReadOnlyList<(int Index, MarkdownImage Image)> FindHtmlImages(string source)
    {
        var images = new List<(int Index, MarkdownImage Image)>();
        var searchStart = 0;
        while (searchStart < source.Length)
        {
            var tagStart = source.IndexOf("<img", searchStart, StringComparison.OrdinalIgnoreCase);
            if (tagStart < 0)
            {
                return images;
            }

            var tagEnd = source.IndexOf('>', tagStart + 4);
            if (tagEnd < 0)
            {
                return images;
            }

            var tag = source[tagStart..(tagEnd + 1)];
            var src = ReadHtmlAttribute(tag, "src");
            var image = CreateImage(src, ReadHtmlAttribute(tag, "alt"));
            if (image is not null)
            {
                images.Add((tagStart, image));
            }

            searchStart = tagEnd + 1;
        }

        return images;
    }

    private static string ReadHtmlAttribute(string tag, string attributeName)
    {
        var searchStart = 0;
        while (searchStart < tag.Length)
        {
            var index = tag.IndexOf(attributeName, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return string.Empty;
            }

            var before = index == 0 ? ' ' : tag[index - 1];
            var afterIndex = index + attributeName.Length;
            var after = afterIndex < tag.Length ? tag[afterIndex] : '\0';
            if (!IsAttributeBoundary(before) || !char.IsWhiteSpace(after) && after != '=')
            {
                searchStart = afterIndex;
                continue;
            }

            while (afterIndex < tag.Length && char.IsWhiteSpace(tag[afterIndex]))
            {
                afterIndex++;
            }

            if (afterIndex >= tag.Length || tag[afterIndex] != '=')
            {
                searchStart = afterIndex;
                continue;
            }

            afterIndex++;
            while (afterIndex < tag.Length && char.IsWhiteSpace(tag[afterIndex]))
            {
                afterIndex++;
            }

            if (afterIndex >= tag.Length)
            {
                return string.Empty;
            }

            if (tag[afterIndex] is '"' or '\'')
            {
                return ReadQuotedAttribute(tag, afterIndex).Value;
            }

            var valueEnd = afterIndex;
            while (valueEnd < tag.Length && !char.IsWhiteSpace(tag[valueEnd]) && tag[valueEnd] != '>')
            {
                valueEnd++;
            }

            return tag[afterIndex..valueEnd];
        }

        return string.Empty;
    }

    private static (string Value, int EndIndex) ReadQuotedAttribute(string source, int quoteIndex)
    {
        var quote = source[quoteIndex];
        var valueStart = quoteIndex + 1;
        var valueEnd = source.IndexOf(quote, valueStart);
        return valueEnd < 0
            ? (source[valueStart..], source.Length)
            : (source[valueStart..valueEnd], valueEnd);
    }

    private static bool IsAttributeBoundary(char value)
        => char.IsWhiteSpace(value) || value is '<' or '/' or '\0';
}
