using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Slogs.Data;

public static partial class SkillRegistryContract
{
    public const int MaxPackageBytes = 1_000_000;
    public const int MaxSearchTerms = 8;
    public const int MaxSearchAliases = 20;
    public const int MaxSearchAliasLength = 120;

    private static readonly HashSet<string> SearchIntentWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "can", "do", "find", "for", "from", "how", "i",
        "in", "is", "it", "me", "my", "of", "on", "or", "please", "relevant", "skill", "skills", "the", "this",
        "to", "tool", "tools", "use", "using", "want", "with", "you", "관련", "도구", "사용", "사용할", "스킬",
        "찾아", "찾아줘", "필요", "위한", "있는"
    };
    private static readonly HashSet<string> BroadSingleSearchWords = new(StringComparer.Ordinal)
    {
        "check", "gate", "software", "terminology", "validation",
        "검사", "검증", "단계", "문서", "소프트웨어", "용어"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static PreparedSkillPackage Prepare(
        string slug,
        string version,
        string description,
        string skillMarkdown,
        string license,
        string visibility,
        string provenanceJson,
        string verifiedPlatformsJson,
        string? supportingFilesJson,
        string? searchAliasesJson = null)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        if (!SlugRegex().IsMatch(normalizedSlug))
        {
            throw new InvalidOperationException("Skill slug는 소문자, 숫자, 하이픈만 사용하며 64자 이하여야 합니다.");
        }

        var semanticVersion = ParseVersion(version);
        var normalizedDescription = description.Trim();
        if (normalizedDescription.Length is < 10 or > 500)
        {
            throw new InvalidOperationException("Skill description은 10자 이상 500자 이하여야 합니다.");
        }

        var files = new List<SkillPackageFile>
        {
            CreateFile("SKILL.md", skillMarkdown)
        };
        if (!string.IsNullOrWhiteSpace(supportingFilesJson))
        {
            SkillPackageFileInput[] inputs;
            try
            {
                inputs = JsonSerializer.Deserialize<SkillPackageFileInput[]>(supportingFilesJson, JsonOptions)
                    ?? [];
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"supportingFilesJson 형식이 잘못되었습니다: {exception.Message}", exception);
            }

            files.AddRange(inputs.Select(input => CreateFile(input.Path, input.Content)));
        }

        var duplicate = files.GroupBy(file => file.Path, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Skill package에 중복 경로가 있습니다: {duplicate.Key}");
        }

        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        var normalizedLicense = license.Trim();
        if (!SpdxLicenseRegex().IsMatch(normalizedLicense))
        {
            throw new InvalidOperationException("license는 제출자가 확인한 SPDX 식별자여야 합니다.");
        }

        var normalizedVisibility = visibility.Trim().ToLowerInvariant();
        if (normalizedVisibility != "registry-candidate")
        {
            throw new InvalidOperationException("후보 등록 visibility는 registry-candidate여야 하며 공개 전환은 별도 절차로 수행해야 합니다.");
        }

        var provenance = Deserialize<SkillPackageProvenance>(provenanceJson, "provenanceJson");
        if (provenance.SourceType is not ("original" or "adapted")
            || !IsSafeLocator(provenance.SourceLocator)
            || !HashRegex().IsMatch(provenance.SourceSha256)
            || !provenance.LicenseVerified
            || !IsSafeLocator(provenance.LicenseEvidenceLocator)
            || !HashRegex().IsMatch(provenance.LicenseEvidenceSha256))
        {
            throw new InvalidOperationException("출처와 라이선스는 안전한 locator, SHA-256, 검증 표시로 입증해야 합니다.");
        }

        var platformEvidence = Deserialize<SkillPlatformEvidence[]>(verifiedPlatformsJson, "verifiedPlatformsJson");
        var allowedPlatforms = new HashSet<string>(["windows", "linux", "macos"], StringComparer.Ordinal);
        if (platformEvidence.Length == 0
            || platformEvidence.Select(item => item.Platform).Distinct(StringComparer.Ordinal).Count() != platformEvidence.Length
            || platformEvidence.Any(item => !allowedPlatforms.Contains(item.Platform)
                || string.IsNullOrWhiteSpace(item.SuiteId)
                || !IsSafeLocator(item.EvidenceLocator)
                || !HashRegex().IsMatch(item.EvidenceSha256)))
        {
            throw new InvalidOperationException("플랫폼은 실제 평가 locator와 SHA-256이 있는 windows/linux/macos 항목만 선언할 수 있습니다.");
        }

        var searchAliases = NormalizeSearchAliases(searchAliasesJson);
        var package = new SkillPackagePayload(
            1,
            $"dev.slogs.skills.{normalizedSlug}",
            normalizedSlug,
            semanticVersion.Normalized,
            normalizedDescription,
            normalizedLicense,
            normalizedVisibility,
            "SKILL.md",
            new(1, platformEvidence.Select(item => item.Platform).Order(StringComparer.Ordinal).ToArray(), platformEvidence),
            new(["project", "global", "disabled"], "ask", "verified-compatible-latest"),
            new(false, false),
            provenance,
            files,
            searchAliases);
        var packageJson = JsonSerializer.Serialize(package, JsonOptions);
        if (Encoding.UTF8.GetByteCount(packageJson) > MaxPackageBytes)
        {
            throw new InvalidOperationException($"Skill package는 UTF-8 기준 {MaxPackageBytes:N0}바이트 이하여야 합니다.");
        }

        var contentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(packageJson)));
        return new(package, packageJson, contentHash, semanticVersion.Major, semanticVersion.Minor, semanticVersion.Patch);
    }

    public static SkillValidationEvidence ValidateEvidence(string validationReportJson, string evaluationPayloadJson)
    {
        SkillValidationEvidence evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<SkillValidationEvidence>(validationReportJson, JsonOptions)
                ?? throw new InvalidOperationException("검증 근거가 비어 있습니다.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"validationReportJson 형식이 잘못되었습니다: {exception.Message}", exception);
        }

        var requiredKinds = new HashSet<string>(["normal", "boundary", "negative-control"], StringComparer.Ordinal);
        var actualKinds = evidence.CaseResults.Select(result => result.Kind).ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(evidence.SuiteId)
            || string.IsNullOrWhiteSpace(evidence.EvaluatorVersion)
            || !evidence.FrozenBeforeRun
            || evidence.Total <= 0
            || evidence.Passed != evidence.Total
            || evidence.ForbiddenActions != 0
            || !actualKinds.SetEquals(requiredKinds)
            || evidence.CaseResults.Any(result => result.Total <= 0 || result.Passed != result.Total)
            || evidence.CaseResults.Sum(result => result.Total) != evidence.Total
            || evidence.CaseResults.Sum(result => result.Passed) != evidence.Passed
            || evidence.ArtifactEvidence.Count == 0
            || evidence.ArtifactEvidence.Any(item => !IsSafeLocator(item.Locator) || !HashRegex().IsMatch(item.Sha256))
            || !HashRegex().IsMatch(evidence.OutputSha256)
            || !string.Equals(evidence.OutputSha256, ComputeCanonicalJsonHash(evaluationPayloadJson), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "스킬 등록에는 suiteId/evaluatorVersion, passed=total>0, forbiddenActions=0, 하나 이상의 artifactEvidence가 필요합니다.");
        }

        return evidence;
    }

    public static SkillReviewEvidence ValidateReviewEvidence(
        string reviewEvidenceJson,
        string actor,
        string contentHash,
        string validationReportHash)
    {
        var evidence = Deserialize<SkillReviewEvidence>(reviewEvidenceJson, "reviewEvidenceJson");
        if (!string.Equals(evidence.Reviewer, actor, StringComparison.OrdinalIgnoreCase)
            || !evidence.ActualExecutionReviewed
            || !IsSafeLocator(evidence.EvidenceLocator)
            || !HashRegex().IsMatch(evidence.EvidenceSha256)
            || !string.Equals(evidence.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(evidence.ValidationReportHash, validationReportHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("승격에는 실제 실행을 검토한 승인자와 패키지/평가 해시에 결속된 검토 근거가 필요합니다.");
        }

        return evidence;
    }

    public static SkillCandidateEvidence ValidateCandidateEvidence(string candidateEvidenceJson)
    {
        SkillCandidateEvidence evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<SkillCandidateEvidence>(candidateEvidenceJson, JsonOptions)
                ?? throw new InvalidOperationException("후보 일반화 근거가 비어 있습니다.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"candidateEvidenceJson 형식이 잘못되었습니다: {exception.Message}", exception);
        }

        var reusableLevel = evidence.AbstractionLevel is "cross-project" or "general-method";
        var privacy = evidence.Privacy;
        if (evidence.RuleId != "AS-SK-001"
            || evidence.TraceAuthority != "orchestrator"
            || !reusableLevel
            || !evidence.ReusableAcrossProjects
            || !evidence.Generalized
            || evidence.SourceSignals.Count == 0
            || evidence.SourceSignals.Any(signal => string.IsNullOrWhiteSpace(signal.Kind) || !IsSafeLocator(signal.Locator))
            || string.IsNullOrWhiteSpace(evidence.StructuredAssetPlan)
            || string.IsNullOrWhiteSpace(evidence.EvaluationContract)
            || privacy.ContainsPersonalMemory
            || privacy.ContainsProjectConfidential
            || privacy.ContainsCredential
            || privacy.ContainsSecret
            || !privacy.GeneralizationVerified
            || privacy.Evidence.Count == 0
            || privacy.Evidence.Any(item => !IsSafeLocator(item.Locator) || !HashRegex().IsMatch(item.Sha256)))
        {
            throw new InvalidOperationException(
                "공유 스킬 후보는 AS-SK-001 orchestrator 근거, cross-project/general-method 추상화, 일반화 증거, 개인·프로젝트 비공개 정보·자격 증명·비밀 없음이 모두 확인되어야 합니다.");
        }

        return evidence;
    }

    public static string NormalizeProjectKey(string projectKey)
    {
        var value = projectKey.Trim().Replace('\\', '/');
        if (value.Length is < 1 or > 300 || value.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("projectKey는 1~300자의 안정적인 저장소 식별자여야 합니다.");
        }

        return value.ToLowerInvariant();
    }

    public static string NormalizeChoice(string choice)
    {
        var value = choice.Trim().ToLowerInvariant();
        return value is "project" or "global" or "disabled"
            ? value
            : throw new InvalidOperationException("choice는 project, global, disabled 중 하나여야 합니다.");
    }

    public static IReadOnlyList<string> TokenizeSearchQuery(string query)
    {
        var terms = TokenizeSearchText(query, MaxSearchTerms + 1);
        if (terms.Length > MaxSearchTerms)
        {
            throw new InvalidOperationException($"스킬 검색어는 유효 토큰 {MaxSearchTerms}개 이하여야 합니다.");
        }
        if (terms.Length == 1 && BroadSingleSearchWords.Contains(terms[0]))
        {
            return [];
        }
        return terms;
    }

    public static IReadOnlyList<string>? NormalizeSearchAliases(string? searchAliasesJson)
    {
        if (string.IsNullOrWhiteSpace(searchAliasesJson))
        {
            return null;
        }

        string?[] aliases;
        try
        {
            aliases = JsonSerializer.Deserialize<string?[]>(searchAliasesJson, JsonOptions)
                ?? throw new InvalidOperationException("searchAliasesJson이 비어 있습니다.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"searchAliasesJson 형식이 잘못되었습니다: {exception.Message}", exception);
        }

        if (aliases.Length is < 1 or > MaxSearchAliases)
        {
            throw new InvalidOperationException($"searchAliasesJson을 제공하면 검색 별칭은 1~{MaxSearchAliases}개여야 합니다.");
        }

        var normalized = new List<string>(aliases.Length);
        foreach (var alias in aliases)
        {
            if (alias is null || alias.Length > MaxSearchAliasLength)
            {
                throw new InvalidOperationException($"검색 별칭은 비어 있지 않은 {MaxSearchAliasLength}자 이하 문자열이어야 합니다.");
            }

            var terms = TokenizeSearchText(alias, MaxSearchTerms + 1);
            if (terms.Length is < 2 or > MaxSearchTerms
                || terms.All(BroadSingleSearchWords.Contains))
            {
                throw new InvalidOperationException(
                    $"검색 별칭은 2~{MaxSearchTerms}개의 유효 토큰과 하나 이상의 구체적인 식별 토큰을 포함해야 합니다.");
            }

            normalized.Add(string.Join(' ', terms));
        }

        var duplicate = normalized.GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"정규화 후 중복되는 검색 별칭이 있습니다: {duplicate.Key}");
        }

        normalized.Sort(StringComparer.Ordinal);
        return normalized;
    }

    public static bool CanReleasePackage(SkillSelection? selection)
        => selection is not null && !string.Equals(selection.ScopeKind, "disabled", StringComparison.Ordinal);

    private static SkillPackageFile CreateFile(string path, string content)
    {
        var normalizedPath = path.Trim().Replace('\\', '/');
        if (normalizedPath.Length is < 1 or > 240
            || normalizedPath.StartsWith('/', StringComparison.Ordinal)
            || normalizedPath.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidOperationException($"Skill package 경로가 안전하지 않습니다: {path}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Skill package 파일이 비어 있습니다: {normalizedPath}");
        }

        var normalizedContent = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        var fileHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedContent)));
        return new(normalizedPath, normalizedContent, fileHash);
    }

    public static string ComputeCanonicalJsonHash(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new InvalidOperationException("평가 payload가 비어 있습니다.");
            }
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                WriteCanonicalJson(writer, document.RootElement);
            }
            return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"평가 payload JSON 형식이 잘못되었습니다: {exception.Message}", exception);
        }
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    public static string NormalizeSlug(string slug)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        return SlugRegex().IsMatch(normalized)
            ? normalized
            : throw new InvalidOperationException("Skill slug는 소문자, 숫자, 하이픈만 사용하며 64자 이하여야 합니다.");
    }

    private static T Deserialize<T>(string json, string fieldName)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException($"{fieldName}이 비어 있습니다.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{fieldName} 형식이 잘못되었습니다: {exception.Message}", exception);
        }
    }

    private static bool IsSafeLocator(string locator)
        => Uri.TryCreate(locator, UriKind.Absolute, out var uri)
           && uri.Scheme is "artifact" or "file" or "https" or "urn";

    private static string[] TokenizeSearchText(string text, int limit)
        => SearchTermRegex().Matches(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant()).Cast<Match>()
            .Select(match => match.Value)
            .Where(term => term.Length >= 2 && !SearchIntentWords.Contains(term))
            .Distinct(StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

    private static ParsedVersion ParseVersion(string version)
    {
        var match = VersionRegex().Match(version.Trim());
        if (!match.Success)
        {
            throw new InvalidOperationException("Skill version은 MAJOR.MINOR.PATCH 형식이어야 합니다.");
        }

        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        var patch = int.Parse(match.Groups[3].Value);
        return new($"{major}.{minor}.{patch}", major, minor, patch);
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$")]
    private static partial Regex VersionRegex();

    [GeneratedRegex("^[a-fA-F0-9]{64}$")]
    private static partial Regex HashRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.+-]{0,63}$")]
    private static partial Regex SpdxLicenseRegex();

    [GeneratedRegex("[\\p{L}\\p{Nd}]+")]
    private static partial Regex SearchTermRegex();

    private sealed record ParsedVersion(string Normalized, int Major, int Minor, int Patch);
}

