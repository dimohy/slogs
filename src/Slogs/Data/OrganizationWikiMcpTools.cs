using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Slogs.Data;

[McpServerToolType]
public sealed class OrganizationWikiMcpTools(
    IHttpContextAccessor httpContextAccessor,
    OrganizationWikiService wikiService,
    OrganizationMetricsService metricsService)
{
    [McpServerTool(Name = "org_wiki_search")]
    [Description("Search approved active organization-memory candidates within an explicit organization and scope. Personal llm_wiki_* memory is never searched by this tool.")]
    public async Task<string> SearchAsync(
        [Description("Organization UUID.")] Guid organizationId,
        [Description("Recall terms. Leave empty for recent approved organization memories.")] string? query = null,
        [Description("Optional slash-separated category path.")] string? categoryPath = null,
        [Description("Optional scope: team, organization, customer, or equipment. Organization baseline memories are included.")] string? scopeKind = null,
        [Description("Required scope identifier for team, customer, or equipment scope.")] string? scopeKey = null,
        [Description("Maximum results, 1 to 50.")] int limit = 10)
        => Serialize(await wikiService.SearchAsync(
            organizationId, query, categoryPath, scopeKind, scopeKey, limit, RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_recall")]
    [Description("Recall compact approved organization memory for a task. This applies organization baseline plus the requested specific scope and excludes unapproved candidates.")]
    public async Task<string> RecallAsync(
        [Description("Organization UUID.")] Guid organizationId,
        [Description("Task or question requiring organization memory.")] string query,
        [Description("Optional specific scope: team, customer, or equipment.")] string? scopeKind = null,
        [Description("Specific scope identifier.")] string? scopeKey = null,
        [Description("Maximum results, 1 to 50.")] int limit = 5)
        => Serialize(await wikiService.RecallAsync(
            organizationId, query, scopeKind, scopeKey, limit, RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_read")]
    [Description("Read one organization memory with source, lifecycle, scope, proposer, and approver context. Personal candidates remain proposer/approver-only.")]
    public async Task<string> ReadAsync(
        [Description("Organization UUID.")] Guid organizationId,
        [Description("Organization memory UUID.")] Guid memoryId)
        => Serialize(await wikiService.ReadAsync(organizationId, memoryId, RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_capture")]
    [Description("Capture a private personal draft candidate detected from a correction. It is not shared with other employees until explicitly proposed and approved.")]
    public async Task<string> CaptureAsync(
        Guid organizationId,
        string title,
        string summary,
        string content,
        string sourcePrompt,
        string categoryPath,
        [Description("Comma-separated tags.")] string? tags = null,
        string? proposalReason = null)
        => Serialize(await wikiService.CaptureAsync(
            organizationId,
            CreateDraft(title, summary, content, sourcePrompt, tags, categoryPath, OrganizationMemoryScopes.PersonalCandidate, null, proposalReason),
            RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_propose")]
    [Description("Submit a structured organization-memory candidate for approver review. Approval is never implicit.")]
    public async Task<string> ProposeAsync(
        Guid organizationId,
        string title,
        string summary,
        string content,
        string sourcePrompt,
        string categoryPath,
        string scopeKind,
        string? scopeKey = null,
        string? tags = null,
        Guid? supersedesMemoryId = null,
        string? proposalReason = null)
        => Serialize(await wikiService.ProposeAsync(
            organizationId,
            CreateDraft(title, summary, content, sourcePrompt, tags, categoryPath, scopeKind, scopeKey, proposalReason, supersedesMemoryId),
            RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_approve")]
    [Description("Approve a review-requested organization-memory candidate. Pending conflicts block approval, and the approver plus reason are audited.")]
    public async Task<string> ApproveAsync(
        Guid organizationId,
        Guid memoryId,
        string reason,
        [Description("Optional complete corrected content applied before approval.")] string? correctedContent = null)
        => Serialize(await wikiService.ApproveAsync(
            organizationId,
            memoryId,
            new(reason, correctedContent),
            RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_reject")]
    [Description("Reject a review-requested organization-memory candidate with an auditable reason.")]
    public async Task<string> RejectAsync(Guid organizationId, Guid memoryId, string reason)
        => Serialize(await wikiService.RejectAsync(
            organizationId,
            memoryId,
            new(reason),
            RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_withdraw")]
    [Description("Withdraw an organization-memory candidate. The proposer may withdraw their own candidate; approvers may withdraw within their role.")]
    public async Task<string> WithdrawAsync(Guid organizationId, Guid memoryId, string reason)
        => Serialize(await wikiService.WithdrawAsync(
            organizationId,
            memoryId,
            new(reason),
            RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_categories")]
    [Description("List categories containing approved active organization memories.")]
    public async Task<string> CategoriesAsync(Guid organizationId)
        => Serialize(await wikiService.CategoriesAsync(organizationId, RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_conflicts")]
    [Description("List organization-memory and source conflicts. Pending conflicts are validation gates and are never auto-resolved.")]
    public async Task<string> ConflictsAsync(Guid organizationId, bool pendingOnly = true)
        => Serialize(await wikiService.ListConflictsAsync(organizationId, pendingOnly, RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_sources_ingest")]
    [Description("Ingest a source collection result with URL, grade, capture time, and content hash. Unchanged hashes do not create a new candidate; changed hashes create a pending conflict.")]
    public async Task<string> IngestSourceAsync(
        Guid organizationId,
        string title,
        string sourceUri,
        string sourceKind,
        string grade,
        string contentHash,
        DateTime capturedAt,
        string? excerpt = null,
        string? failureMessage = null,
        Guid? memoryId = null)
        => Serialize(await wikiService.IngestSourceAsync(
            organizationId,
            new(title, sourceUri, sourceKind, grade, contentHash, capturedAt, excerpt, failureMessage, memoryId),
            RequirePrincipal()));

    [McpServerTool(Name = "org_wiki_metrics")]
    [Description("Return organization-level aggregate metrics with minimum-cohort suppression and parent roll-up. No employee ranking or conversation content is returned.")]
    public async Task<string> MetricsAsync(Guid organizationId, DateTime from, DateTime to)
        => Serialize(await metricsService.SummarizeAsync(organizationId, from, to, RequirePrincipal()));

    private System.Security.Claims.ClaimsPrincipal RequirePrincipal()
        => httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } principal
            ? principal
            : throw new OrganizationAccessDeniedException("Slogs organization MCP authentication is required.");

    private static OrganizationMemoryDraftRequest CreateDraft(
        string title,
        string summary,
        string content,
        string sourcePrompt,
        string? tags,
        string categoryPath,
        string scopeKind,
        string? scopeKey,
        string? proposalReason,
        Guid? supersedesMemoryId = null)
        => new(
            title,
            summary,
            content,
            sourcePrompt,
            (tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            categoryPath,
            scopeKind,
            scopeKey,
            supersedesMemoryId,
            proposalReason);

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
}
