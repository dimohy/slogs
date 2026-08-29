using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace Slogs.Data;

[McpServerToolType]
public sealed class KnowledgeCorpusMcpTools(
    IHttpContextAccessor httpContextAccessor,
    KnowledgeCorpusService corpusService,
    KnowledgeChunkingService chunkingService,
    KnowledgeCorpusPrincipalResolver principalResolver)
{
    [McpServerTool(Name = "llm_wiki_corpus_ingest_batch")]
    [Description("Ingest one validated batch into a versioned large knowledge corpus. The same generic contract supports books, manuals, company expertise, research collections, and domain adapters such as Bible structure.")]
    public async Task<string> IngestBatchAsync(
        [Description("Versioned collection metadata, documents, hierarchy nodes, chunks, entities, and evidence-backed relations. Set activate only on the final batch.")] KnowledgeCorpusIngestRequest request)
    {
        var user = RequireUser();
        var actor = await principalResolver.ResolveAsync(user);
        var result = await corpusService.IngestAsync(actor, request);
        return $$"""
            # LLM Wiki Knowledge Corpus Ingest

            - collectionId: {{result.CollectionId}}
            - version: {{result.Version}}
            - status: {{result.Status}}
            - documents: {{result.DocumentCount}}
            - structureNodes: {{result.StructureNodeCount}}
            - chunks: {{result.ChunkCount}}
            - entities: {{result.EntityCount}}
            - relations: {{result.RelationCount}}
            - contentHash: {{result.ContentHash}}

            A staging version is not recallable. Only an integrity-checked active version participates in LLM Wiki recall.
            """;
    }

    [McpServerTool(Name = "llm_wiki_corpus_recall")]
    [Description("Recall evidence chunks and explicit relation paths from active large knowledge collections such as books, manuals, company expertise, research corpora, and Bible domain adapters.")]
    public async Task<string> RecallAsync(
        [Description("Question or knowledge query.")] string query,
        [Description("Maximum evidence chunks, 1 to 10.")] int limit = 3,
        [Description("Maximum explicit relationship hops, 0 to 3.")] int maxGraphHops = 2)
    {
        var user = RequireUser();
        var actor = await principalResolver.ResolveAsync(user);
        var results = await corpusService.RecallAsync(actor, query, limit, maxGraphHops);
        return FormatRecall(results);
    }

    [McpServerTool(Name = "llm_wiki_corpus_chunk_preview")]
    [Description("Deterministically preview traceable chunks from natural document units before corpus ingestion. Domain adapters should provide paragraph, section, verse, or other natural units instead of raw fixed-length text.")]
    public string PreviewChunks(
        string collectionId,
        string version,
        string documentId,
        IReadOnlyList<KnowledgeTextUnit> units,
        string? structureNodeId = null,
        int targetTokens = 420,
        int maxTokens = 560,
        int minTokens = 120,
        int overlapUnits = 1)
    {
        _ = RequireUser();
        var chunks = chunkingService.CreateChunks(
            collectionId,
            version,
            documentId,
            structureNodeId,
            units,
            new KnowledgeChunkingOptions(targetTokens, maxTokens, minTokens, overlapUnits));
        var builder = new StringBuilder();
        builder.AppendLine("# LLM Wiki Knowledge Chunk Preview");
        builder.AppendLine();
        foreach (var chunk in chunks)
        {
            builder.AppendLine($"## {chunk.ChunkId}");
            builder.AppendLine();
            builder.AppendLine($"- locator: {chunk.StartLocator} .. {chunk.EndLocator}");
            builder.AppendLine($"- tokens: {chunk.TokenCount} ({chunk.TokenizerId})");
            builder.AppendLine($"- previous: {chunk.PreviousChunkId ?? "none"}");
            builder.AppendLine($"- next: {chunk.NextChunkId ?? "none"}");
            builder.AppendLine($"- overlapUnits: {chunk.OverlapUnits}");
            builder.AppendLine();
            builder.AppendLine(chunk.Text);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    internal static string FormatRecall(IReadOnlyList<KnowledgeChunkRecall> results)
    {
        if (results.Count == 0)
        {
            return "No matching active LLM Wiki knowledge corpus chunks.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("# LLM Wiki Knowledge Corpus Recall");
        builder.AppendLine();
        foreach (var result in results)
        {
            builder.AppendLine($"## {result.DocumentTitle} — {result.StartLocator}..{result.EndLocator} ({result.RelevancePercent}%)");
            builder.AppendLine();
            builder.AppendLine(result.Text);
            builder.AppendLine();
            builder.AppendLine($"- corpus: {result.CollectionId}@{result.Version}");
            builder.AppendLine($"- domain: {result.Domain}");
            builder.AppendLine($"- license: {result.License}");
            builder.AppendLine($"- collectionSource: {result.CollectionSourceUri}");
            builder.AppendLine($"- documentSource: {result.DocumentSourceLocator}");
            builder.AppendLine($"- documentId: {result.DocumentId}");
            builder.AppendLine($"- chunkId: {result.ChunkId}");
            if (result.Relations.Count > 0)
            {
                builder.AppendLine("- relationPaths:");
                foreach (var relation in result.Relations)
                {
                    builder.AppendLine($"  - {relation.FromNodeId} --{relation.RelationType}--> {relation.ToNodeId} [{relation.ClaimClass}, {relation.Confidence:0.###}; {relation.CollectionId}@{relation.Version}]");
                    foreach (var evidence in relation.Evidence)
                    {
                        builder.AppendLine($"    - evidence: {evidence.SourceId} @ {evidence.Locator} ({evidence.EvidenceType})");
                    }
                }
            }

            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private AuthUser RequireUser()
        => SlogsAuthentication.TryCreateUser(httpContextAccessor.HttpContext?.User)
            ?? throw new InvalidOperationException("Slogs MCP 인증이 필요합니다. Slogs 설정에서 MCP 토큰을 만든 뒤 Authorization: Bearer 토큰으로 연결하세요.");
}