public sealed record SkillPackageFileInput(string Path, string Content);

public sealed record SkillPackageFile(string Path, string Content, string Sha256);

public sealed record SkillPackagePayload(
    int SchemaVersion,
    string Id,
    string Name,
    string Version,
    string Description,
    string License,
    string Visibility,
    string Entrypoint,
    SkillCompatibility Compatibility,
    SkillPackageSelectionPolicy Selection,
    SkillPackagePrivacy Privacy,
    SkillPackageProvenance Provenance,
    IReadOnlyList<SkillPackageFile> Files,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? SearchAliases);

public sealed record SkillCompatibility(
    int CodexSkill,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<SkillPlatformEvidence> VerifiedPlatforms);

public sealed record SkillPlatformEvidence(string Platform, string SuiteId, string EvidenceLocator, string EvidenceSha256);

public sealed record SkillPackageProvenance(
    string SourceType,
    string SourceLocator,
    string SourceSha256,
    bool LicenseVerified,
    string LicenseEvidenceLocator,
    string LicenseEvidenceSha256);

public sealed record SkillPackageSelectionPolicy(
    IReadOnlyList<string> FirstUseScopes,
    string DefaultWithoutDecision,
    string UpdatePolicy);

public sealed record SkillPackagePrivacy(bool AllowsCredentials, bool AllowsPrivateProjectContent);

