using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Slogs.Data;

public static partial class ExternalSkillSourceContract
{
    public const int MaxEntrypointBytes = 1_000_000;

    public static ExternalSkillSourceDescriptor Normalize(
        string slug,
        string sourceUrl,
        string trackingRef,
        string entrypointPath,
        string description,
        string license,
        string searchAliasesJson)
    {
        var normalizedSlug = SkillRegistryContract.NormalizeSlug(slug);
        if (!Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("외부 스킬 원본은 query/fragment가 없는 https://github.com/{owner}/{repository} 주소여야 합니다.");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            throw new InvalidOperationException("외부 스킬 원본은 GitHub 저장소 루트 주소여야 합니다.");
        }

        var owner = segments[0];
        var repository = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];
        if (!GitHubNameRegex().IsMatch(owner) || !GitHubNameRegex().IsMatch(repository))
        {
            throw new InvalidOperationException("GitHub owner 또는 repository 이름이 안전하지 않습니다.");
        }

        var normalizedRef = trackingRef.Trim();
        if (!GitRefRegex().IsMatch(normalizedRef) || normalizedRef.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("trackingRef가 안전하지 않습니다.");
        }

        var normalizedPath = NormalizeRepositoryPath(entrypointPath);
        if (!normalizedPath.EndsWith("/SKILL.md", StringComparison.Ordinal)
            && !string.Equals(normalizedPath, "SKILL.md", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("외부 스킬 entrypoint는 SKILL.md여야 합니다.");
        }

        var normalizedDescription = description.Trim();
        if (normalizedDescription.Length is < 10 or > 500)
        {
            throw new InvalidOperationException("외부 스킬 description은 10~500자여야 합니다.");
        }
        var normalizedLicense = license.Trim();
        if (!SpdxLicenseRegex().IsMatch(normalizedLicense))
        {
            throw new InvalidOperationException("외부 스킬 license는 확인된 SPDX 식별자여야 합니다.");
        }

        var aliases = SkillRegistryContract.NormalizeSearchAliases(searchAliasesJson)
            ?? throw new InvalidOperationException("외부 스킬에는 구체적인 검색 별칭이 필요합니다.");
        var canonicalUrl = $"https://github.com/{owner}/{repository}";
        return new(normalizedSlug, canonicalUrl, owner, repository, normalizedRef, normalizedPath,
            normalizedDescription, normalizedLicense, JsonSerializer.Serialize(aliases));
    }

    public static string NormalizeRepositoryPath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        if (normalized.Length is < 1 or > 500
            || normalized.StartsWith('/', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidOperationException("외부 스킬 저장소 경로가 안전하지 않습니다.");
        }
        return normalized;
    }

    public static string ValidateEntrypoint(string expectedSlug, string markdown)
    {
        if (Encoding.UTF8.GetByteCount(markdown) > MaxEntrypointBytes)
        {
            throw new InvalidOperationException($"외부 SKILL.md는 UTF-8 기준 {MaxEntrypointBytes:N0}바이트 이하여야 합니다.");
        }
        var match = FrontmatterRegex().Match(markdown.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (!match.Success)
        {
            throw new InvalidOperationException("외부 SKILL.md에 유효한 YAML frontmatter가 없습니다.");
        }
        var name = FrontmatterValue(match.Groups[1].Value, "name");
        var description = FrontmatterValue(match.Groups[1].Value, "description");
        if (!string.Equals(name, expectedSlug, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidOperationException("외부 SKILL.md의 name/description이 등록 계약과 호환되지 않습니다.");
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));
    }

    public static void ValidateRevision(string revision)
    {
        if (!RevisionRegex().IsMatch(revision))
        {
            throw new InvalidOperationException("외부 스킬 revision은 40자리 Git commit SHA여야 합니다.");
        }
    }

    private static string? FrontmatterValue(string frontmatter, string key)
        => frontmatter.Split('\n')
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && string.Equals(parts[0].Trim(), key, StringComparison.Ordinal))
            .Select(parts => parts[1].Trim().Trim('"', '\''))
            .FirstOrDefault();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex GitHubNameRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$")]
    private static partial Regex GitRefRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.+-]{0,63}$")]
    private static partial Regex SpdxLicenseRegex();

    [GeneratedRegex("^[a-f0-9]{40}$", RegexOptions.IgnoreCase)]
    private static partial Regex RevisionRegex();

    [GeneratedRegex("\\A---\\n(.*?)\\n---(?:\\n|\\z)", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();
}

public sealed record ExternalSkillSourceDescriptor(
    string Slug,
    string SourceUrl,
    string RepositoryOwner,
    string RepositoryName,
    string TrackingRef,
    string EntrypointPath,
    string Description,
    string License,
    string SearchAliasesJson);

public sealed record ExternalSkillSource(
    ExternalSkillSourceDescriptor Descriptor,
    string RegisteredBy,
    string? LastResolvedRevision,
    string? LastResolvedContent,
    string? LastResolvedContentHash,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ExternalSkillSnapshot(
    string Revision,
    string Content,
    string ContentHash,
    string ContentOrigin,
    DateTimeOffset CheckedAt);

public sealed record ExternalSkillResolution(
    bool FirstUseDecisionRequired,
    bool Disabled,
    string SkillSlug,
    string? ScopeKind,
    string? ProjectKey,
    ExternalSkillSourceDescriptor Source,
    ExternalSkillSnapshot? Snapshot);

public sealed record ExternalSkillSearchResult(
    string Slug,
    string Description,
    string SourceUrl,
    string TrackingRef,
    string EntrypointPath,
    string License);
