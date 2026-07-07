namespace Slogs.Data;

public static class ReturnUrlNormalizer
{
    public static string NormalizeLocalPath(string? returnUrl, string fallback, string? baseUri = null)
        => TryNormalizeLocalPath(returnUrl, out var safeUrl, baseUri)
            ? safeUrl
            : fallback;

    public static bool TryNormalizeLocalPath(string? returnUrl, out string safeUrl, string? baseUri = null)
    {
        safeUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(returnUrl)
            || !Uri.TryCreate(returnUrl.Trim(), UriKind.RelativeOrAbsolute, out var parsedUrl))
        {
            return false;
        }

        if (!parsedUrl.IsAbsoluteUri)
        {
            var original = parsedUrl.OriginalString;
            if (!IsSafeRootedRelativePath(original))
            {
                return false;
            }

            safeUrl = original;
            return true;
        }

        if (string.IsNullOrWhiteSpace(baseUri)
            || !Uri.TryCreate(baseUri, UriKind.Absolute, out var parsedBaseUri)
            || !IsSameOrigin(parsedUrl, parsedBaseUri)
            || !IsSafeRootedRelativePath(parsedUrl.PathAndQuery))
        {
            return false;
        }

        safeUrl = parsedUrl.PathAndQuery;
        return true;
    }

    private static bool IsSafeRootedRelativePath(string value)
        => value.StartsWith('/', StringComparison.Ordinal)
            && !value.StartsWith("//", StringComparison.Ordinal)
            && !value.StartsWith("/\\", StringComparison.Ordinal)
            && !value.Contains('\\', StringComparison.Ordinal);

    private static bool IsSameOrigin(Uri value, Uri baseUri)
        => value.Scheme.Equals(baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && value.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase)
            && value.Port == baseUri.Port;
}
