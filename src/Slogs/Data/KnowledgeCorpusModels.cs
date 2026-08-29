namespace Slogs.Data;

public static class KnowledgeCorpusBatchLimits
{
    public const int Documents = 100;
    public const int StructureNodes = 500;
    public const int Chunks = 20;
    public const int Entities = 500;
    public const int Relations = 500;
}

public sealed record KnowledgeCollectionInput(
    string CollectionId,
    string Version,
    string Title,
    string Domain,
    string Language,
    string License,
    string SourceUri,
    string OwnerKind,
    string OwnerKey,
    string Visibility,
    string? ScopeKey,
    bool RedistributionAllowed,
    int ExpectedChunkCount);

public sealed record KnowledgeAclGrantInput(
    string PrincipalKind,
    string PrincipalKey,
    string Permission);

public sealed record KnowledgeCorpusActor(
    string UserName,
    bool IsAdmin,
    IReadOnlyDictionary<string, string> OrganizationRoles)
{
    public IReadOnlySet<string> OrganizationKeys { get; } = OrganizationRoles.Keys.ToHashSet(StringComparer.Ordinal);

    public static KnowledgeCorpusActor User(string userName, bool isAdmin = false)
        => new(userName, isAdmin, new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed record KnowledgeDocumentInput(
    string DocumentId,
    string Title,
    string DocumentType,
    int Ordinal,
    string SourceLocator,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record KnowledgeStructureInput(
    string NodeId,
    string DocumentId,
    string? ParentNodeId,
    string NodeType,
    string Label,
    int Ordinal,
    string Locator,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record KnowledgeChunkInput(
    string ChunkId,
    string DocumentId,
    string? StructureNodeId,
    int Ordinal,
    string Text,
    string StartLocator,
    string EndLocator,
    string? PreviousChunkId,
    string? NextChunkId,
    int OverlapUnits,
    int TokenCount,
    string TokenizerId,
    IReadOnlyList<string>? SearchAliases = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record KnowledgeEntityInput(
    string EntityId,
    string EntityType,
    string CanonicalLabel,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record KnowledgeEvidenceInput(
    string SourceId,
    string Locator,
    string EvidenceType,
    IReadOnlyList<string>? ChunkIds = null);

public sealed record KnowledgeRelationInput(
    string RelationId,
    string FromNodeId,
    string RelationType,
    string ToNodeId,
    string ClaimClass,
    string ReviewStatus,
    double Confidence,
    IReadOnlyList<KnowledgeEvidenceInput> Evidence,
    string CreatedBy,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record KnowledgeCorpusIngestRequest(
    KnowledgeCollectionInput Collection,
    IReadOnlyList<KnowledgeDocumentInput> Documents,
    IReadOnlyList<KnowledgeStructureInput> StructureNodes,
    IReadOnlyList<KnowledgeChunkInput> Chunks,
    IReadOnlyList<KnowledgeEntityInput> Entities,
    IReadOnlyList<KnowledgeRelationInput> Relations,
    IReadOnlyList<KnowledgeAclGrantInput>? Acl = null,
    bool Activate = false,
    bool RefreshContentHash = true);

public sealed record KnowledgeCorpusIngestResult(
    string CollectionId,
    string Version,
    string Status,
    int DocumentCount,
    int StructureNodeCount,
    int ChunkCount,
    int EntityCount,
    int RelationCount,
    string? ContentHash);

public sealed record KnowledgeRelationRecall(
    string CollectionId,
    string Version,
    string RelationType,
    string FromNodeId,
    string ToNodeId,
    string ClaimClass,
    double Confidence,
    IReadOnlyList<KnowledgeEvidenceInput> Evidence,
    string? FromLabel = null,
    IReadOnlyList<string>? FromAliases = null,
    string? ToLabel = null,
    IReadOnlyList<string>? ToAliases = null);

public sealed record KnowledgeChunkRecall(
    string CollectionId,
    string Version,
    string Domain,
    string DocumentId,
    string DocumentTitle,
    string ChunkId,
    string Text,
    string StartLocator,
    string EndLocator,
    int RelevancePercent,
    IReadOnlyList<KnowledgeRelationRecall> Relations,
    string License = "",
    string CollectionSourceUri = "",
    string DocumentSourceLocator = "");

public sealed record KnowledgeTextUnit(
    string UnitId,
    string Locator,
    string Text,
    bool HardBoundary = false,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record KnowledgeChunkingOptions(
    int TargetTokens = 420,
    int MaxTokens = 560,
    int MinTokens = 120,
    int OverlapUnits = 1,
    string TokenizerId = "unicode-word-estimate-v1");
