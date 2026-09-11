using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Slogs.Data;

[McpServerToolType]
public sealed class OrganizationCorpusMcpTools(
    IHttpContextAccessor httpContextAccessor,
    OrganizationActorResolver actorResolver,
    KnowledgeCorpusService corpusService)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "org_wiki_corpus_status")]
    [Description("Read the active version, integrity hash, and exact counts of one organization-owned corpus using organization read permission. No personal or unrelated public corpus is consulted.")]
    public async Task<string> StatusAsync(Guid organizationId, string collectionId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(organizationId, cancellationToken);
        return JsonSerializer.Serialize(await corpusService.ReadOrganizationStatusAsync(actor, collectionId, cancellationToken), Json);
    }

    [McpServerTool(Name = "org_wiki_corpus_recall")]
    [Description("Recall evidence from one active organization-owned corpus, scoped before retrieval. Hop 1 retrieves direct chunks without pair scoring, hop 2 crosses one approved relation, and hop 3 crosses two approved relations. Returns JSON chunks with original locators, evidence paths, and measured diagnostics. No personal or unrelated public corpus is consulted.")]
    public async Task<string> RecallAsync(
        Guid organizationId, string collectionId, string query,
        [Description("Maximum seed chunks, 1 to 10. Approved graph evidence may add chunks up to three times this limit, at most 30 total.")] int limit = 3,
        [Description("1 for direct evidence; 2 for one approved relation bridge; 3 for a two-bridge evidence chain.")] int maxGraphHops = 1,
        [Description("Optional exact source chunk ID already established for the question. Must belong to this organization, collection, and active version. Do not invent an anchor.")] string? anchorChunkId = null,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(organizationId, cancellationToken);
        return JsonSerializer.Serialize(await corpusService.RecallOrganizationAsync(
            actor, collectionId, query, limit, maxGraphHops, anchorChunkId, cancellationToken), Json);
    }

    private Task<OrganizationActorContext> RequireActorAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new OrganizationAccessDeniedException("Organization authentication is required.");
        if (principal.FindFirstValue(OrganizationClaimTypes.ActorKind) == OrganizationActorKinds.Service
            && !Guid.TryParse(principal.FindFirstValue(OrganizationClaimTypes.OrganizationId), out _))
            throw new OrganizationAccessDeniedException("A service principal must be bound to an organization.");
        return actorResolver.RequireAsync(organizationId, principal, OrganizationTokenScopes.Read, cancellationToken);
    }
}