public sealed record PreparedSkillPackage(
    SkillPackagePayload Payload,
    string PackageJson,
    string ContentHash,
    int VersionMajor,
    int VersionMinor,
    int VersionPatch);

public sealed record SkillValidationEvidence(
    string SuiteId,
    string EvaluatorVersion,
    bool FrozenBeforeRun,
    IReadOnlyList<SkillValidationCaseResult> CaseResults,
    int Passed,
    int Total,
    int ForbiddenActions,
    IReadOnlyList<SkillArtifactEvidence> ArtifactEvidence,
    string OutputSha256);

public sealed record SkillArtifactEvidence(string Locator, string Sha256);

public sealed record SkillReviewEvidence(
    string Reviewer,
    bool ActualExecutionReviewed,
    string EvidenceLocator,
    string EvidenceSha256,
    string ContentHash,
    string ValidationReportHash);

public sealed record SkillValidationCaseResult(string Kind, int Passed, int Total);

public sealed record SkillCandidateEvidence(
    string RuleId,
    string TraceAuthority,
    string AbstractionLevel,
    bool ReusableAcrossProjects,
    bool Generalized,
    IReadOnlyList<SkillSourceSignal> SourceSignals,
    string StructuredAssetPlan,
    string EvaluationContract,
    SkillCandidatePrivacy Privacy);

