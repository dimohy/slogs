using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class LlmWikiGrowingGraphTests
{
    [Fact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Remember_atomically_validates_and_grows_memory_and_corpus_relations()
    {
        var connectionString = Environment.GetEnvironmentVariable("SLOGS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var options = new DbContextOptionsBuilder<SlogsDbContext>()
            .UseNpgsql(dataSource)
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            await InvokeEnsureSchemaAsync(db);
            db.Users.AddRange(User("owner"), User("other"));
            await db.SaveChangesAsync();
        }

        var embedding = new FixedBgeM3EmbeddingService();
        var service = new LlmWikiService(factory, embedding);
        var target = await service.RememberAsync(
            "owner",
            new LlmWikiRememberRequest(
                "기존 설계의 핵심은 승인된 근거만 사용한다.",
                "기존 설계 근거",
                "기존 설계",
                "design",
                "graph/growing"));
        var foreign = await service.RememberAsync(
            "other",
            new LlmWikiRememberRequest(
                "다른 사용자의 비공개 기억",
                "격리 근거",
                "다른 사용자 기억",
                "private",
                "graph/growing"));
        var sentinelA = await service.RememberAsync(
            "owner",
            new LlmWikiRememberRequest("격리알파", "알파고유", "격리알파", "alpha", "isolated/sentinel"));
        var sentinelB = await service.RememberAsync(
            "owner",
            new LlmWikiRememberRequest("격리베타", "베타고유", "격리베타", "beta", "isolated/sentinel"));
        var sentinelUpdatedAt = DateTime.UtcNow.AddDays(-1);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "LlmWikiGraphEdges"
                    ("OwnerUserName", "FromEntryId", "ToEntryId", "EdgeScore", "IndexVersion", "UpdatedAt")
                VALUES ({"owner"}, {sentinelA.Id}, {sentinelB.Id}, {0.75},
                    {LlmWikiGraphSearchCommand.GraphIndexVersion}, {sentinelUpdatedAt})
                ON CONFLICT ("OwnerUserName", "FromEntryId", "ToEntryId") DO UPDATE SET
                    "EdgeScore"=EXCLUDED."EdgeScore", "UpdatedAt"=EXCLUDED."UpdatedAt";
                """);
        }

        var countBeforeInvalid = await CountEntriesAsync(factory, "owner");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RememberAsync(
            "owner",
            new LlmWikiRememberRequest(
                "새 기억은 관련 근거를 연결한다.",
                "새 기억 관계 근거",
                "잘못된 관계",
                "growing",
                "graph/growing",
                [MemoryRelation(target.Id, "새 기억 관계 근거", "존재하지 않는 대상 근거")])));
        Assert.Equal(countBeforeInvalid, await CountEntriesAsync(factory, "owner"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RememberAsync(
            "owner",
            new LlmWikiRememberRequest(
                "새 기억은 관련 근거를 연결한다.",
                "새 기억 관계 근거",
                "권한 위반 관계",
                "growing",
                "graph/growing",
                [MemoryRelation(foreign.Id, "새 기억 관계 근거", "격리 근거")])));
        Assert.Equal(countBeforeInvalid, await CountEntriesAsync(factory, "owner"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RememberAsync(
            "owner",
            new LlmWikiRememberRequest(
                "낮은 신뢰 관계는 활성화하지 않는다.",
                "낮은 신뢰 근거",
                "낮은 신뢰 관계",
                "growing",
                "graph/growing",
                [MemoryRelation(target.Id, "낮은 신뢰 근거", "기존 설계 근거") with { Confidence = 0.69 }])));
        Assert.Equal(countBeforeInvalid, await CountEntriesAsync(factory, "owner"));
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(0, await db.Database.SqlQuery<int>(
                $"""SELECT COUNT(*)::integer AS "Value" FROM "LlmWikiEntrySemanticRelations" WHERE "OwnerUserName"='owner'""").SingleAsync());
        }

        var stored = await service.RememberAsync(
            "owner",
            new LlmWikiRememberRequest(
                "새 기억은 기존 설계를 구체화한다.",
                "새 기억 관계 근거",
                "성장형 그래프 기억",
                "growing",
                "graph/growing",
                [MemoryRelation(target.Id, "새 기억 관계 근거", "기존 설계 근거")]));

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.Database.SqlQuery<int>(
                $"""SELECT COUNT(*)::integer AS "Value" FROM "LlmWikiEntrySemanticRelations" WHERE "AnchorEntryId"={stored.Id} AND "State"='active'""").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQuery<int>(
                $"""SELECT COUNT(*)::integer AS "Value" FROM "LlmWikiEntryEmbeddings" WHERE "EntryId"={stored.Id} AND "Model"='bge-m3' AND "Dimensions"=1024""").SingleAsync());
            var unchanged = await db.Database.SqlQuery<DateTime>(
                $"""SELECT "UpdatedAt" AS "Value" FROM "LlmWikiGraphEdges" WHERE "OwnerUserName"='owner' AND "FromEntryId"={sentinelA.Id} AND "ToEntryId"={sentinelB.Id}""").SingleAsync();
            Assert.InRange(Math.Abs((sentinelUpdatedAt - unchanged).TotalMilliseconds), 0, 0.01);
        }

        var related = await service.SearchAsync(
            "owner", "새 기억은 기존 설계를 구체화한다", 10,
            minRelevancePercent: 0, categoryPath: "graph/growing", maxGraphHops: 2);
        var relatedTarget = Assert.Single(related, result => result.Id == target.Id);
        Assert.Equal(1, relatedTarget.GraphDepth);
        Assert.Equal("refines", relatedTarget.SemanticPath);

        await service.UpdateAsync(
            "owner",
            stored.Id.ToString(),
            new LlmWikiUpdateRequest(
                "관계 근거가 제거된 완전히 변경된 기억",
                string.Empty,
                stored.Title,
                "growing",
                "graph/growing"));
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.Database.SqlQuery<int>(
                $"""SELECT COUNT(*)::integer AS "Value" FROM "LlmWikiEntrySemanticRelations" WHERE "AnchorEntryId"={stored.Id} AND "State"='retired'""").SingleAsync());
        }

        var corpus = new KnowledgeCorpusService(factory, embedding);
        var actor = KnowledgeCorpusActor.User("owner");
        const string collectionId = "growing-graph-manual";
        const string version = "1.0.0";
        const string chunkId = "chunk:growing-graph";
        await corpus.IngestAsync(actor, new KnowledgeCorpusIngestRequest(
            new KnowledgeCollectionInput(
                collectionId, version, "성장형 그래프 매뉴얼", "technology", "ko", "private",
                "urn:test:growing-graph", "user", "owner", "private", null, false, 1),
            [new KnowledgeDocumentInput("doc:growing", "성장형 그래프 매뉴얼", "manual", 0, "urn:test:growing-graph#doc")],
            [],
            [new KnowledgeChunkInput(
                chunkId, "doc:growing", null, 0, "관계는 양쪽의 명시적 근거를 검증한다.",
                "manual/p1", "manual/p1", null, null, 0, 8, "unicode-word-estimate-v1")],
            [],
            [],
            Activate: true));
        var corpusMemory = await service.RememberAsync(
            "owner",
            new LlmWikiRememberRequest(
                "코퍼스 관계도 근거를 검증한다.",
                "코퍼스 연결 근거",
                "코퍼스 연결 기억",
                "growing,corpus",
                "graph/growing",
                [new LlmWikiRelationInput(
                    LlmWikiRelationTargetKinds.KnowledgeChunk,
                    "supports",
                    LlmWikiRelationDirections.Outgoing,
                    0.96,
                    "코퍼스 연결 근거",
                    "명시적 근거를 검증한다",
                    TargetCollectionId: collectionId,
                    TargetVersion: version,
                    TargetOwnerUserName: "owner",
                    TargetChunkId: chunkId)]),
            corpusActor: actor);
        var links = await service.GetKnowledgeLinksAsync("owner", [corpusMemory.Id]);
        var accessible = await corpus.ReadLinkedChunksAsync(actor, links, 2);
        var linkedChunk = Assert.Single(accessible);
        Assert.Equal(chunkId, linkedChunk.ChunkId);
        Assert.Contains(linkedChunk.Relations, relation => relation.RelationType == "supports"
            && relation.ClaimClass == "agent-reviewed");
        Assert.Empty(await corpus.ReadLinkedChunksAsync(KnowledgeCorpusActor.User("outsider"), links, 2));
    }

    private static LlmWikiRelationInput MemoryRelation(Guid targetEntryId, string anchorEvidence, string targetEvidence)
        => new(
            LlmWikiRelationTargetKinds.Memory,
            "refines",
            LlmWikiRelationDirections.Outgoing,
            0.95,
            anchorEvidence,
            targetEvidence,
            TargetEntryId: targetEntryId);

    private static async Task<int> CountEntriesAsync(IDbContextFactory<SlogsDbContext> factory, string owner)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.LlmWikiEntries.CountAsync(entry => entry.OwnerUserName == owner);
    }

    private static async Task InvokeEnsureSchemaAsync(SlogsDbContext db)
    {
        var method = typeof(SlogsDbInitializer).GetMethod("EnsureSchemaAsync", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("EnsureSchemaAsync를 찾을 수 없습니다.");
        var task = method.Invoke(null, [db]) as Task
            ?? throw new InvalidOperationException("EnsureSchemaAsync 호출 결과가 Task가 아닙니다.");
        await task;
    }

    private static UserRecord User(string userName)
        => new()
        {
            UserName = userName,
            DisplayName = userName,
            Email = $"{userName}@example.invalid",
            Password = "test-only",
            RegisteredAt = DateTime.UtcNow
        };

    private sealed class TestDbContextFactory(DbContextOptions<SlogsDbContext> options)
        : IDbContextFactory<SlogsDbContext>
    {
        public SlogsDbContext CreateDbContext() => new(options);

        public Task<SlogsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private sealed class FixedBgeM3EmbeddingService : IKnowledgeEmbeddingService
    {
        private static readonly IReadOnlyList<float> Vector = Enumerable.Repeat(0.01f, 1024).ToArray();

        public string Model => "bge-m3";
        public int Dimensions => 1024;
        public bool SupportsFullFunctionReranking => true;

        public Task<IReadOnlyList<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult(Vector);

        public Task<IReadOnlyList<float>> EmbedDocumentAsync(string document, CancellationToken cancellationToken)
            => Task.FromResult(Vector);

        public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedDocumentsAsync(
            IReadOnlyList<string> documents,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>(documents.Select(_ => Vector).ToArray());

        public Task<IReadOnlyList<KnowledgeRerankScore>> ScorePairsAsync(
            string query,
            IReadOnlyList<string> passages,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<KnowledgeRerankScore>>(
                passages.Select(_ => new KnowledgeRerankScore(0.9f, 0.9f, 0.9f, 0.9f)).ToArray());
    }
}
