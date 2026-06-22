using System.Globalization;
using System.Text;

namespace Slogs.Data;

public static class SlugGenerator
{
    public const int MaxLength = 220;

    public static string Suggest(string title)
        => Normalize(title);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "post";
        }

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var pendingSeparator = false;

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                AppendPendingSeparator(builder, ref pendingSeparator);
                builder.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c) || c is '-' or '_' or '.')
            {
                pendingSeparator = builder.Length > 0;
            }
        }

        var slug = TrimToMaxLength(builder.ToString().Normalize(NormalizationForm.FormC));
        return string.IsNullOrWhiteSpace(slug) ? "post" : slug;
    }

    private static void AppendPendingSeparator(StringBuilder builder, ref bool pendingSeparator)
    {
        if (!pendingSeparator)
        {
            return;
        }

        if (builder.Length > 0 && builder[^1] != '-')
        {
            builder.Append('-');
        }

        pendingSeparator = false;
    }

    private static string TrimToMaxLength(string value)
    {
        if (value.Length <= MaxLength)
        {
            return value.Trim('-');
        }

        return value[..MaxLength].Trim('-');
    }
}
