using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace Slogs.Data;

[McpServerToolType]
public sealed class SkillRegistryMcpTools(
    IHttpContextAccessor httpContextAccessor,
    SkillRegistryService skillRegistryService,
    ExternalSkillRegistryService externalSkillRegistryService)
{
    [McpServerTool(Name = "skill_registry_register_external")]
    [Description("Register an explicitly authorized GitHub repository as the canonical source of an external skill and select it globally. Every resolution checks the tracked ref upstream before a cache may be reused.")]
    public async Task<string> RegisterExternalAsync(
        string slug,
        [Description("Canonical GitHub repository root URL.")] string sourceUrl,
        [Description("Branch or tag checked for the newest compatible commit on every resolution.")] string trackingRef,
        [Description("Repository-relative SKILL.md path.")] string entrypointPath,
        string description,
        [Description("Verified SPDX license identifier.")] string license,
        [Description("JSON array of precise multilingual trigger aliases.")] string searchAliasesJson,
        [Description("Non-sensitive evidence of the user's explicit global-selection request.")] string decisionEvidence)
    {
        var user = RequireUser();
        var result = await externalSkillRegistryService.RegisterGlobalAsync(
            user.UserName, slug, sourceUrl, trackingRef, entrypointPath, description, license,
            searchAliasesJson, decisionEvidence);
        var snapshot = result.Snapshot!;
        return $"# External Skill Registered\n\n- slug: {result.SkillSlug}\n- canonicalSource: {result.Source.SourceUrl}\n- trackingRef: {result.Source.TrackingRef}\n- resolvedRevision: {snapshot.Revision}\n- resolvedContentHash: {snapshot.ContentHash}\n- sourceChecked: true\n- scope: global\n- updatePolicy: upstream-latest-compatible-on-every-resolution\n- cachePolicy: reuse-only-after-upstream-revision-check";
    }

    [McpServerTool(Name = "skill_registry_prepare")]
    [Description("Validate and canonicalize a discovered Codex skill package before publication, and return its SHA-256 content hash. This does not store anything.")]
    public async Task<string> PrepareAsync(
        [Description("Lowercase hyphenated skill slug.")] string slug,
        [Description("Semantic version in MAJOR.MINOR.PATCH format.")] string version,
        [Description("Precise discovery description used for skill selection.")] string description,
        [Description("Complete SKILL.md content.")] string skillMarkdown,
        [Description("Submitter-verified SPDX license identifier; never inferred by the registry.")] string license,
        [Description("Must be registry-candidate. Public release is a separate authorized operation.")] string visibility,
        [Description("JSON provenance with sourceType, sourceLocator/sourceSha256, licenseVerified, and license evidence locator/hash.")] string provenanceJson,
        [Description("JSON array of actually tested platforms with suiteId and evidence locator/hash. Untested platforms must be omitted.")] string verifiedPlatformsJson,
        [Description("Optional JSON array of {path, content} supporting text files.")] string? supportingFilesJson = null,
        [Description("Optional JSON array of precise 2-8 token multilingual discovery aliases. Each alias must include a specific identifying token; aliases are canonicalized and content-hashed into the immutable package.")] string? searchAliasesJson = null)
    {
        RequireUser();
        var prepared = await skillRegistryService.PrepareAsync(
            slug, version, description, skillMarkdown, license, visibility, provenanceJson, verifiedPlatformsJson,
            supportingFilesJson, searchAliasesJson);
        return $"# Prepared Skill Package\n\n- id: {prepared.Payload.Id}\n- slug: {prepared.Payload.Name}\n- version: {prepared.Payload.Version}\n- contentHash: {prepared.ContentHash}\n- files: {prepared.Payload.Files.Count}\n\nNo package was stored. Run frozen behavioral evaluation, then call `skill_registry_submit_candidate` with this exact content hash and machine-readable generalization, privacy, and evaluation evidence.";
    }

    [McpServerTool(Name = "skill_registry_submit_candidate")]
    [Description("Atomically submit an immutable Agentic Shaping skill as a validated-candidate after server validation of cross-project/general-method abstraction, privacy-safe generalization, package hash, and frozen behavioral evidence. This does not publicly activate the skill.")]
    public async Task<string> SubmitCandidateAsync(
        string slug,
        string version,
        string description,
        string skillMarkdown,
        string license,
        string visibility,
        string provenanceJson,
        string verifiedPlatformsJson,
        string candidateEvidenceJson,
        string validationReportJson,
        [Description("Canonical evaluation output JSON. The server recomputes SHA-256 and requires validationReportJson.outputSha256 to match.")] string evaluationPayloadJson,
        string expectedContentHash,
        string? supportingFilesJson = null,
        [Description("Optional JSON array of precise 2-8 token multilingual discovery aliases. Must exactly match the aliases used by prepare because they are part of the package hash.")] string? searchAliasesJson = null)
    {
        var user = RequireUser();
        var package = await skillRegistryService.SubmitCandidateAsync(
            user.UserName, slug, version, description, skillMarkdown, license, visibility, provenanceJson, verifiedPlatformsJson,
            supportingFilesJson, candidateEvidenceJson, validationReportJson, evaluationPayloadJson, expectedContentHash,
            searchAliasesJson);
        return $"# Stored Skill Candidate\n\n- registry: slogs-skill-registry\n- status: {package.Status}\n- candidateId: {package.Id}\n- slug: {package.Slug}\n- version: {package.Version}\n- contentHash: {package.ContentHash}\n- validationReportHash: {package.ValidationReportHash}\n- submittedBy: @{package.SubmittedBy}\n\nThe candidate is stored but not active or available for resolution.";
    }

    [McpServerTool(Name = "skill_registry_validate_candidate")]
    [Description("Promote one stored validated-candidate to validated after an authorized hash-bound review. This is restricted to @dimohy; it never chooses or installs the skill for users.")]
    public async Task<string> ValidateCandidateAsync(
        Guid candidateId,
        string expectedContentHash,
        string expectedValidationReportHash,
        [Description("JSON evidence of an authorized actual-execution review, bound to the package and evaluation hashes.")] string reviewEvidenceJson)
    {
        var user = RequireUser();
        var package = await skillRegistryService.ValidateCandidateAsync(
            user.UserName, candidateId, expectedContentHash, expectedValidationReportHash, reviewEvidenceJson);
        return $"# Validated Skill Available\n\n- registry: slogs-skill-registry\n- status: {package.Status}\n- slug: {package.Slug}\n- version: {package.Version}\n- contentHash: {package.ContentHash}\n- validationReportHash: {package.ValidationReportHash}\n- validatedBy: @{package.ValidatedBy}";
    }

    [McpServerTool(Name = "skill_registry_search")]
    [Description("Search latest validated shared skills by exact slug, trigger description, or a precise content-hashed multilingual search alias. Returns metadata only; use resolve after the user's first-use scope choice.")]
    public async Task<string> SearchAsync(string query, int limit = 5)
    {
        RequireUser();
        var results = await skillRegistryService.SearchAsync(query, limit);
        var externalResults = await externalSkillRegistryService.SearchAsync(query, limit);
        return FormatSearchResults(results, externalResults);
    }

    internal static string FormatSearchResults(
        IReadOnlyList<RegisteredSkillVersion> results,
        IReadOnlyList<ExternalSkillSearchResult>? externalResults = null)
    {
        var builder = new StringBuilder("# Validated Skill Candidates\n");
        foreach (var result in results)
        {
            builder.AppendLine().AppendLine($"## {result.Slug} {result.Version}")
                .AppendLine(result.Description)
                .AppendLine($"- contentHash: {result.ContentHash}");
        }
        foreach (var result in externalResults ?? [])
        {
            builder.AppendLine().AppendLine($"## {result.Slug} (external canonical source)")
                .AppendLine(result.Description)
                .AppendLine($"- source: {result.SourceUrl}")
                .AppendLine($"- trackingRef: {result.TrackingRef}")
                .AppendLine($"- entrypoint: {result.EntrypointPath}")
                .AppendLine($"- license: {result.License}");
        }
        if (results.Count == 0 && (externalResults?.Count ?? 0) == 0)
        {
            builder.AppendLine().AppendLine("No matching validated skill was found.");
        }
        return builder.ToString();
    }

    [McpServerTool(Name = "skill_registry_choose")]
    [Description("Record the user's explicit first-use choice for a validated skill: project, global, or disabled. Project choice requires a stable projectKey. Automatic latest-version resolution is enabled by default.")]
    public async Task<string> ChooseAsync(
        string skillSlug,
        [Description("Explicit user choice: project, global, or disabled.")] string choice,
        [Description("Stable repository/project identifier. Required for project choice; optional for project-specific disabled choice.")] string? projectKey = null,
        [Description("Must be true only after the Agent presented project/global/disabled choices to the user.")] bool choicePrompted = false,
        [Description("Short non-sensitive evidence of the user's explicit choice.")] string decisionEvidence = "",
        bool autoUpdate = true,
        string? pinnedVersion = null)
    {
        var user = RequireUser();
        var selection = await skillRegistryService.ChooseAsync(
            user.UserName, skillSlug, choice, projectKey, choicePrompted, decisionEvidence, autoUpdate, pinnedVersion);
        return $"# Skill Choice Saved\n\n- skill: {selection.SkillSlug}\n- firstUseDecisionRequired: false\n- choicePrompted: {selection.ChoicePrompted.ToString().ToLowerInvariant()}\n- scopeKind: {selection.ScopeKind}\n- projectKey: {selection.ProjectKey ?? "(all projects)"}\n- autoUpdate: {selection.AutoUpdate}\n- pinnedVersion: {selection.PinnedVersion ?? "(latest validated)"}\n- decisionEvidence: {selection.DecisionEvidence}";
    }

    [McpServerTool(Name = "skill_registry_resolve")]
    [Description("Resolve the selected validated skill for a project. If this is the first use, returns only a decision-required response and the Agent must ask project/global/disabled before applying anything. Auto-update selections resolve the newest validated immutable version.")]
    public async Task<string> ResolveAsync(string skillSlug, string? projectKey = null)
    {
        var user = RequireUser();
        var external = await externalSkillRegistryService.ResolveAsync(user.UserName, skillSlug, projectKey);
        if (external is not null)
        {
            if (external.FirstUseDecisionRequired)
            {
                return $"# Skill First-Use Decision Required\n\nNo skill content was returned or applied. Ask the user to choose `project`, `global`, or `disabled` for `{external.SkillSlug}`.";
            }
            if (external.Disabled)
            {
                return $"# Skill Disabled\n\n`{external.SkillSlug}` is disabled for the selected scope. No skill content was returned or applied.";
            }
            var snapshot = external.Snapshot!;
            return $"# Resolved External Skill\n\n- slug: {external.SkillSlug}\n- canonicalSource: {external.Source.SourceUrl}\n- trackingRef: {external.Source.TrackingRef}\n- resolvedRevision: {snapshot.Revision}\n- resolvedContentHash: {snapshot.ContentHash}\n- contentOrigin: {snapshot.ContentOrigin}\n- sourceCheckedAt: {snapshot.CheckedAt:O}\n- scope: {external.ScopeKind}\n- projectKey: {external.ProjectKey ?? "(all projects)"}\n- consistency: read supporting files with `skill_registry_read_external_file` and this exact revision\n\n```markdown\n{snapshot.Content}\n```";
        }
        var resolution = await skillRegistryService.ResolveAsync(user.UserName, skillSlug, projectKey);
        if (resolution.FirstUseDecisionRequired)
        {
            return $"# Skill First-Use Decision Required\n\nNo skill content was returned or applied. Ask the user to choose `project`, `global`, or `disabled` for `{resolution.SkillSlug}`, then call `skill_registry_choose`.";
        }
        if (resolution.Package is null)
        {
            return $"# Skill Disabled\n\n`{resolution.SkillSlug}` is disabled for the selected scope. No skill content was returned or applied.";
        }

        return $"# Resolved Validated Skill\n\n- registry: slogs-skill-registry\n- status: validated\n- slug: {resolution.Package.Slug}\n- latestValidatedVersion: {resolution.LatestValidatedVersion}\n- resolvedVersion: {resolution.Package.Version}\n- resolvedContentHash: {resolution.Package.ContentHash}\n- contentReleased: true\n- scope: {resolution.ScopeKind}\n- projectKey: {resolution.ProjectKey ?? "(all projects)"}\n- registryEvidence: immutable version and SHA-256 package hash\n\n```json\n{resolution.Package.PackageJson}\n```";
    }

    [McpServerTool(Name = "skill_registry_read_external_file")]
    [Description("Read a supporting file from a registered external skill at the exact commit returned by resolve, preventing mixed-revision instructions.")]
    public async Task<string> ReadExternalFileAsync(string skillSlug, string revision, string path)
    {
        RequireUser();
        var content = await externalSkillRegistryService.ReadFileAsync(skillSlug, revision, path);
        return $"# External Skill File\n\n- skill: {skillSlug}\n- revision: {revision}\n- path: {path}\n\n```text\n{content}\n```";
    }

    private AuthUser RequireUser()
        => SlogsAuthentication.TryCreateUser(httpContextAccessor.HttpContext?.User)
            ?? throw new InvalidOperationException("Slogs MCP 인증이 필요합니다.");
}