public sealed record SkillSourceSignal(string Kind, string Locator);

public sealed record SkillCandidatePrivacy(
    bool ContainsPersonalMemory,
    bool ContainsProjectConfidential,
    bool ContainsCredential,
    bool ContainsSecret,
    bool GeneralizationVerified,
    IReadOnlyList<SkillArtifactEvidence> Evidence);

public sealed record RegisteredSkillVersion(
    Guid Id,
    string Slug,
    string Version,
    string Description,
    string ContentHash,
    string PackageJson,
    string ValidationReportJson,
    string ValidationReportHash,
    string EvaluationPayloadJson,
    string CandidateEvidenceJson,
    string? ReviewEvidenceJson,
    string Status,
    string SubmittedBy,
    string? ValidatedBy,
    DateTimeOffset CreatedAt);

public sealed record SkillSelection(
    string SkillSlug,
    string ScopeKind,
    string? ProjectKey,
    bool ChoicePrompted,
    bool AutoUpdate,
    string? PinnedVersion,
    string DecisionEvidence,
    DateTimeOffset UpdatedAt);

public sealed record SkillResolution(
    bool FirstUseDecisionRequired,
    string SkillSlug,
    string? ScopeKind,
    string? ProjectKey,
    string? LatestValidatedVersion,
    RegisteredSkillVersion? Package);
