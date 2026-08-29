using System.Text.Json.Serialization;

namespace Slogs.Data;

public static class LlmWikiSemanticGraphContract
{
    public const int SchemaVersion = 1;

    public static readonly IReadOnlySet<string> EntityTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "person", "project", "product", "organization", "place", "event", "concept", "decision", "artifact", "technology"
    };

    public static readonly IReadOnlySet<string> RelationTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "alias-of", "same-as", "part-of", "depends-on", "implements", "documents", "supports",
        "contradicts", "supersedes", "refines", "caused-by", "resolves", "example-of", "precedes", "related-to"
    };

    public static readonly IReadOnlySet<string> EvidenceFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "title", "summary", "category-path", "source-prompt", "content", "raw-prompt", "raw-content"
    };
}

public sealed record LlmWikiSemanticGraphManifest(
    int SchemaVersion,
    string CorpusSha256,
    string OwnerUserName,
    string Generator,
    string GeneratorVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<LlmWikiSemanticEntity> Entities,
    IReadOnlyList<LlmWikiSemanticMention> Mentions,
    IReadOnlyList<LlmWikiSemanticRelation> Relations,
    IReadOnlyList<LlmWikiMemorySplitProposal> SplitProposals);

public sealed record LlmWikiSemanticEntity(
    string Key,
    string CanonicalName,
    string EntityType,
    string Description);

public sealed record LlmWikiSemanticMention(
    string EntityKey,
    Guid EntryId,
    Guid? SourceId,
    string EvidenceField,
    string EvidenceQuote,
    double Confidence);

public sealed record LlmWikiSemanticRelation(
    string FromEntityKey,
    string ToEntityKey,
    string RelationType,
    double Confidence,
    IReadOnlyList<LlmWikiSemanticEvidence> Evidence);

public sealed record LlmWikiSemanticEvidence(
    Guid EntryId,
    Guid? SourceId,
    string EvidenceField,
    string EvidenceQuote);

public sealed record LlmWikiMemorySplitProposal(
    Guid SourceEntryId,
    string ProposedTitle,
    string ProposedCategoryPath,
    string ProposedPrompt,
    string ProposedContent,
    string Reason,
    IReadOnlyList<LlmWikiSemanticEvidence> Evidence);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(LlmWikiSemanticGraphManifest))]
internal sealed partial class LlmWikiSemanticGraphJsonContext : JsonSerializerContext;
