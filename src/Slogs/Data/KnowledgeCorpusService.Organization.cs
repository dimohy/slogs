using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;

namespace Slogs.Data;

public sealed partial class KnowledgeCorpusService
{
    private sealed record OrganizationCorpusScope(Guid OrganizationId, string CollectionId, string Version, string? AnchorChunkId = null);
    private sealed class RecallExecution
    {
        public int PairScoreCalls { get; set; }
        public int PairScoreCandidates { get; set; }
    }

    public async Task<OrganizationCorpusStatus> ReadOrganizationStatusAsync(
        OrganizationActorContext actor, string collectionId, CancellationToken cancellationToken = default)
    {
        RequireCorpusRead(actor);
        var collection = Normalize(collectionId, 160, nameof(collectionId));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureConnectionOpenAsync(db, cancellationToken);
        await using var command = CreateCommand(db,
            """
            SELECT c."Version", c."Status", c."ContentHash",
              (SELECT COUNT(*)::integer FROM "LlmWikiKnowledgeDocuments" d
               WHERE d."CollectionId"=c."CollectionId" AND d."Version"=c."Version" AND d."OwnerUserName"=c."OwnerUserName"),
              (SELECT COUNT(*)::integer FROM "LlmWikiKnowledgeChunks" k
               WHERE k."CollectionId"=c."CollectionId" AND k."Version"=c."Version" AND k."OwnerUserName"=c."OwnerUserName"),
              (SELECT COUNT(*)::integer FROM "LlmWikiKnowledgeRelations" r
               WHERE r."CollectionId"=c."CollectionId" AND r."Version"=c."Version" AND r."OwnerUserName"=c."OwnerUserName")
            FROM "LlmWikiKnowledgeCollections" c
            WHERE c."CollectionId"=@collection AND c."OwnerKind"='organization'
              AND c."OwnerKey"=@organization AND c."Status"='active';
            """);
        command.Parameters.AddWithValue("collection", collection);
        command.Parameters.AddWithValue("organization", actor.OrganizationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new OrganizationNotFoundException("No active corpus exists in the authenticated organization and collection.");
        var status = new OrganizationCorpusStatus(actor.OrganizationId, collection,
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5));
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The organization corpus has multiple active storage identities.");
        return status;
    }

    public async Task<OrganizationCorpusRecall> RecallOrganizationAsync(
        OrganizationActorContext actor, string collectionId, string query,
        int limit = 3, int maxGraphHops = 1, string? anchorChunkId = null, CancellationToken cancellationToken = default)
    {
        RequireCorpusRead(actor);
        if (maxGraphHops is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(maxGraphHops));
        if (limit is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(limit));
        if (maxGraphHops > 1 && !embeddingService.SupportsFullFunctionReranking)
            throw new InvalidOperationException("Organization relational corpus recall requires full-function reranking.");
        var stopwatch = Stopwatch.StartNew();
        var status = await ReadOrganizationStatusAsync(actor, collectionId, cancellationToken);
        var scope = new OrganizationCorpusScope(actor.OrganizationId, status.CollectionId, status.Version,
            anchorChunkId is null ? null : Normalize(anchorChunkId, 240, nameof(anchorChunkId)));
        var execution = new RecallExecution();
        // Never promote a service actor into an administrator or use the presenter's personal identity.
        // The SQL scope is applied before exact, lexical, vector, and relation candidate selection.
        var seeds = await RecallCoreAsync(actor.ActorId, false, [actor.OrganizationId.ToString("D")],
            query, limit, maxGraphHops, true, cancellationToken, scope, execution);
        var chunks = seeds.Select(seed => seed with { GraphDepth = 1, SemanticPath = [seed.ChunkId] }).ToList();
        if (maxGraphHops > 1 && chunks.Count > 0)
        {
            var related = await ReadOrganizationGraphChunksAsync(scope, chunks, Math.Min(30, limit * 3), cancellationToken);
            chunks.AddRange(related);
        }
        return new(actor.OrganizationId, status.CollectionId, status.Version, status.Status,
            status.ContentHash, chunks,
            new(maxGraphHops, KnowledgeRecallRouting.GetProfile(maxGraphHops),
                execution.PairScoreCalls, execution.PairScoreCandidates, seeds.Count, chunks.Count, stopwatch.ElapsedMilliseconds));
    }

