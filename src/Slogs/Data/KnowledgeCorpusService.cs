using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Slogs.Data;

public sealed class KnowledgeCorpusService(
    IDbContextFactory<SlogsDbContext> dbFactory,
    IKnowledgeEmbeddingService embeddingService)
{
    private const int MaxBatchDocuments = KnowledgeCorpusBatchLimits.Documents;
    private const int MaxBatchStructureNodes = KnowledgeCorpusBatchLimits.StructureNodes;
    private const int MaxBatchChunks = KnowledgeCorpusBatchLimits.Chunks;
    private const int MaxBatchEntities = KnowledgeCorpusBatchLimits.Entities;
    private const int MaxBatchRelations = KnowledgeCorpusBatchLimits.Relations;
    private const int MaxChunkTextLength = 50_000;
    private const int MaxBgeM3OnlineRerankCandidates = 5;
    private const int ReciprocalRankFusionConstant = 60;
    private const string IndexVersion = "knowledge-corpus-v1";
    private static readonly HashSet<string> AllowedVisibility = new(StringComparer.Ordinal)
    {
        "private",
        "organization",
        "public_shared"
    };
    private static readonly HashSet<string> AllowedOwnerKinds = new(StringComparer.Ordinal)
    {
        "user",
        "organization",
        "system"
    };
    private static readonly HashSet<string> AllowedPrincipalKinds = new(StringComparer.Ordinal)
    {
        "user",
        "organization"
    };
    private static readonly HashSet<string> AllowedPermissions = new(StringComparer.Ordinal)
    {
        "reader",
        "editor",
        "maintainer"
    };
    private static readonly HashSet<string> AllowedReviewStatus = new(StringComparer.Ordinal)
    {
        "candidate",
        "approved",
        "published",
        "disputed",
        "rejected"
    };

    public Task<KnowledgeCorpusIngestResult> IngestAsync(
        string ownerUserName,
        bool isAdmin,
        KnowledgeCorpusIngestRequest request,
        CancellationToken cancellationToken = default)
        => IngestAsync(KnowledgeCorpusActor.User(ownerUserName, isAdmin), request, cancellationToken);

    public async Task<KnowledgeCorpusIngestResult> IngestAsync(
        KnowledgeCorpusActor actorContext,
        KnowledgeCorpusIngestRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = ValidateActor(actorContext);
        var collection = ValidateCollection(actor, request.Collection);
        var acl = ValidateAcl(request.Acl ?? []);
        var documents = ValidateDocuments(request.Documents);
        var structures = ValidateStructureNodes(request.StructureNodes);
        var chunks = ValidateChunks(request.Chunks);
        var entities = ValidateEntities(request.Entities);
        var relations = ValidateRelations(request.Relations);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureConnectionOpenAsync(db, cancellationToken);
        var access = await ResolveStorageOwnerAsync(db, actor, collection, cancellationToken);
        await EnsureCanWriteAsync(db, actor, access, collection, cancellationToken);
        if (acl.Count > 0)
        {
            await EnsureCanManageAclAsync(db, actor, access, collection, cancellationToken);
        }
        var storageOwner = access.StorageOwnerUserName;

        var searchTexts = chunks.Select(chunk => BuildChunkSearchText(collection, chunk)).ToArray();
        var embeddings = chunks.Count == 0
            ? []
            : await embeddingService.EmbedDocumentsAsync(searchTexts, cancellationToken);
        if (embeddings.Count != chunks.Count)
        {
            throw new InvalidOperationException(
                $"Corpus embedding count mismatch: embeddings={embeddings.Count}, chunks={chunks.Count}.");
        }
        var embeddedChunks = chunks.Select((chunk, index) =>
            new EmbeddedChunk(chunk, searchTexts[index], Sha256(searchTexts[index]), embeddings[index])).ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await UpsertCollectionAsync(db, storageOwner, collection, cancellationToken);
        await UpsertAclAsync(db, actor.UserName, storageOwner, collection, acl, cancellationToken);
        await UpsertDocumentsAsync(db, storageOwner, collection, documents, cancellationToken);
        await UpsertStructureNodesAsync(db, storageOwner, collection, structures, cancellationToken);
        await UpsertChunksAsync(db, storageOwner, collection, embeddedChunks, cancellationToken);
        await UpsertEntitiesAsync(db, storageOwner, collection, entities, cancellationToken);
        await UpsertRelationsAsync(db, storageOwner, collection, relations, cancellationToken);

        var counts = await ReadCountsAsync(db, storageOwner, collection.CollectionId, collection.Version, cancellationToken);
        string? contentHash = null;
        var status = "staging";
        if (request.Activate || request.RefreshContentHash)
        {
            contentHash = await ComputeContentHashAsync(
                db,
                storageOwner,
                collection.CollectionId,
                collection.Version,
                cancellationToken);
            if (request.Activate)
            {
                await ValidateActivationAsync(db, storageOwner, collection, counts, cancellationToken);
                await ActivateAsync(db, storageOwner, collection, contentHash, cancellationToken);
                status = "active";
            }
            else
            {
                await UpdateContentHashAsync(db, storageOwner, collection, contentHash, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new KnowledgeCorpusIngestResult(
            collection.CollectionId,
            collection.Version,
            status,
            counts.Documents,
            counts.Structures,
            counts.Chunks,
            counts.Entities,
            counts.Relations,
            contentHash);
    }

    public Task<IReadOnlyList<KnowledgeChunkRecall>> RecallAsync(
        string ownerUserName,
        string query,
        int limit = 3,
        int maxGraphHops = 2,
        IReadOnlyList<string>? organizationScopeKeys = null,
        CancellationToken cancellationToken = default)
        => RecallCoreAsync(ownerUserName, false, organizationScopeKeys, query, limit, maxGraphHops, cancellationToken);

    public Task<IReadOnlyList<KnowledgeChunkRecall>> RecallAsync(
        KnowledgeCorpusActor actor,
        string query,
        int limit = 3,
        int maxGraphHops = 2,
        CancellationToken cancellationToken = default)
    {
        var validatedActor = ValidateActor(actor);
        return RecallCoreAsync(
            validatedActor.UserName,
            validatedActor.IsAdmin,
            validatedActor.OrganizationKeys.ToArray(),
            query,
            limit,
            maxGraphHops,
            cancellationToken);
    }

    private async Task<IReadOnlyList<KnowledgeChunkRecall>> RecallCoreAsync(
        string ownerUserName,
        bool isAdmin,
        IReadOnlyList<string>? organizationScopeKeys,
        string query,
        int limit,
        int maxGraphHops,
        CancellationToken cancellationToken)
    {
        var owner = Normalize(ownerUserName, 80, "ownerUserName");
        var searchText = Normalize(query, 2_000, "query");
        var safeLimit = Math.Clamp(limit, 1, 10);
        var safeGraphHops = Math.Clamp(maxGraphHops, 0, 3);
        var scopeKeys = (organizationScopeKeys ?? [])
            .Select(value => Normalize(value, 160, "organizationScopeKey"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var queryEmbedding = await embeddingService.EmbedQueryAsync(searchText, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureConnectionOpenAsync(db, cancellationToken);
        var candidateLimit = embeddingService.SupportsFullFunctionReranking
            ? CalculateBgeM3CandidateLimit(safeLimit)
            : safeLimit;
        var lexicalTerms = BuildLexicalTerms(searchText);
        var hierarchicalReference = TryExtractHierarchicalReference(searchText);
        var exactLocatorAliases = ExtractCanonicalLocatorAliases(searchText);
        var seeds = await SearchSeedChunksAsync(
            db,
            owner,
            isAdmin,
            scopeKeys,
            ToVectorLiteral(queryEmbedding),
            lexicalTerms,
            searchText,
            hierarchicalReference?.Chapter ?? -1,
            hierarchicalReference?.Verse ?? -1,
            exactLocatorAliases,
            candidateLimit,
            cancellationToken);
        if (seeds.Count == 0)
        {
            return [];
        }

        if (embeddingService.SupportsFullFunctionReranking)
        {
            var rerankCount = Math.Min(seeds.Count, MaxBgeM3OnlineRerankCandidates);
            var scores = await embeddingService.ScorePairsAsync(
                searchText,
                seeds.Take(rerankCount).Select(BuildRerankPassage).ToArray(),
                cancellationToken);
            if (scores.Count != rerankCount)
            {
                throw new InvalidOperationException(
                    $"BGE-M3 corpus rerank count mismatch: scores={scores.Count}, candidates={rerankCount}.");
            }
            var rerankedHead = seeds.Take(rerankCount).Select((seed, index) => new
                {
                    Seed = seed with
                    {
                        RelevancePercent = (int)Math.Round(Math.Clamp(
                            (scores[index].Combined * 0.8f) + ((seed.RelevancePercent / 100f) * 0.2f),
                            0f,
                            1f) * 100f)
                    },
                    Score = scores[index].Combined,
                    OriginalOrder = index
                })
                .OrderByDescending(value => value.Seed.ExactLocatorMatch)
                .ThenByDescending(value => value.Score)
                .ThenBy(value => value.OriginalOrder)
                .Select(value => value.Seed);
            seeds = rerankedHead
                .Concat(seeds.Skip(rerankCount))
                .Take(safeLimit)
                .ToArray();
        }

        var results = new List<KnowledgeChunkRecall>(seeds.Count);
        foreach (var seed in seeds)
        {
            var relations = safeGraphHops == 0
                ? []
                : await ReadRelationsAsync(db, owner, isAdmin, scopeKeys, seed, safeGraphHops, cancellationToken);
            results.Add(new KnowledgeChunkRecall(
                seed.CollectionId,
                seed.Version,
                seed.Domain,
                seed.DocumentId,
                seed.DocumentTitle,
                seed.ChunkId,
                seed.Text,
                seed.StartLocator,
                seed.EndLocator,
                seed.RelevancePercent,
                relations));
        }

        return results;
    }

    private static KnowledgeCollectionInput ValidateCollection(KnowledgeCorpusActor actor, KnowledgeCollectionInput value)
    {
        var ownerKind = Normalize(value.OwnerKind, 24, "ownerKind").ToLowerInvariant();
        if (!AllowedOwnerKinds.Contains(ownerKind))
        {
            throw new InvalidDataException($"지원하지 않는 ownerKind: {ownerKind}");
        }

        var ownerKey = Normalize(value.OwnerKey, 160, "ownerKey");
        var visibility = Normalize(value.Visibility, 40, "visibility").ToLowerInvariant();
        if (!AllowedVisibility.Contains(visibility))
        {
            throw new InvalidDataException($"지원하지 않는 visibility: {visibility}");
        }

        var scopeKey = string.IsNullOrWhiteSpace(value.ScopeKey)
            ? null
            : Normalize(value.ScopeKey, 160, "scopeKey");
        if (visibility == "organization" && scopeKey is null)
        {
            throw new InvalidDataException("organization 컬렉션에는 scopeKey가 필요합니다.");
        }

        if (visibility != "organization" && scopeKey is not null)
        {
            throw new InvalidDataException("scopeKey는 organization 컬렉션에만 사용할 수 있습니다.");
        }

        if (visibility == "public_shared" && !value.RedistributionAllowed)
        {
            throw new InvalidOperationException("공용 지식 컬렉션에는 재배포 허가가 필요합니다.");
        }

        if (value.ExpectedChunkCount <= 0)
        {
            throw new InvalidDataException("expectedChunkCount는 양수여야 합니다.");
        }

        return value with
        {
            CollectionId = Normalize(value.CollectionId, 120, "collectionId").ToLowerInvariant(),
            Version = Normalize(value.Version, 80, "version"),
            Title = Normalize(value.Title, 200, "title"),
            Domain = Normalize(value.Domain, 80, "domain").ToLowerInvariant(),
            Language = Normalize(value.Language, 40, "language"),
            License = Normalize(value.License, 120, "license"),
            SourceUri = Normalize(value.SourceUri, 1_000, "sourceUri"),
            OwnerKind = ownerKind,
            OwnerKey = ownerKey,
            Visibility = visibility,
            ScopeKey = scopeKey
        };
    }

    private static KnowledgeCorpusActor ValidateActor(KnowledgeCorpusActor value)
        => value with
        {
            UserName = Normalize(value.UserName, 80, "actor userName"),
            OrganizationRoles = value.OrganizationRoles.ToDictionary(
                pair => Normalize(pair.Key, 160, "actor organizationKey"),
                pair => Normalize(pair.Value, 32, "actor organizationRole").ToLowerInvariant(),
                StringComparer.Ordinal)
        };

    private static IReadOnlyList<KnowledgeAclGrantInput> ValidateAcl(IReadOnlyList<KnowledgeAclGrantInput> values)
    {
        var normalized = values.Select(value => value with
        {
            PrincipalKind = Normalize(value.PrincipalKind, 24, "ACL principalKind").ToLowerInvariant(),
            PrincipalKey = Normalize(value.PrincipalKey, 160, "ACL principalKey"),
            Permission = Normalize(value.Permission, 24, "ACL permission").ToLowerInvariant()
        }).ToArray();
        if (normalized.Any(value => !AllowedPrincipalKinds.Contains(value.PrincipalKind)))
        {
            throw new InvalidDataException("ACL principalKind는 user 또는 organization이어야 합니다.");
        }

        if (normalized.Any(value => !AllowedPermissions.Contains(value.Permission)))
        {
            throw new InvalidDataException("ACL permission은 reader, editor 또는 maintainer이어야 합니다.");
        }

        EnsureUnique(normalized.Select(value => $"{value.PrincipalKind}\0{value.PrincipalKey}"), "ACL principal");
        return normalized;
    }

    private static async Task<CollectionAccessResolution> ResolveStorageOwnerAsync(
        SlogsDbContext db,
        KnowledgeCorpusActor actor,
        KnowledgeCollectionInput collection,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(db,
            """
            SELECT "OwnerUserName", "Visibility", "ScopeKey"
            FROM "LlmWikiKnowledgeCollections"
            WHERE "CollectionId"=@collectionId AND "Version"=@version
              AND "OwnerKind"=@ownerKind AND "OwnerKey"=@ownerKey
            LIMIT 2;
            """);
        command.Parameters.Add(new NpgsqlParameter("collectionId", collection.CollectionId));
        command.Parameters.Add(new NpgsqlParameter("version", collection.Version));
        command.Parameters.Add(new NpgsqlParameter("ownerKind", collection.OwnerKind));
        command.Parameters.Add(new NpgsqlParameter("ownerKey", collection.OwnerKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var matches = new List<CollectionAccessResolution>(2);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new CollectionAccessResolution(
                reader.GetString(0),
                true,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return matches.Count switch
        {
            0 => new CollectionAccessResolution(actor.UserName, false, null, null),
            1 => matches[0],
            _ => throw new InvalidOperationException("같은 소유 주체에 중복된 컬렉션 버전이 있습니다.")
        };
    }

    private static async Task EnsureCanWriteAsync(
        SlogsDbContext db,
        KnowledgeCorpusActor actor,
        CollectionAccessResolution resolution,
        KnowledgeCollectionInput collection,
        CancellationToken cancellationToken)
    {
        if (!resolution.Exists)
        {
            if (!actor.IsAdmin && !ActorControlsOwner(actor, collection.OwnerKind, collection.OwnerKey))
            {
                throw new UnauthorizedAccessException("확인된 소유자 주체만 지식 컬렉션을 생성할 수 있습니다.");
            }

            if (collection.Visibility == "public_shared" && !actor.IsAdmin)
            {
                throw new UnauthorizedAccessException("새 공용 지식 컬렉션의 공개 활성화에는 관리자 권한이 필요합니다.");
            }

            if (collection.Visibility == "organization"
                && !actor.IsAdmin
                && !actor.OrganizationKeys.Contains(collection.ScopeKey!))
            {
                throw new UnauthorizedAccessException("확인된 조직 구성원만 해당 조직에 컬렉션을 공개할 수 있습니다.");
            }

            return;
        }

        if (collection.Visibility == "public_shared"
            && resolution.ExistingVisibility != "public_shared"
            && !actor.IsAdmin)
        {
            throw new UnauthorizedAccessException("공용 읽기 범위로의 전환에는 관리자 권한이 필요합니다.");
        }

        if (collection.Visibility == "organization"
            && (resolution.ExistingVisibility != "organization" || resolution.ExistingScopeKey != collection.ScopeKey)
            && !actor.IsAdmin
            && !actor.OrganizationKeys.Contains(collection.ScopeKey!))
        {
            throw new UnauthorizedAccessException("확인된 조직 구성원만 해당 조직으로 열람 범위를 변경할 수 있습니다.");
        }

        if (actor.IsAdmin || ActorControlsOwner(actor, collection.OwnerKind, collection.OwnerKey))
        {
            return;
        }

        await using var command = CreateCommand(db,
            """
            SELECT EXISTS (
                SELECT 1 FROM "LlmWikiKnowledgeCollectionAcl"
                WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner
                  AND "Permission" IN ('editor', 'maintainer')
                  AND (("PrincipalKind"='user' AND "PrincipalKey"=@actor)
                    OR ("PrincipalKind"='organization' AND "PrincipalKey"=ANY(@organizationKeys)))
            );
            """);
        AddCollectionParameters(command, resolution.StorageOwnerUserName, collection);
        command.Parameters.Add(new NpgsqlParameter("actor", actor.UserName));
        command.Parameters.Add(new NpgsqlParameter("organizationKeys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = actor.OrganizationKeys.ToArray() });
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
        {
            throw new UnauthorizedAccessException("컬렉션 수정 권한이 없습니다. 공개 읽기 권한은 수정 권한을 부여하지 않습니다.");
        }
    }

    private static bool ActorControlsOwner(KnowledgeCorpusActor actor, string ownerKind, string ownerKey)
        => ownerKind switch
        {
            "user" => ownerKey.Equals(actor.UserName, StringComparison.Ordinal),
            "organization" => CanManageOrganization(actor, ownerKey),
            "system" => actor.IsAdmin,
            _ => false
        };

    private static bool CanManageOrganization(KnowledgeCorpusActor actor, string organizationKey)
        => actor.OrganizationRoles.TryGetValue(organizationKey, out var role)
            && role is OrganizationRoles.Owner or OrganizationRoles.Admin;

    private static async Task EnsureCanManageAclAsync(
        SlogsDbContext db,
        KnowledgeCorpusActor actor,
        CollectionAccessResolution resolution,
        KnowledgeCollectionInput collection,
        CancellationToken cancellationToken)
    {
        if (!resolution.Exists || actor.IsAdmin || ActorControlsOwner(actor, collection.OwnerKind, collection.OwnerKey))
        {
            return;
        }

        await using var command = CreateCommand(db,
            """
            SELECT EXISTS (
                SELECT 1 FROM "LlmWikiKnowledgeCollectionAcl"
                WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner
                  AND "Permission"='maintainer'
                  AND (("PrincipalKind"='user' AND "PrincipalKey"=@actor)
                    OR ("PrincipalKind"='organization' AND "PrincipalKey"=ANY(@organizationKeys)))
            );
            """);
        AddCollectionParameters(command, resolution.StorageOwnerUserName, collection);
        command.Parameters.Add(new NpgsqlParameter("actor", actor.UserName));
        command.Parameters.Add(new NpgsqlParameter("organizationKeys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = actor.OrganizationKeys.ToArray() });
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
        {
            throw new UnauthorizedAccessException("ACL 변경은 소유자, 관리자 또는 maintainer만 할 수 있습니다.");
        }
    }

    private static IReadOnlyList<KnowledgeDocumentInput> ValidateDocuments(IReadOnlyList<KnowledgeDocumentInput> values)
    {
        ValidateBatch(values.Count, MaxBatchDocuments, "documents");
        var normalized = values.Select(value => value with
        {
            DocumentId = Normalize(value.DocumentId, 180, "documentId"),
            Title = Normalize(value.Title, 300, "document title"),
            DocumentType = Normalize(value.DocumentType, 80, "documentType").ToLowerInvariant(),
            SourceLocator = Normalize(value.SourceLocator, 1_000, "sourceLocator"),
            Metadata = NormalizeMetadata(value.Metadata)
        }).ToArray();
        EnsureUnique(normalized.Select(value => value.DocumentId), "documentId");
        return normalized;
    }

    private static IReadOnlyList<KnowledgeStructureInput> ValidateStructureNodes(IReadOnlyList<KnowledgeStructureInput> values)
    {
        ValidateBatch(values.Count, MaxBatchStructureNodes, "structureNodes");
        var normalized = values.Select(value => value with
        {
            NodeId = Normalize(value.NodeId, 220, "structure nodeId"),
            DocumentId = Normalize(value.DocumentId, 180, "structure documentId"),
            ParentNodeId = string.IsNullOrWhiteSpace(value.ParentNodeId) ? null : Normalize(value.ParentNodeId, 220, "parentNodeId"),
            NodeType = Normalize(value.NodeType, 80, "nodeType").ToLowerInvariant(),
            Label = Normalize(value.Label, 300, "structure label"),
            Locator = Normalize(value.Locator, 500, "structure locator"),
            Metadata = NormalizeMetadata(value.Metadata)
        }).ToArray();
        EnsureUnique(normalized.Select(value => value.NodeId), "structure nodeId");
        if (normalized.Any(value => value.ParentNodeId == value.NodeId))
        {
            throw new InvalidDataException("구조 노드는 자신을 부모로 지정할 수 없습니다.");
        }

        return normalized;
    }

    private static IReadOnlyList<KnowledgeChunkInput> ValidateChunks(IReadOnlyList<KnowledgeChunkInput> values)
    {
        ValidateBatch(values.Count, MaxBatchChunks, "chunks");
        var normalized = values.Select(value => value with
        {
            ChunkId = Normalize(value.ChunkId, 240, "chunkId"),
            DocumentId = Normalize(value.DocumentId, 180, "chunk documentId"),
            StructureNodeId = string.IsNullOrWhiteSpace(value.StructureNodeId) ? null : Normalize(value.StructureNodeId, 220, "structureNodeId"),
            Text = Normalize(value.Text, MaxChunkTextLength, "chunk text"),
            StartLocator = Normalize(value.StartLocator, 500, "startLocator"),
            EndLocator = Normalize(value.EndLocator, 500, "endLocator"),
            PreviousChunkId = string.IsNullOrWhiteSpace(value.PreviousChunkId) ? null : Normalize(value.PreviousChunkId, 240, "previousChunkId"),
            NextChunkId = string.IsNullOrWhiteSpace(value.NextChunkId) ? null : Normalize(value.NextChunkId, 240, "nextChunkId"),
            TokenizerId = Normalize(value.TokenizerId, 80, "tokenizerId"),
            SearchAliases = NormalizeAliases(value.SearchAliases),
            Metadata = NormalizeMetadata(value.Metadata)
        }).ToArray();
        EnsureUnique(normalized.Select(value => value.ChunkId), "chunkId");
        if (normalized.Any(value => value.TokenCount <= 0 || value.OverlapUnits < 0))
        {
            throw new InvalidDataException("chunk tokenCount는 양수이고 overlapUnits는 0 이상이어야 합니다.");
        }

        return normalized;
    }

    private static IReadOnlyList<KnowledgeEntityInput> ValidateEntities(IReadOnlyList<KnowledgeEntityInput> values)
    {
        ValidateBatch(values.Count, MaxBatchEntities, "entities");
        var normalized = values.Select(value => value with
        {
            EntityId = Normalize(value.EntityId, 240, "entityId"),
            EntityType = Normalize(value.EntityType, 80, "entityType").ToLowerInvariant(),
            CanonicalLabel = Normalize(value.CanonicalLabel, 300, "canonicalLabel"),
            Aliases = NormalizeAliases(value.Aliases),
            Metadata = NormalizeMetadata(value.Metadata)
        }).ToArray();
        EnsureUnique(normalized.Select(value => value.EntityId), "entityId");
        return normalized;
    }

    private static IReadOnlyList<KnowledgeRelationInput> ValidateRelations(IReadOnlyList<KnowledgeRelationInput> values)
    {
        ValidateBatch(values.Count, MaxBatchRelations, "relations");
        var normalized = values.Select(value =>
        {
            var status = Normalize(value.ReviewStatus, 40, "reviewStatus").ToLowerInvariant();
            if (!AllowedReviewStatus.Contains(status))
            {
                throw new InvalidDataException($"지원하지 않는 reviewStatus: {status}");
            }

            if (value.Confidence is < 0 or > 1)
            {
                throw new InvalidDataException("relation confidence는 0..1 범위여야 합니다.");
            }

            var evidence = value.Evidence.Select(item => item with
            {
                SourceId = Normalize(item.SourceId, 120, "evidence sourceId"),
                Locator = Normalize(item.Locator, 500, "evidence locator"),
                EvidenceType = Normalize(item.EvidenceType, 80, "evidenceType").ToLowerInvariant(),
                ChunkIds = NormalizeAliases(item.ChunkIds)
            }).ToArray();
            if (status is "approved" or "published" && evidence.Length == 0)
            {
                throw new InvalidDataException("승인된 관계에는 근거가 필요합니다.");
            }

            return value with
            {
                RelationId = Normalize(value.RelationId, 240, "relationId"),
                FromNodeId = Normalize(value.FromNodeId, 240, "fromNodeId"),
                RelationType = Normalize(value.RelationType, 100, "relationType").ToLowerInvariant(),
                ToNodeId = Normalize(value.ToNodeId, 240, "toNodeId"),
                ClaimClass = Normalize(value.ClaimClass, 80, "claimClass").ToLowerInvariant(),
                ReviewStatus = status,
                Evidence = evidence,
                CreatedBy = Normalize(value.CreatedBy, 80, "createdBy"),
                Metadata = NormalizeMetadata(value.Metadata)
            };
        }).ToArray();
        EnsureUnique(normalized.Select(value => value.RelationId), "relationId");
        return normalized;
    }

    private static async Task UpsertCollectionAsync(SlogsDbContext db, string owner, KnowledgeCollectionInput value, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(db,
            """
            INSERT INTO "LlmWikiKnowledgeCollections"
                ("CollectionId", "Version", "OwnerUserName", "Title", "Domain", "Language", "License", "SourceUri", "OwnerKind", "OwnerKey", "Visibility", "ScopeKey", "RedistributionAllowed", "ExpectedChunkCount", "Status", "ContentHash", "CreatedAt", "UpdatedAt")
            VALUES
                (@collectionId, @version, @owner, @title, @domain, @language, @license, @sourceUri, @ownerKind, @ownerKey, @visibility, @scopeKey, @redistributionAllowed, @expectedChunkCount, 'staging', '', @now, @now)
            ON CONFLICT ("CollectionId", "Version", "OwnerUserName") DO UPDATE SET
                "Title" = EXCLUDED."Title", "Domain" = EXCLUDED."Domain", "Language" = EXCLUDED."Language",
                "License" = EXCLUDED."License", "SourceUri" = EXCLUDED."SourceUri", "Visibility" = EXCLUDED."Visibility",
                "ScopeKey" = EXCLUDED."ScopeKey", "RedistributionAllowed" = EXCLUDED."RedistributionAllowed",
                "ExpectedChunkCount" = EXCLUDED."ExpectedChunkCount", "UpdatedAt" = EXCLUDED."UpdatedAt";
            """);
        AddCollectionParameters(command, owner, value);
        command.Parameters.Add(new NpgsqlParameter("title", value.Title));
        command.Parameters.Add(new NpgsqlParameter("domain", value.Domain));
        command.Parameters.Add(new NpgsqlParameter("language", value.Language));
        command.Parameters.Add(new NpgsqlParameter("license", value.License));
        command.Parameters.Add(new NpgsqlParameter("sourceUri", value.SourceUri));
        command.Parameters.Add(new NpgsqlParameter("ownerKind", value.OwnerKind));
        command.Parameters.Add(new NpgsqlParameter("ownerKey", value.OwnerKey));
        command.Parameters.Add(new NpgsqlParameter("visibility", value.Visibility));
        command.Parameters.Add(new NpgsqlParameter("scopeKey", (object?)value.ScopeKey ?? DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("redistributionAllowed", value.RedistributionAllowed));
        command.Parameters.Add(new NpgsqlParameter("expectedChunkCount", value.ExpectedChunkCount));
        command.Parameters.Add(new NpgsqlParameter("now", DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertAclAsync(
        SlogsDbContext db,
        string actorUserName,
        string owner,
        KnowledgeCollectionInput collection,
        IReadOnlyList<KnowledgeAclGrantInput> values,
        CancellationToken cancellationToken)
    {
        foreach (var value in values)
        {
            await using var command = CreateCommand(db,
                """
                INSERT INTO "LlmWikiKnowledgeCollectionAcl"
                    ("CollectionId", "Version", "OwnerUserName", "PrincipalKind", "PrincipalKey", "Permission", "GrantedByUserName", "CreatedAt")
                VALUES (@collectionId, @version, @owner, @principalKind, @principalKey, @permission, @actor, @now)
                ON CONFLICT ("CollectionId", "Version", "OwnerUserName", "PrincipalKind", "PrincipalKey") DO UPDATE SET
                    "Permission"=EXCLUDED."Permission", "GrantedByUserName"=EXCLUDED."GrantedByUserName", "CreatedAt"=EXCLUDED."CreatedAt";
                """);
            AddCollectionParameters(command, owner, collection);
            command.Parameters.Add(new NpgsqlParameter("principalKind", value.PrincipalKind));
            command.Parameters.Add(new NpgsqlParameter("principalKey", value.PrincipalKey));
            command.Parameters.Add(new NpgsqlParameter("permission", value.Permission));
            command.Parameters.Add(new NpgsqlParameter("actor", actorUserName));
            command.Parameters.Add(new NpgsqlParameter("now", DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpsertDocumentsAsync(SlogsDbContext db, string owner, KnowledgeCollectionInput collection, IReadOnlyList<KnowledgeDocumentInput> values, CancellationToken cancellationToken)
    {
        foreach (var value in values)
        {
            await using var command = CreateCommand(db,
                """
                INSERT INTO "LlmWikiKnowledgeDocuments"
                    ("CollectionId", "Version", "OwnerUserName", "DocumentId", "Title", "DocumentType", "Ordinal", "SourceLocator", "MetadataJson", "UpdatedAt")
                VALUES (@collectionId, @version, @owner, @id, @title, @type, @ordinal, @locator, CAST(@metadata AS jsonb), @now)
                ON CONFLICT ("CollectionId", "Version", "OwnerUserName", "DocumentId") DO UPDATE SET
                    "Title" = EXCLUDED."Title", "DocumentType" = EXCLUDED."DocumentType", "Ordinal" = EXCLUDED."Ordinal",
                    "SourceLocator" = EXCLUDED."SourceLocator", "MetadataJson" = EXCLUDED."MetadataJson", "UpdatedAt" = EXCLUDED."UpdatedAt";
                """);
            AddCollectionParameters(command, owner, collection);
            command.Parameters.Add(new NpgsqlParameter("id", value.DocumentId));
            command.Parameters.Add(new NpgsqlParameter("title", value.Title));
            command.Parameters.Add(new NpgsqlParameter("type", value.DocumentType));
            command.Parameters.Add(new NpgsqlParameter("ordinal", value.Ordinal));
            command.Parameters.Add(new NpgsqlParameter("locator", value.SourceLocator));
            command.Parameters.Add(new NpgsqlParameter("metadata", JsonSerializer.Serialize(value.Metadata)));
            command.Parameters.Add(new NpgsqlParameter("now", DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpsertStructureNodesAsync(SlogsDbContext db, string owner, KnowledgeCollectionInput collection, IReadOnlyList<KnowledgeStructureInput> values, CancellationToken cancellationToken)
    {
        foreach (var value in values)
        {
            await using var command = CreateCommand(db,
                """
                INSERT INTO "LlmWikiKnowledgeStructureNodes"
                    ("CollectionId", "Version", "OwnerUserName", "NodeId", "DocumentId", "ParentNodeId", "NodeType", "Label", "Ordinal", "Locator", "MetadataJson", "UpdatedAt")
                VALUES (@collectionId, @version, @owner, @id, @documentId, @parentId, @type, @label, @ordinal, @locator, CAST(@metadata AS jsonb), @now)
                ON CONFLICT ("CollectionId", "Version", "OwnerUserName", "NodeId") DO UPDATE SET
                    "DocumentId" = EXCLUDED."DocumentId", "ParentNodeId" = EXCLUDED."ParentNodeId", "NodeType" = EXCLUDED."NodeType",
                    "Label" = EXCLUDED."Label", "Ordinal" = EXCLUDED."Ordinal", "Locator" = EXCLUDED."Locator",
                    "MetadataJson" = EXCLUDED."MetadataJson", "UpdatedAt" = EXCLUDED."UpdatedAt";
                """);
            AddCollectionParameters(command, owner, collection);
            command.Parameters.Add(new NpgsqlParameter("id", value.NodeId));
            command.Parameters.Add(new NpgsqlParameter("documentId", value.DocumentId));
            command.Parameters.Add(new NpgsqlParameter("parentId", (object?)value.ParentNodeId ?? DBNull.Value));
            command.Parameters.Add(new NpgsqlParameter("type", value.NodeType));
            command.Parameters.Add(new NpgsqlParameter("label", value.Label));
            command.Parameters.Add(new NpgsqlParameter("ordinal", value.Ordinal));
            command.Parameters.Add(new NpgsqlParameter("locator", value.Locator));
            command.Parameters.Add(new NpgsqlParameter("metadata", JsonSerializer.Serialize(value.Metadata)));
            command.Parameters.Add(new NpgsqlParameter("now", DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task UpsertChunksAsync(SlogsDbContext db, string owner, KnowledgeCollectionInput collection, IReadOnlyList<EmbeddedChunk> values, CancellationToken cancellationToken)
    {
        foreach (var item in values)
        {
            var value = item.Chunk;
            await using var command = CreateCommand(db,
                """
                INSERT INTO "LlmWikiKnowledgeChunks"
                    ("CollectionId", "Version", "OwnerUserName", "ChunkId", "DocumentId", "StructureNodeId", "Ordinal", "Text", "StartLocator", "EndLocator", "PreviousChunkId", "NextChunkId", "OverlapUnits", "TokenCount", "TokenizerId", "SearchAliasesJson", "MetadataJson", "SearchText", "ContentHash", "EmbeddingModel", "EmbeddingDimensions", "IndexVersion", "Embedding", "UpdatedAt")
                VALUES (@collectionId, @version, @owner, @id, @documentId, @structureId, @ordinal, @text, @startLocator, @endLocator, @previousId, @nextId, @overlap, @tokenCount, @tokenizerId, CAST(@aliases AS jsonb), CAST(@metadata AS jsonb), @searchText, @hash, @model, @dimensions, @indexVersion, CAST(@embedding AS vector), @now)
                ON CONFLICT ("CollectionId", "Version", "OwnerUserName", "ChunkId") DO UPDATE SET
                    "DocumentId" = EXCLUDED."DocumentId", "StructureNodeId" = EXCLUDED."StructureNodeId", "Ordinal" = EXCLUDED."Ordinal",
                    "Text" = EXCLUDED."Text", "StartLocator" = EXCLUDED."StartLocator", "EndLocator" = EXCLUDED."EndLocator",
                    "PreviousChunkId" = EXCLUDED."PreviousChunkId", "NextChunkId" = EXCLUDED."NextChunkId", "OverlapUnits" = EXCLUDED."OverlapUnits",
                    "TokenCount" = EXCLUDED."TokenCount", "TokenizerId" = EXCLUDED."TokenizerId", "SearchAliasesJson" = EXCLUDED."SearchAliasesJson",
                    "MetadataJson" = EXCLUDED."MetadataJson", "SearchText" = EXCLUDED."SearchText", "ContentHash" = EXCLUDED."ContentHash",
                    "EmbeddingModel" = EXCLUDED."EmbeddingModel", "EmbeddingDimensions" = EXCLUDED."EmbeddingDimensions",
                    "IndexVersion" = EXCLUDED."IndexVersion", "Embedding" = EXCLUDED."Embedding", "UpdatedAt" = EXCLUDED."UpdatedAt";
                """);
            AddCollectionParameters(command, owner, collection);
            command.Parameters.Add(new NpgsqlParameter("id", value.ChunkId));
            command.Parameters.Add(new NpgsqlParameter("documentId", value.DocumentId));
            command.Parameters.Add(new NpgsqlParameter("structureId", (object?)value.StructureNodeId ?? DBNull.Value));
            command.Parameters.Add(new NpgsqlParameter("ordinal", value.Ordinal));
            command.Parameters.Add(new NpgsqlParameter("text", value.Text));
            command.Parameters.Add(new NpgsqlParameter("startLocator", value.StartLocator));
            command.Parameters.Add(new NpgsqlParameter("endLocator", value.EndLocator));
            command.Parameters.Add(new NpgsqlParameter("previousId", (object?)value.PreviousChunkId ?? DBNull.Value));
            command.Parameters.Add(new NpgsqlParameter("nextId", (object?)value.NextChunkId ?? DBNull.Value));
            command.Parameters.Add(new NpgsqlParameter("overlap", value.OverlapUnits));
            command.Parameters.Add(new NpgsqlParameter("tokenCount", value.TokenCount));
            command.Parameters.Add(new NpgsqlParameter("tokenizerId", value.TokenizerId));
            command.Parameters.Add(new NpgsqlParameter("aliases", JsonSerializer.Serialize(value.SearchAliases)));
            command.Parameters.Add(new NpgsqlParameter("metadata", JsonSerializer.Serialize(value.Metadata)));
            command.Parameters.Add(new NpgsqlParameter("searchText", item.SearchText));
            command.Parameters.Add(new NpgsqlParameter("hash", item.ContentHash));
            command.Parameters.Add(new NpgsqlParameter("model", embeddingService.Model));
            command.Parameters.Add(new NpgsqlParameter("dimensions", embeddingService.Dimensions));
            command.Parameters.Add(new NpgsqlParameter("indexVersion", IndexVersion));
            command.Parameters.Add(new NpgsqlParameter("embedding", ToVectorLiteral(item.Embedding)));
            command.Parameters.Add(new NpgsqlParameter("now", DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpsertEntitiesAsync(SlogsDbContext db, string owner, KnowledgeCollectionInput collection, IReadOnlyList<KnowledgeEntityInput> values, CancellationToken cancellationToken)
    {
        foreach (var value in values)
        {
            await using var command = CreateCommand(db,
                """
                INSERT INTO "LlmWikiKnowledgeEntities"
                    ("CollectionId", "Version", "OwnerUserName", "EntityId", "EntityType", "CanonicalLabel", "AliasesJson", "MetadataJson", "UpdatedAt")
                VALUES (@collectionId, @version, @owner, @id, @type, @label, CAST(@aliases AS jsonb), CAST(@metadata AS jsonb), @now)
                ON CONFLICT ("CollectionId", "Version", "OwnerUserName", "EntityId") DO UPDATE SET
                    "EntityType" = EXCLUDED."EntityType", "CanonicalLabel" = EXCLUDED."CanonicalLabel",
                    "AliasesJson" = EXCLUDED."AliasesJson", "MetadataJson" = EXCLUDED."MetadataJson", "UpdatedAt" = EXCLUDED."UpdatedAt";
                """);
            AddCollectionParameters(command, owner, collection);
            command.Parameters.Add(new NpgsqlParameter("id", value.EntityId));
            command.Parameters.Add(new NpgsqlParameter("type", value.EntityType));
            command.Parameters.Add(new NpgsqlParameter("label", value.CanonicalLabel));
            command.Parameters.Add(new NpgsqlParameter("aliases", JsonSerializer.Serialize(value.Aliases)));
            command.Parameters.Add(new NpgsqlParameter("metadata", JsonSerializer.Serialize(value.Metadata)));
            command.Parameters.Add(new NpgsqlParameter("now", DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpsertRelationsAsync(SlogsDbContext db, string owner, KnowledgeCollectionInput collection, IReadOnlyList<KnowledgeRelationInput> values, CancellationToken cancellationToken)
    {
        foreach (var value in values)
        {
            await using var command = CreateCommand(db,
                """
                INSERT INTO "LlmWikiKnowledgeRelations"
                    ("CollectionId", "Version", "OwnerUserName", "RelationId", "FromNodeId", "RelationType", "ToNodeId", "ClaimClass", "ReviewStatus", "Confidence", "EvidenceJson", "CreatedBy", "MetadataJson", "UpdatedAt")
                VALUES (@collectionId, @version, @owner, @id, @fromId, @type, @toId, @claimClass, @status, @confidence, CAST(@evidence AS jsonb), @createdBy, CAST(@metadata AS jsonb), @now)
                ON CONFLICT ("CollectionId", "Version", "OwnerUserName", "RelationId") DO UPDATE SET
                    "FromNodeId" = EXCLUDED."FromNodeId", "RelationType" = EXCLUDED."RelationType", "ToNodeId" = EXCLUDED."ToNodeId",
                    "ClaimClass" = EXCLUDED."ClaimClass", "ReviewStatus" = EXCLUDED."ReviewStatus", "Confidence" = EXCLUDED."Confidence",
                    "EvidenceJson" = EXCLUDED."EvidenceJson", "CreatedBy" = EXCLUDED."CreatedBy",
                    "MetadataJson" = EXCLUDED."MetadataJson", "UpdatedAt" = EXCLUDED."UpdatedAt";
                """);
            AddCollectionParameters(command, owner, collection);
            command.Parameters.Add(new NpgsqlParameter("id", value.RelationId));
            command.Parameters.Add(new NpgsqlParameter("fromId", value.FromNodeId));
            command.Parameters.Add(new NpgsqlParameter("type", value.RelationType));
            command.Parameters.Add(new NpgsqlParameter("toId", value.ToNodeId));
            command.Parameters.Add(new NpgsqlParameter("claimClass", value.ClaimClass));
            command.Parameters.Add(new NpgsqlParameter("status", value.ReviewStatus));
            command.Parameters.Add(new NpgsqlParameter("confidence", value.Confidence));
            command.Parameters.Add(new NpgsqlParameter("evidence", JsonSerializer.Serialize(value.Evidence)));
            command.Parameters.Add(new NpgsqlParameter("createdBy", value.CreatedBy));
            command.Parameters.Add(new NpgsqlParameter("metadata", JsonSerializer.Serialize(value.Metadata)));
            command.Parameters.Add(new NpgsqlParameter("now", DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<CorpusCounts> ReadCountsAsync(SlogsDbContext db, string owner, string collectionId, string version, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(db,
            """
            SELECT
                (SELECT COUNT(*) FROM "LlmWikiKnowledgeDocuments" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner),
                (SELECT COUNT(*) FROM "LlmWikiKnowledgeStructureNodes" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner),
                (SELECT COUNT(*) FROM "LlmWikiKnowledgeChunks" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner),
                (SELECT COUNT(*) FROM "LlmWikiKnowledgeEntities" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner),
                (SELECT COUNT(*) FROM "LlmWikiKnowledgeRelations" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner);
            """);
        AddIdentityParameters(command, owner, collectionId, version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("지식 컬렉션 개수를 읽지 못했습니다.");
        }

        return new CorpusCounts(
            checked((int)reader.GetInt64(0)),
            checked((int)reader.GetInt64(1)),
            checked((int)reader.GetInt64(2)),
            checked((int)reader.GetInt64(3)),
            checked((int)reader.GetInt64(4)));
    }

    private static async Task ValidateActivationAsync(SlogsDbContext db, string owner, KnowledgeCollectionInput collection, CorpusCounts counts, CancellationToken cancellationToken)
    {
        if (counts.Chunks != collection.ExpectedChunkCount)
        {
            throw new InvalidDataException($"컬렉션 활성화 실패: expectedChunks={collection.ExpectedChunkCount}, actualChunks={counts.Chunks}");
        }

        await using var command = CreateCommand(db,
            """
            WITH all_nodes AS (
                SELECT "DocumentId" AS id FROM "LlmWikiKnowledgeDocuments" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner
                UNION ALL SELECT "NodeId" FROM "LlmWikiKnowledgeStructureNodes" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner
                UNION ALL SELECT "ChunkId" FROM "LlmWikiKnowledgeChunks" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner
                UNION ALL SELECT "EntityId" FROM "LlmWikiKnowledgeEntities" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner
            ), invalid_neighbors AS (
                SELECT c."ChunkId"
                FROM "LlmWikiKnowledgeChunks" c
                WHERE c."CollectionId"=@collectionId AND c."Version"=@version AND c."OwnerUserName"=@owner
                  AND ((c."PreviousChunkId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM all_nodes n WHERE n.id=c."PreviousChunkId"))
                    OR (c."NextChunkId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM all_nodes n WHERE n.id=c."NextChunkId"))
                    OR (c."PreviousChunkId" IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM "LlmWikiKnowledgeChunks" p
                        WHERE p."CollectionId"=@collectionId AND p."Version"=@version AND p."OwnerUserName"=@owner
                          AND p."ChunkId"=c."PreviousChunkId" AND p."NextChunkId"=c."ChunkId"))
                    OR (c."NextChunkId" IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM "LlmWikiKnowledgeChunks" n
                        WHERE n."CollectionId"=@collectionId AND n."Version"=@version AND n."OwnerUserName"=@owner
                          AND n."ChunkId"=c."NextChunkId" AND n."PreviousChunkId"=c."ChunkId")))
            ), invalid_structure_links AS (
                SELECT s."NodeId"
                FROM "LlmWikiKnowledgeStructureNodes" s
                WHERE s."CollectionId"=@collectionId AND s."Version"=@version AND s."OwnerUserName"=@owner
                  AND s."ParentNodeId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM "LlmWikiKnowledgeStructureNodes" p
                    WHERE p."CollectionId"=@collectionId AND p."Version"=@version AND p."OwnerUserName"=@owner AND p."NodeId"=s."ParentNodeId")
                UNION ALL
                SELECT c."ChunkId"
                FROM "LlmWikiKnowledgeChunks" c
                WHERE c."CollectionId"=@collectionId AND c."Version"=@version AND c."OwnerUserName"=@owner
                  AND c."StructureNodeId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM "LlmWikiKnowledgeStructureNodes" s
                    WHERE s."CollectionId"=@collectionId AND s."Version"=@version AND s."OwnerUserName"=@owner AND s."NodeId"=c."StructureNodeId")
            ), invalid_relations AS (
                SELECT r."RelationId"
                FROM "LlmWikiKnowledgeRelations" r
                WHERE r."CollectionId"=@collectionId AND r."Version"=@version AND r."OwnerUserName"=@owner
                  AND (NOT EXISTS (SELECT 1 FROM all_nodes n WHERE n.id=r."FromNodeId")
                    OR NOT EXISTS (SELECT 1 FROM all_nodes n WHERE n.id=r."ToNodeId"))
            ), invalid_evidence_chunks AS (
                SELECT r."RelationId"
                FROM "LlmWikiKnowledgeRelations" r
                CROSS JOIN LATERAL jsonb_array_elements(r."EvidenceJson") evidence
                CROSS JOIN LATERAL jsonb_array_elements_text(COALESCE(evidence->'ChunkIds', '[]'::jsonb)) chunk_id
                WHERE r."CollectionId"=@collectionId AND r."Version"=@version AND r."OwnerUserName"=@owner
                  AND NOT EXISTS (SELECT 1 FROM "LlmWikiKnowledgeChunks" c
                    WHERE c."CollectionId"=@collectionId AND c."Version"=@version AND c."OwnerUserName"=@owner AND c."ChunkId"=chunk_id)
            ), duplicate_nodes AS (
                SELECT id FROM all_nodes GROUP BY id HAVING COUNT(*) > 1
            )
            SELECT
                (SELECT COUNT(*) FROM invalid_neighbors),
                (SELECT COUNT(*) FROM invalid_structure_links),
                (SELECT COUNT(*) FROM invalid_relations),
                (SELECT COUNT(*) FROM invalid_evidence_chunks),
                (SELECT COUNT(*) FROM duplicate_nodes);
            """);
        AddCollectionParameters(command, owner, collection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var invalidNeighbors = reader.GetInt64(0);
        var invalidStructureLinks = reader.GetInt64(1);
        var invalidRelations = reader.GetInt64(2);
        var invalidEvidenceChunks = reader.GetInt64(3);
        var duplicateNodes = reader.GetInt64(4);
        if (invalidNeighbors > 0 || invalidStructureLinks > 0 || invalidRelations > 0 || invalidEvidenceChunks > 0 || duplicateNodes > 0)
        {
            throw new InvalidDataException(
                $"컬렉션 활성화 무결성 실패: invalidNeighbors={invalidNeighbors}, invalidStructureLinks={invalidStructureLinks}, invalidRelations={invalidRelations}, invalidEvidenceChunks={invalidEvidenceChunks}, duplicateNodeIds={duplicateNodes}");
        }
    }

    private static async Task<string> ComputeContentHashAsync(SlogsDbContext db, string owner, string collectionId, string version, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(db,
            """
            SELECT value FROM (
                SELECT 'C|' || "ChunkId" || '|' || "ContentHash" AS value FROM "LlmWikiKnowledgeChunks" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner
                UNION ALL
                SELECT 'R|' || "RelationId" || '|' || "FromNodeId" || '|' || "RelationType" || '|' || "ToNodeId" || '|' || "ReviewStatus" || '|' || "EvidenceJson"::text
                FROM "LlmWikiKnowledgeRelations" WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner
            ) values ORDER BY value;
            """);
        AddIdentityParameters(command, owner, collectionId, version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var builder = new StringBuilder();
        while (await reader.ReadAsync(cancellationToken))
        {
            builder.AppendLine(reader.GetString(0));
        }

        return Sha256(builder.ToString());
    }

    private static async Task ActivateAsync(SlogsDbContext db, string owner, KnowledgeCollectionInput collection, string contentHash, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(db,
            """
            UPDATE "LlmWikiKnowledgeCollections" SET "Status"='retired', "UpdatedAt"=@now
            WHERE "CollectionId"=@collectionId AND "OwnerUserName"=@owner AND "Status"='active';
            UPDATE "LlmWikiKnowledgeCollections" SET "Status"='active', "ContentHash"=@hash, "ActivatedAt"=@now, "UpdatedAt"=@now
            WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner;
            """);
        AddCollectionParameters(command, owner, collection);
        command.Parameters.Add(new NpgsqlParameter("hash", contentHash));
        command.Parameters.Add(new NpgsqlParameter("now", DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateContentHashAsync(SlogsDbContext db, string owner, KnowledgeCollectionInput collection, string contentHash, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(db,
            """
            UPDATE "LlmWikiKnowledgeCollections" SET "ContentHash"=@hash, "UpdatedAt"=@now
            WHERE "CollectionId"=@collectionId AND "Version"=@version AND "OwnerUserName"=@owner;
            """);
        AddCollectionParameters(command, owner, collection);
        command.Parameters.Add(new NpgsqlParameter("hash", contentHash));
        command.Parameters.Add(new NpgsqlParameter("now", DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<SeedChunk>> SearchSeedChunksAsync(
        SlogsDbContext db,
        string owner,
        bool isAdmin,
        string[] scopeKeys,
        string vectorLiteral,
        string[] lexicalTerms,
        string queryText,
        int referenceChapter,
        int referenceVerse,
        string[] exactLocatorAliases,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(db,
            """
            WITH visible AS NOT MATERIALIZED (
                SELECT k.*, c."Domain", d."Title" AS document_title
                FROM "LlmWikiKnowledgeChunks" k
                INNER JOIN "LlmWikiKnowledgeCollections" c ON c."CollectionId"=k."CollectionId" AND c."Version"=k."Version" AND c."OwnerUserName"=k."OwnerUserName"
                INNER JOIN "LlmWikiKnowledgeDocuments" d ON d."CollectionId"=k."CollectionId" AND d."Version"=k."Version" AND d."OwnerUserName"=k."OwnerUserName" AND d."DocumentId"=k."DocumentId"
                WHERE c."Status"='active'
                  AND (
                    c."Visibility"='public_shared'
                    OR (c."OwnerKind"='system' AND @isAdmin)
                    OR (c."OwnerKind"='user' AND c."OwnerKey"=@owner)
                    OR (c."OwnerKind"='organization' AND c."OwnerKey"=ANY(@scopeKeys))
                    OR (c."Visibility"='organization' AND c."ScopeKey"=ANY(@scopeKeys))
                    OR EXISTS (
                        SELECT 1 FROM "LlmWikiKnowledgeCollectionAcl" acl
                        WHERE acl."CollectionId"=c."CollectionId" AND acl."Version"=c."Version" AND acl."OwnerUserName"=c."OwnerUserName"
                          AND ((acl."PrincipalKind"='user' AND acl."PrincipalKey"=@owner)
                            OR (acl."PrincipalKind"='organization' AND acl."PrincipalKey"=ANY(@scopeKeys)))
                    )
                  )
                  AND k."EmbeddingModel"=@model AND k."EmbeddingDimensions"=@dimensions
            ), exact_ranked AS (
                SELECT v."CollectionId", v."Version", v."OwnerUserName", v."ChunkId",
                    ROW_NUMBER() OVER (ORDER BY v."CollectionId", v."Version", v."ChunkId") AS exact_rank
                FROM visible v
                WHERE v."SearchAliasesJson" ?| @exactLocatorAliases
                   OR (@referenceChapter >= 0 AND @referenceVerse >= 0
                     AND POSITION(LOWER(v.document_title) IN LOWER(@queryText)) > 0
                     AND EXISTS (
                       SELECT 1 FROM jsonb_array_elements_text(v."SearchAliasesJson") alias
                       WHERE alias LIKE ('%.' || @referenceChapter::text || '.' || @referenceVerse::text)
                     ))
                ORDER BY v."CollectionId", v."Version", v."ChunkId"
                LIMIT @channelLimit
            ), vector_ranked AS (
                SELECT v."CollectionId", v."Version", v."OwnerUserName", v."ChunkId",
                    ROW_NUMBER() OVER (ORDER BY v."Embedding" <=> CAST(@embedding AS vector), v."ChunkId") AS vector_rank,
                    1-(v."Embedding" <=> CAST(@embedding AS vector)) AS vector_score
                FROM visible v
                ORDER BY v."Embedding" <=> CAST(@embedding AS vector), v."ChunkId"
                LIMIT @channelLimit
            ), query_terms AS (
                SELECT DISTINCT term
                FROM unnest(@lexicalTerms::text[]) AS term
            ), visible_count AS (
                SELECT COUNT(*)::double precision AS total
                FROM visible
            ), term_stats AS (
                SELECT q.term,
                    LN((vc.total+1)/(COUNT(v."ChunkId")+1))+1 AS inverse_document_frequency
                FROM query_terms q
                CROSS JOIN visible_count vc
                LEFT JOIN visible v
                  ON v."SearchVector" @@ to_tsquery('simple', q.term)
                GROUP BY q.term, vc.total
            ), lexical_scored AS (
                SELECT v."CollectionId", v."Version", v."OwnerUserName", v."ChunkId",
                    SUM(t.inverse_document_frequency)::double precision AS lexical_score,
                    MIN(v."Embedding" <=> CAST(@embedding AS vector)) AS vector_distance
                FROM visible v
                INNER JOIN term_stats t
                  ON v."SearchVector" @@ to_tsquery('simple', t.term)
                GROUP BY v."CollectionId", v."Version", v."OwnerUserName", v."ChunkId"
            ), lexical_ranked AS (
                SELECT l."CollectionId", l."Version", l."OwnerUserName", l."ChunkId",
                    ROW_NUMBER() OVER (ORDER BY l.lexical_score DESC, l.vector_distance, l."ChunkId") AS lexical_rank,
                    l.lexical_score
                FROM lexical_scored l
                ORDER BY l.lexical_score DESC, l.vector_distance, l."ChunkId"
                LIMIT @channelLimit
            ), rank_rows AS (
                SELECT v."CollectionId", v."Version", v."OwnerUserName", v."ChunkId",
                    NULL::bigint AS exact_rank, v.vector_rank, v.vector_score, NULL::bigint AS lexical_rank, NULL::real AS lexical_score
                FROM vector_ranked v
                UNION ALL
                SELECT l."CollectionId", l."Version", l."OwnerUserName", l."ChunkId",
                    NULL::bigint, NULL::bigint, NULL::double precision, l.lexical_rank, l.lexical_score
                FROM lexical_ranked l
                UNION ALL
                SELECT e."CollectionId", e."Version", e."OwnerUserName", e."ChunkId",
                    e.exact_rank, NULL::bigint, NULL::double precision, NULL::bigint, NULL::real
                FROM exact_ranked e
            ), fused AS (
                SELECT r."CollectionId", r."Version", r."OwnerUserName", r."ChunkId",
                    MIN(r.exact_rank) AS exact_rank,
                    MIN(r.vector_rank) AS vector_rank,
                    MAX(r.vector_score) AS vector_score,
                    MIN(r.lexical_rank) AS lexical_rank,
                    MAX(r.lexical_score) AS lexical_score,
                    COALESCE(1.0/(@rrfConstant+MIN(r.vector_rank)),0)
                        + COALESCE(1.0/(@rrfConstant+MIN(r.lexical_rank)),0) AS rrf_score
                FROM rank_rows r
                GROUP BY r."CollectionId", r."Version", r."OwnerUserName", r."ChunkId"
            )
            SELECT v."CollectionId", v."Version", v."OwnerUserName", v."Domain", v."DocumentId", v.document_title, v."ChunkId", v."StructureNodeId", v."Text", v."StartLocator", v."EndLocator",
                f.exact_rank IS NOT NULL,
                ROUND(LEAST(GREATEST(COALESCE(f.vector_score,0)*0.8 + LEAST(COALESCE(f.lexical_score,0)*2,0.2),0),1)*100)::integer
            FROM fused f
            INNER JOIN visible v
              ON v."CollectionId"=f."CollectionId" AND v."Version"=f."Version"
             AND v."OwnerUserName"=f."OwnerUserName" AND v."ChunkId"=f."ChunkId"
            ORDER BY CASE WHEN f.exact_rank IS NOT NULL THEN 0 ELSE 1 END,
                f.exact_rank,
                CASE
                    WHEN f.vector_rank<=@channelQuota OR f.lexical_rank<=@channelQuota THEN 0
                    ELSE 1
                END,
                f.rrf_score DESC,
                COALESCE(f.lexical_score,0) DESC,
                COALESCE(f.vector_score,0) DESC,
                v."ChunkId"
            LIMIT @limit;
            """);
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("isAdmin", isAdmin));
        command.Parameters.Add(new NpgsqlParameter("scopeKeys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = scopeKeys });
        command.Parameters.Add(new NpgsqlParameter("model", embeddingService.Model));
        command.Parameters.Add(new NpgsqlParameter("dimensions", embeddingService.Dimensions));
        command.Parameters.Add(new NpgsqlParameter("embedding", vectorLiteral));
        command.Parameters.Add(new NpgsqlParameter("lexicalTerms", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = lexicalTerms });
        command.Parameters.Add(new NpgsqlParameter("queryText", queryText));
        command.Parameters.Add(new NpgsqlParameter("referenceChapter", referenceChapter));
        command.Parameters.Add(new NpgsqlParameter("referenceVerse", referenceVerse));
        command.Parameters.Add(new NpgsqlParameter("exactLocatorAliases", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = exactLocatorAliases });
        command.Parameters.Add(new NpgsqlParameter("channelLimit", Math.Max(limit * 20, 100)));
        command.Parameters.Add(new NpgsqlParameter("channelQuota", CalculateHybridChannelQuota(limit)));
        command.Parameters.Add(new NpgsqlParameter("rrfConstant", ReciprocalRankFusionConstant));
        command.Parameters.Add(new NpgsqlParameter("limit", limit));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<SeedChunk>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SeedChunk(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetBoolean(11), reader.GetInt32(12)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<KnowledgeRelationRecall>> ReadRelationsAsync(SlogsDbContext db, string owner, bool isAdmin, string[] scopeKeys, SeedChunk seed, int maxGraphHops, CancellationToken cancellationToken)
    {
        var startNodes = new[] { seed.ChunkId, seed.StructureNodeId, seed.DocumentId }.Where(value => value is not null).Cast<string>().ToArray();
        await using var command = CreateCommand(db,
            """
            WITH RECURSIVE visible_relations AS (
                SELECT r.* FROM "LlmWikiKnowledgeRelations" r
                INNER JOIN "LlmWikiKnowledgeCollections" c ON c."CollectionId"=r."CollectionId" AND c."Version"=r."Version" AND c."OwnerUserName"=r."OwnerUserName"
                WHERE c."Status"='active' AND r."ReviewStatus" IN ('approved','published')
                  AND (
                    c."Visibility"='public_shared'
                    OR (c."OwnerKind"='system' AND @isAdmin)
                    OR (c."OwnerKind"='user' AND c."OwnerKey"=@owner)
                    OR (c."OwnerKind"='organization' AND c."OwnerKey"=ANY(@scopeKeys))
                    OR (c."Visibility"='organization' AND c."ScopeKey"=ANY(@scopeKeys))
                    OR EXISTS (
                        SELECT 1 FROM "LlmWikiKnowledgeCollectionAcl" acl
                        WHERE acl."CollectionId"=c."CollectionId" AND acl."Version"=c."Version" AND acl."OwnerUserName"=c."OwnerUserName"
                          AND ((acl."PrincipalKind"='user' AND acl."PrincipalKey"=@owner)
                            OR (acl."PrincipalKind"='organization' AND acl."PrincipalKey"=ANY(@scopeKeys)))
                    )
                  )
            ), graph AS (
                SELECT r.*, 1 AS depth,
                    CASE WHEN r."FromNodeId"=ANY(@startNodes) THEN r."ToNodeId" ELSE r."FromNodeId" END AS frontier,
                    ARRAY[r."FromNodeId",r."ToNodeId"]::text[] AS path
                FROM visible_relations r
                WHERE r."FromNodeId"=ANY(@startNodes) OR r."ToNodeId"=ANY(@startNodes)
                UNION ALL
                SELECT r.*, g.depth+1,
                    CASE WHEN r."FromNodeId"=g.frontier THEN r."ToNodeId" ELSE r."FromNodeId" END AS frontier,
                    g.path || CASE WHEN r."FromNodeId"=g.frontier THEN r."ToNodeId" ELSE r."FromNodeId" END
                FROM graph g
                INNER JOIN visible_relations r ON r."FromNodeId"=g.frontier OR r."ToNodeId"=g.frontier
                WHERE g.depth<@maxGraphHops
                  AND NOT (CASE WHEN r."FromNodeId"=g.frontier THEN r."ToNodeId" ELSE r."FromNodeId" END=ANY(g.path))
            ), deduplicated AS (
                SELECT DISTINCT ON (g."CollectionId",g."Version",g."OwnerUserName",g."RelationId") g.*
                FROM graph g
                ORDER BY g."CollectionId",g."Version",g."OwnerUserName",g."RelationId",g.depth
            )
            SELECT g."CollectionId", g."Version", g."RelationType", g."FromNodeId", g."ToNodeId", g."ClaimClass", g."Confidence", g."EvidenceJson"::text,
                COALESCE(from_entity."CanonicalLabel", from_structure."Label", g."FromNodeId"),
                COALESCE(from_entity."AliasesJson"::text, '[]'),
                COALESCE(to_entity."CanonicalLabel", to_structure."Label", g."ToNodeId"),
                COALESCE(to_entity."AliasesJson"::text, '[]')
            FROM deduplicated g
            LEFT JOIN "LlmWikiKnowledgeEntities" from_entity
              ON from_entity."CollectionId"=g."CollectionId" AND from_entity."Version"=g."Version"
             AND from_entity."OwnerUserName"=g."OwnerUserName" AND from_entity."EntityId"=g."FromNodeId"
            LEFT JOIN "LlmWikiKnowledgeStructureNodes" from_structure
              ON from_structure."CollectionId"=g."CollectionId" AND from_structure."Version"=g."Version"
             AND from_structure."OwnerUserName"=g."OwnerUserName" AND from_structure."NodeId"=g."FromNodeId"
            LEFT JOIN "LlmWikiKnowledgeEntities" to_entity
              ON to_entity."CollectionId"=g."CollectionId" AND to_entity."Version"=g."Version"
             AND to_entity."OwnerUserName"=g."OwnerUserName" AND to_entity."EntityId"=g."ToNodeId"
            LEFT JOIN "LlmWikiKnowledgeStructureNodes" to_structure
             ON to_structure."CollectionId"=g."CollectionId" AND to_structure."Version"=g."Version"
             AND to_structure."OwnerUserName"=g."OwnerUserName" AND to_structure."NodeId"=g."ToNodeId"
            ORDER BY CASE WHEN g."RelationType" IN ('contains_passage','contains','part_of') THEN 1 ELSE 0 END,
                g.depth, g."Confidence" DESC, g."RelationId"
            LIMIT 30;
            """);
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("isAdmin", isAdmin));
        command.Parameters.Add(new NpgsqlParameter("scopeKeys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = scopeKeys });
        command.Parameters.Add(new NpgsqlParameter("startNodes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = startNodes });
        command.Parameters.Add(new NpgsqlParameter("maxGraphHops", maxGraphHops));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<KnowledgeRelationRecall>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new KnowledgeRelationRecall(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetDouble(6),
                JsonSerializer.Deserialize<KnowledgeEvidenceInput[]>(reader.GetString(7)) ?? [],
                reader.GetString(8),
                JsonSerializer.Deserialize<string[]>(reader.GetString(9)) ?? [],
                reader.GetString(10),
                JsonSerializer.Deserialize<string[]>(reader.GetString(11)) ?? []));
        }

        return results;
    }

    private static string BuildChunkSearchText(KnowledgeCollectionInput collection, KnowledgeChunkInput chunk)
        => $"collection: {collection.Title}\ndomain: {collection.Domain}\ndocument: {chunk.DocumentId}\nlocator: {chunk.StartLocator}..{chunk.EndLocator}\naliases: {string.Join(", ", chunk.SearchAliases ?? [])}\n{chunk.Text}";

    private static string BuildRerankPassage(SeedChunk seed)
        => $"domain: {seed.Domain}\ndocument: {seed.DocumentTitle}\nlocator: {seed.StartLocator}..{seed.EndLocator}\n{seed.Text}";

    private static string BuildLexicalTsQuery(string query)
        => string.Join(" | ", BuildLexicalTerms(query));

    private static string[] BuildLexicalTerms(string query)
    {
        var terms = Regex.Matches(query.Normalize(NormalizationForm.FormKC), @"[\p{L}\p{N}]+")
            .Select(match => match.Value.ToLowerInvariant())
            .Where(term => term.Length >= 2)
            .Where(term => !LexicalQuestionWords.Contains(term))
            .SelectMany(ExpandLexicalTerm)
            .Where(term => !LexicalQuestionWords.Contains(term))
            .Distinct(StringComparer.Ordinal)
            .Take(48)
            .ToArray();

        return terms.Length == 0 ? ["slogsnoqueryterms"] : terms;
    }

    private static HierarchicalReference? TryExtractHierarchicalReference(string query)
    {
        var match = Regex.Match(
            query.Normalize(NormalizationForm.FormKC),
            @"(?<chapter>[1-9][0-9]*)\s*(?:장\s*(?<verse>[0-9]+)\s*절?|[:：]\s*(?<verse>[0-9]+))",
            RegexOptions.CultureInvariant);
        return match.Success
            ? new(int.Parse(match.Groups["chapter"].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups["verse"].Value, System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    private static string[] ExtractCanonicalLocatorAliases(string query)
        => Regex.Matches(
                query.Normalize(NormalizationForm.FormKC),
                @"(?<reference>[1-3]?[A-Za-z]{2,}\.[1-9][0-9]*\.[0-9]+)",
                RegexOptions.CultureInvariant)
            .Select(match => $"passage:{match.Groups["reference"].Value}")
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();

    private static IEnumerable<string> ExpandLexicalTerm(string term)
    {
        yield return term;
        var current = term;
        foreach (var suffix in KoreanLexicalSuffixes)
        {
            if (current.Length - suffix.Length < 2 || !current.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            current = current[..^suffix.Length];
            yield return current;
            break;
        }

        if (current.Length >= 4 && current.EndsWith('인'))
        {
            yield return current[..^1];
        }
        if (current.Length >= 3 && current.EndsWith('서'))
        {
            yield return current[..^1];
        }
    }

    private static readonly string[] KoreanLexicalSuffixes =
    [
        "으로부터", "에게서", "한테서", "이라고", "라고", "으로", "에서", "에게", "한테",
        "께서", "처럼", "보다", "까지", "부터", "이나", "이나마", "이라도", "라도",
        "은", "는", "이", "가", "을", "를", "의", "에", "로", "와", "과", "도", "만"
    ];

    private static readonly HashSet<string> LexicalQuestionWords = new(StringComparer.Ordinal)
    {
        "어느", "무엇", "무엇인가", "누구", "누구인가", "어디", "언제", "어떻게", "왜"
    };

    private static int CalculateBgeM3CandidateLimit(int requestedLimit)
        => Math.Min(
            Math.Max(
                requestedLimit,
                Math.Min(requestedLimit * 2, MaxBgeM3OnlineRerankCandidates)),
            10);

    private static int CalculateHybridChannelQuota(int candidateLimit)
        => Math.Max(1, (int)Math.Floor(candidateLimit * 0.4));

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? values)
        => (values ?? new Dictionary<string, string>())
            .ToDictionary(pair => Normalize(pair.Key, 120, "metadata key"), pair => Normalize(pair.Value, 2_000, "metadata value"), StringComparer.Ordinal);

    private static IReadOnlyList<string> NormalizeAliases(IReadOnlyList<string>? values)
        => (values ?? []).Select(value => Normalize(value, 240, "alias")).Distinct(StringComparer.Ordinal).ToArray();

    private static void ValidateBatch(int count, int maximum, string name)
    {
        if (count > maximum)
        {
            throw new InvalidDataException($"{name} 배치는 최대 {maximum}개입니다.");
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string field)
    {
        var array = values.ToArray();
        if (array.Distinct(StringComparer.Ordinal).Count() != array.Length)
        {
            throw new InvalidDataException($"한 배치에 중복된 {field}가 있습니다.");
        }
    }

    private static string Normalize(string value, int maxLength, string field)
    {
        var result = value?.Normalize(NormalizationForm.FormKC).Trim() ?? string.Empty;
        if (result.Length == 0 || result.Length > maxLength)
        {
            throw new InvalidDataException($"{field} 길이가 유효하지 않습니다: {result.Length}");
        }

        return result;
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string ToVectorLiteral(IReadOnlyList<float> values)
        => $"[{string.Join(',', values.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}]";

    private static NpgsqlCommand CreateCommand(SlogsDbContext db, string commandText)
    {
        var command = (NpgsqlCommand)db.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        return command;
    }

    private static void AddCollectionParameters(NpgsqlCommand command, string owner, KnowledgeCollectionInput collection)
        => AddIdentityParameters(command, owner, collection.CollectionId, collection.Version);

    private static void AddIdentityParameters(NpgsqlCommand command, string owner, string collectionId, string version)
    {
        command.Parameters.Add(new NpgsqlParameter("collectionId", collectionId));
        command.Parameters.Add(new NpgsqlParameter("version", version));
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
    }

    private static async Task EnsureConnectionOpenAsync(SlogsDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }
    }

    private sealed record EmbeddedChunk(KnowledgeChunkInput Chunk, string SearchText, string ContentHash, IReadOnlyList<float> Embedding);
    private sealed record CollectionAccessResolution(
        string StorageOwnerUserName,
        bool Exists,
        string? ExistingVisibility,
        string? ExistingScopeKey);
    private sealed record CorpusCounts(int Documents, int Structures, int Chunks, int Entities, int Relations);
    private sealed record HierarchicalReference(int Chapter, int Verse);
    private sealed record SeedChunk(
        string CollectionId,
        string Version,
        string OwnerUserName,
        string Domain,
        string DocumentId,
        string DocumentTitle,
        string ChunkId,
        string? StructureNodeId,
        string Text,
        string StartLocator,
        string EndLocator,
        bool ExactLocatorMatch,
        int RelevancePercent);
}