    private async Task<IReadOnlyList<KnowledgeChunkRecall>> ReadOrganizationGraphChunksAsync(
        OrganizationCorpusScope scope, IReadOnlyList<KnowledgeChunkRecall> seeds, int totalLimit,
        CancellationToken cancellationToken)
    {
        var seedIds = seeds.Select(seed => seed.ChunkId).ToHashSet(StringComparer.Ordinal);
        var paths = seeds.SelectMany(seed => seed.Relations)
            .Where(relation => relation.ReviewStatus is "approved" or "published")
            .SelectMany(relation => (relation.SemanticPath ?? []).Select((node, index) => new
            {
                Node = node, Depth = index + 1,
                Path = (IReadOnlyList<string>)relation.SemanticPath!.Take(index + 1).ToArray()
            }))
            .Where(value => value.Depth > 1 && !seedIds.Contains(value.Node))
            .GroupBy(value => value.Node, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.Depth).First(), StringComparer.Ordinal);
        if (paths.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureConnectionOpenAsync(db, cancellationToken);
        await using var command = CreateCommand(db,
            """
            SELECT k."DocumentId", d."Title", k."ChunkId", k."Text", k."StartLocator", k."EndLocator",
                   c."Domain", c."License", c."SourceUri", d."SourceLocator", k."OwnerUserName"
            FROM "LlmWikiKnowledgeCollections" c
            INNER JOIN "LlmWikiKnowledgeChunks" k
              ON k."CollectionId"=c."CollectionId" AND k."Version"=c."Version" AND k."OwnerUserName"=c."OwnerUserName"
            INNER JOIN "LlmWikiKnowledgeDocuments" d
              ON d."CollectionId"=k."CollectionId" AND d."Version"=k."Version"
             AND d."OwnerUserName"=k."OwnerUserName" AND d."DocumentId"=k."DocumentId"
            WHERE c."Status"='active' AND c."CollectionId"=@collectionFilter AND c."Version"=@versionFilter
              AND c."OwnerKind"='organization' AND c."OwnerKey"=@organizationFilter
              AND k."ChunkId"=ANY(@chunkIds);
            """);
        AddOrganizationScopeParameters(command, scope);
        command.Parameters.Add(new NpgsqlParameter("chunkIds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = paths.Keys.ToArray() });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<KnowledgeChunkRecall>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var chunkId = reader.GetString(2);
            var path = paths[chunkId];
            var relations = seeds.SelectMany(seed => seed.Relations)
                .Where(relation => relation.SemanticPath is { } nodes && nodes.SequenceEqual(path.Path))
                .DistinctBy(relation => relation.RelationId).ToArray();
            results.Add(new(scope.CollectionId, scope.Version, reader.GetString(6), reader.GetString(0),
                reader.GetString(1), chunkId, reader.GetString(3), reader.GetString(4), reader.GetString(5),
                0, relations, reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), path.Depth, path.Path));
        }
        // Graph-derived evidence has no invented similarity score; its path is the retrieval explanation.
        return SelectOrganizationGraphEvidence(results, Math.Max(0, totalLimit - seeds.Count));
    }

    internal static IReadOnlyList<KnowledgeChunkRecall> SelectOrganizationGraphEvidence(
        IReadOnlyList<KnowledgeChunkRecall> candidates, int limit)
    {
        var byId = candidates.ToDictionary(chunk => chunk.ChunkId, StringComparer.Ordinal);
        var selected = new Dictionary<string, KnowledgeChunkRecall>(StringComparer.Ordinal);
        // Preserve a complete requested-depth path before filling the budget with shallow siblings.
        foreach (var candidate in candidates.OrderByDescending(chunk => chunk.GraphDepth).ThenBy(chunk => chunk.ChunkId, StringComparer.Ordinal))
        {
            var path = (candidate.SemanticPath ?? [candidate.ChunkId])
                .Where(id => byId.ContainsKey(id) && !selected.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToArray();
            if (selected.Count + path.Length > limit) continue;
            foreach (var id in path) selected.Add(id, byId[id]);
        }
        return selected.Values.OrderBy(chunk => chunk.GraphDepth).ThenBy(chunk => chunk.ChunkId, StringComparer.Ordinal).ToArray();
    }

    private static void RequireCorpusRead(OrganizationActorContext actor)
    {
        if (actor.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(actor.ActorId)
            || !actor.Scopes.Contains(OrganizationTokenScopes.Read))
            throw new OrganizationAccessDeniedException("An authenticated organization corpus read scope is required.");
    }

    private static async Task<IReadOnlyList<SeedChunk>> ReadOrganizationAnchorAsync(
        SlogsDbContext db, OrganizationCorpusScope scope, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(db,
            """
            SELECT k."CollectionId", k."Version", k."OwnerUserName", c."Domain", k."DocumentId", d."Title",
                   k."ChunkId", k."StructureNodeId", k."Text", k."StartLocator", k."EndLocator",
                   c."License", c."SourceUri", d."SourceLocator"
            FROM "LlmWikiKnowledgeCollections" c
            INNER JOIN "LlmWikiKnowledgeChunks" k
              ON k."CollectionId"=c."CollectionId" AND k."Version"=c."Version" AND k."OwnerUserName"=c."OwnerUserName"
            INNER JOIN "LlmWikiKnowledgeDocuments" d
              ON d."CollectionId"=k."CollectionId" AND d."Version"=k."Version"
             AND d."OwnerUserName"=k."OwnerUserName" AND d."DocumentId"=k."DocumentId"
            WHERE c."Status"='active' AND c."CollectionId"=@collectionFilter AND c."Version"=@versionFilter
              AND c."OwnerKind"='organization' AND c."OwnerKey"=@organizationFilter AND k."ChunkId"=@anchor;
            """);
        AddOrganizationScopeParameters(command, scope);
        command.Parameters.AddWithValue("anchor", scope.AnchorChunkId!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new OrganizationNotFoundException("The anchor chunk does not exist in this active organization corpus.");
        return [new SeedChunk(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13), true, 100)];
    }

    private static void AddOrganizationScopeParameters(NpgsqlCommand command, OrganizationCorpusScope? scope)
    {
        command.Parameters.AddWithValue("collectionFilter", scope?.CollectionId ?? "");
        command.Parameters.AddWithValue("versionFilter", scope?.Version ?? "");
        command.Parameters.AddWithValue("organizationFilter", scope?.OrganizationId.ToString("D") ?? "");
    }
}
