using System.Reflection;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class KnowledgeCorpusIntegrationTests
{
    [Fact]
    public async Task GenericCorpusIngestsChunksActivatesVersionAndRecallsRelations()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SLOGS_KNOWLEDGE_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        const string owner = "knowledge-integration-user";
        var collectionId = $"equipment-manual-{Guid.NewGuid():N}";
        const string version = "1.0.0";
        var services = new ServiceCollection();
        services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(
            "Host=localhost;Port=54329;Database=slogs;Username=slogs;Password=slogs_dev_password"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await InvokeEnsureSchemaAsync(db);

        var corpus = new KnowledgeCorpusService(factory, CreateEmbeddingService());
        var chunker = new KnowledgeChunkingService();
        var chunks = chunker.CreateChunks(
            collectionId,
            version,
            "doc:manual-a",
            "section:diagnostics",
            [
                new KnowledgeTextUnit("p1", "manual/diagnostics/p1", "경고가 반복되면 먼저 센서 배선을 점검한다."),
                new KnowledgeTextUnit("p2", "manual/diagnostics/p2", "배선이 정상이면 센서 기준값을 확인한다.")
            ],
            new KnowledgeChunkingOptions(TargetTokens: 8, MaxTokens: 30, MinTokens: 1, OverlapUnits: 0));
        var targetChunk = chunks[0];
        var request = new KnowledgeCorpusIngestRequest(
            new KnowledgeCollectionInput(
                collectionId,
                version,
                "장비 진단 매뉴얼",
                "industrial-technology",
                "ko",
                "proprietary-internal",
                "urn:test:equipment-manual",
                "user",
                owner,
                "private",
                null,
                false,
                chunks.Count),
            [new KnowledgeDocumentInput("doc:manual-a", "장비 진단 매뉴얼 A", "manual", 0, "urn:test:equipment-manual#a")],
            [new KnowledgeStructureInput("section:diagnostics", "doc:manual-a", null, "section", "진단", 0, "manual/diagnostics")],
            chunks,
            [new KnowledgeEntityInput("entity:sensor-wiring", "component", "센서 배선", ["배선"])],
            [new KnowledgeRelationInput(
                "relation:wiring-remedy",
                "entity:sensor-wiring",
                "recommended_check_for",
                targetChunk.ChunkId,
                "source_explicit",
                "approved",
                1.0,
                [new KnowledgeEvidenceInput(collectionId, targetChunk.StartLocator, "source_chunk", [targetChunk.ChunkId])],
                "deterministic_import")],
            Activate: true);

        try
        {
            var ingested = await corpus.IngestAsync(owner, isAdmin: false, request);
            Assert.Equal("active", ingested.Status);
            Assert.Equal(chunks.Count, ingested.ChunkCount);
            Assert.Equal(1, ingested.RelationCount);

            var recalled = await corpus.RecallAsync(owner, "경고가 반복되면 무엇을 점검해야 하나?", limit: 3, maxGraphHops: 2);
            var result = Assert.Single(recalled, item => item.CollectionId == collectionId);
            Assert.Contains("센서 배선", result.Text);
            Assert.Contains(result.Relations, relation => relation.RelationType == "recommended_check_for");
            Assert.Equal("manual/diagnostics/p1", result.StartLocator);
        }
        finally
        {
            await db.Database.ExecuteSqlAsync(
                $"DELETE FROM \"LlmWikiKnowledgeCollections\" WHERE \"CollectionId\" = {collectionId} AND \"OwnerUserName\" = {owner};");
        }
    }

    [Fact]
    public async Task PublicReadDoesNotGrantWriteAndEditorCannotManageAcl()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SLOGS_KNOWLEDGE_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var owner = $"public-owner-{suffix}";
        var editor = $"public-editor-{suffix}";
        var outsider = $"public-outsider-{suffix}";
        var collectionId = $"public-manual-{suffix}";
        const string version = "1.0.0";
        var services = new ServiceCollection();
        services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(
            "Host=localhost;Port=54329;Database=slogs;Username=slogs;Password=slogs_dev_password"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await InvokeEnsureSchemaAsync(db);
        var corpus = new KnowledgeCorpusService(factory, CreateEmbeddingService());
        var collection = new KnowledgeCollectionInput(
            collectionId,
            version,
            "공개 장비 매뉴얼",
            "industrial-technology",
            "ko",
            "CC-BY-4.0",
            "urn:test:public-manual",
            "user",
            owner,
            "public_shared",
            null,
            true,
            1);
        var chunk = new KnowledgeChunkInput(
            $"chunk:{suffix}",
            "doc:public-manual",
            null,
            0,
            "공개 매뉴얼의 센서 배선 점검 절차입니다.",
            "manual/p1",
            "manual/p1",
            null,
            null,
            0,
            9,
            "unicode-word-estimate-v1");
        var create = new KnowledgeCorpusIngestRequest(
            collection,
            [new KnowledgeDocumentInput("doc:public-manual", "공개 장비 매뉴얼", "manual", 0, "urn:test:public-manual#doc")],
            [],
            [chunk],
            [],
            [],
            Activate: true);

        try
        {
            await corpus.IngestAsync(KnowledgeCorpusActor.User(owner, isAdmin: true), create);
            var publicRecall = await corpus.RecallAsync(outsider, "센서 배선 점검", limit: 3, maxGraphHops: 0);
            Assert.Contains(publicRecall, item => item.CollectionId == collectionId);

            var metadataOnly = new KnowledgeCorpusIngestRequest(collection, [], [], [], [], []);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                corpus.IngestAsync(KnowledgeCorpusActor.User(editor), metadataOnly));

            var grantEditor = metadataOnly with
            {
                Acl = [new KnowledgeAclGrantInput("user", editor, "editor")]
            };
            await corpus.IngestAsync(KnowledgeCorpusActor.User(owner, isAdmin: true), grantEditor);
            await corpus.IngestAsync(KnowledgeCorpusActor.User(editor), metadataOnly);

            var editorAclChange = metadataOnly with
            {
                Acl = [new KnowledgeAclGrantInput("user", outsider, "reader")]
            };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                corpus.IngestAsync(KnowledgeCorpusActor.User(editor), editorAclChange));

            await corpus.IngestAsync(
                KnowledgeCorpusActor.User(owner, isAdmin: true),
                metadataOnly with { Acl = [new KnowledgeAclGrantInput("user", editor, "maintainer")] });
            await corpus.IngestAsync(KnowledgeCorpusActor.User(editor), editorAclChange);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                corpus.IngestAsync(KnowledgeCorpusActor.User(outsider), metadataOnly));
        }
        finally
        {
            await db.Database.ExecuteSqlAsync(
                $"DELETE FROM \"LlmWikiKnowledgeCollections\" WHERE \"CollectionId\" = {collectionId};");
        }
    }

    [Fact]
    public async Task OrganizationOwnershipUsesVerifiedRoleAndExplicitEditorAcl()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SLOGS_KNOWLEDGE_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var organizationKey = Guid.NewGuid().ToString("D");
        var owner = $"org-owner-{suffix}";
        var member = $"org-member-{suffix}";
        var outsider = $"org-outsider-{suffix}";
        var collectionId = $"org-manual-{suffix}";
        const string version = "1.0.0";
        var services = new ServiceCollection();
        services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(
            "Host=localhost;Port=54329;Database=slogs;Username=slogs;Password=slogs_dev_password"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await InvokeEnsureSchemaAsync(db);
        var corpus = new KnowledgeCorpusService(factory, CreateEmbeddingService());
        var collection = new KnowledgeCollectionInput(
            collectionId,
            version,
            "조직 소유 기술서",
            "company-knowledge",
            "ko",
            "proprietary-internal",
            "urn:test:org-manual",
            "organization",
            organizationKey,
            "private",
            null,
            false,
            1);
        var chunk = new KnowledgeChunkInput(
            $"chunk:{suffix}",
            "doc:org-manual",
            null,
            0,
            "조직 전용 캘리브레이션 절차입니다.",
            "org-manual/p1",
            "org-manual/p1",
            null,
            null,
            0,
            7,
            "unicode-word-estimate-v1");
        var create = new KnowledgeCorpusIngestRequest(
            collection,
            [new KnowledgeDocumentInput("doc:org-manual", "조직 소유 기술서", "manual", 0, "urn:test:org-manual#doc")],
            [],
            [chunk],
            [],
            [],
            Activate: true);
        var ownerActor = new KnowledgeCorpusActor(
            owner,
            false,
            new Dictionary<string, string>(StringComparer.Ordinal) { [organizationKey] = OrganizationRoles.Owner });
        var memberActor = new KnowledgeCorpusActor(
            member,
            false,
            new Dictionary<string, string>(StringComparer.Ordinal) { [organizationKey] = OrganizationRoles.Member });

        try
        {
            await corpus.IngestAsync(ownerActor, create);
            var memberRecall = await corpus.RecallAsync(member, "캘리브레이션", 3, 0, [organizationKey]);
            Assert.Contains(memberRecall, item => item.CollectionId == collectionId);
            var outsiderRecall = await corpus.RecallAsync(outsider, "캘리브레이션", 10, 0);
            Assert.DoesNotContain(outsiderRecall, item => item.CollectionId == collectionId);

            var metadataOnly = new KnowledgeCorpusIngestRequest(collection, [], [], [], [], []);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => corpus.IngestAsync(memberActor, metadataOnly));

            var grantOrganizationEditor = metadataOnly with
            {
                Acl = [new KnowledgeAclGrantInput("organization", organizationKey, "editor")]
            };
            await corpus.IngestAsync(ownerActor, grantOrganizationEditor);
            await corpus.IngestAsync(memberActor, metadataOnly);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => corpus.IngestAsync(
                memberActor,
                metadataOnly with { Acl = [new KnowledgeAclGrantInput("user", outsider, "reader")] }));
        }
        finally
        {
            await db.Database.ExecuteSqlAsync(
                $"DELETE FROM \"LlmWikiKnowledgeCollections\" WHERE \"CollectionId\" = {collectionId};");
        }
    }

    [Fact]
    public async Task SystemOwnedPrivateCorpusIsAdminOnly()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SLOGS_KNOWLEDGE_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var collectionId = $"system-corpus-{suffix}";
        var services = new ServiceCollection();
        services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(
            "Host=localhost;Port=54329;Database=slogs;Username=slogs;Password=slogs_dev_password"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await InvokeEnsureSchemaAsync(db);
        var corpus = new KnowledgeCorpusService(factory, CreateEmbeddingService());
        var collection = new KnowledgeCollectionInput(
            collectionId, "1", "시스템 내부 지식", "system", "ko", "internal", "urn:test:system",
            "system", "slogs", "private", null, false, 1);
        var create = new KnowledgeCorpusIngestRequest(
            collection,
            [new KnowledgeDocumentInput("doc:system", "시스템 내부 지식", "manual", 0, "urn:test:system#doc")],
            [],
            [new KnowledgeChunkInput(
                $"chunk:{suffix}", "doc:system", null, 0, "관리자 전용 시스템 운영 절차", "system/p1", "system/p1",
                null, null, 0, 6, "unicode-word-estimate-v1")],
            [],
            [],
            Activate: true);
        var admin = KnowledgeCorpusActor.User("dimohy", isAdmin: true);

        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                corpus.IngestAsync(KnowledgeCorpusActor.User("ordinary-user"), create));
            await corpus.IngestAsync(admin, create);
            var ordinaryRecall = await corpus.RecallAsync(KnowledgeCorpusActor.User("ordinary-user"), "시스템 운영", 10, 0);
            Assert.DoesNotContain(ordinaryRecall, item => item.CollectionId == collectionId);
            var adminRecall = await corpus.RecallAsync(admin, "시스템 운영", 10, 0);
            Assert.Contains(adminRecall, item => item.CollectionId == collectionId);
        }
        finally
        {
            await db.Database.ExecuteSqlAsync(
                $"DELETE FROM \"LlmWikiKnowledgeCollections\" WHERE \"CollectionId\" = {collectionId};");
        }
    }

    [Fact]
    public async Task BibleAdapterIngestsPassageGraphAndRecallsSaulAsPaulWithVerseEvidence()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SLOGS_KNOWLEDGE_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var owner = $"bible-owner-{suffix}";
        var collectionId = $"bible-fixture-{suffix}";
        var adapter = new BibleKnowledgeCorpusAdapter(new KnowledgeChunkingService());
        var verse = BibleKnowledgeCorpusAdapterTests.Verse(
            "Acts.13.9", 13, 9, "바울이라고 하는 사울이 성령이 충만하여 그를 주목하고");
        var plan = adapter.CreatePlan(
            new BibleCorpusOptions(
                collectionId, "1.0.0", "개역개정 사도행전 fixture", "restricted", "urn:test:bible:acts",
                "user", owner, "private", null, false,
                Chunking: new KnowledgeChunkingOptions(TargetTokens: 80, MaxTokens: 120, MinTokens: 1, OverlapUnits: 0)),
            [verse],
            [
                new BibleEntityCorpusInput(
                    "entity:paul", "person", "바울", ["Paul", "Saul", "사울"], null, ["G3972", "G4569"], "fixture")
            ],
            [
                new BibleRelationCorpusInput(
                    "edge:acts-13-9:mentions-paul", "passage:Acts.13.9", "mentions", "entity:paul",
                    "source_explicit", "approved", 1.0,
                    [new BibleRelationEvidenceInput("fixture", "Acts.13.9", "source_verse", "Acts.13.9")],
                    "fixture")
            ]);

        var services = new ServiceCollection();
        services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(
            "Host=localhost;Port=54329;Database=slogs;Username=slogs;Password=slogs_dev_password"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await InvokeEnsureSchemaAsync(db);
        var corpus = new KnowledgeCorpusService(factory, CreateEmbeddingService());
        var checkpointPath = Path.Combine(
            Path.GetTempPath(), $"slogs-bible-import-checkpoint-{suffix}.json");

        try
        {
            var runner = new BibleCorpusImportRunner(corpus);
            var checkpoint = await runner.RunAsync(
                KnowledgeCorpusActor.User(owner), plan, new string('A', 64), checkpointPath);
            Assert.Equal("complete", checkpoint.State);
            Assert.Equal(checkpoint.TotalBatches, checkpoint.NextBatchIndex);
            Assert.True(File.Exists(checkpointPath));
            var resumed = await runner.RunAsync(
                KnowledgeCorpusActor.User(owner), plan, new string('A', 64), checkpointPath);
            Assert.Equal(checkpoint, resumed);
            await Assert.ThrowsAsync<InvalidDataException>(() => runner.RunAsync(
                KnowledgeCorpusActor.User(owner), plan, new string('B', 64), checkpointPath));

            var recalled = await corpus.RecallAsync(owner, "사울은 바울과 같은 사람인가?", limit: 3, maxGraphHops: 2);
            var result = Assert.Single(recalled, item => item.CollectionId == collectionId);
            Assert.Equal("Acts.13.9", result.StartLocator);
            var identity = Assert.Single(result.Relations, relation => relation.RelationType == "mentions");
            Assert.Equal("entity:paul", identity.ToNodeId);
            Assert.Equal("바울", identity.ToLabel);
            Assert.Contains("사울", identity.ToAliases!);
            Assert.Contains("Saul", identity.ToAliases!);
            Assert.Contains(identity.Evidence, evidence =>
                evidence.Locator == "Acts.13.9" && evidence.ChunkIds!.Contains(result.ChunkId, StringComparer.Ordinal));
        }
        finally
        {
            await db.Database.ExecuteSqlAsync(
                $"DELETE FROM \"LlmWikiKnowledgeCollections\" WHERE \"CollectionId\" = {collectionId};");
            if (File.Exists(checkpointPath))
            {
                File.Delete(checkpointPath);
            }
        }
    }

    [Fact]
    public async Task OriginalBibleAdapterRecallsMorphologyAndPaulSaulAliasFromPublicScholarlyLayer()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SLOGS_KNOWLEDGE_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var collectionId = $"bible-original-fixture-{suffix}";
        var coordinate = BibleKnowledgeCorpusAdapterTests.Verse("Acts.13.9", 13, 9, "restricted coordinate text");
        var token = new BibleOriginalTokenCorpusInput(
            "token:Acts.13.9:001:K", "Acts.13.9", 1, "grc", "Σαῦλος", "Saulos", "Saul",
            "G4569", "N-NSM-P", "Σαῦλος", "Saul", false, "K", "step-tagnt", ["Paul@Acts.7.58"]);
        var plan = new BibleOriginalKnowledgeCorpusAdapter(new KnowledgeChunkingService()).CreatePlan(
            new BibleOriginalCorpusOptions(
                collectionId, "1.0.0", "STEP 원문 fixture", "CC BY 4.0", "urn:test:step",
                "system", "slogs", "public_shared", null, true, RequireAllBooks: false,
                Chunking: new KnowledgeChunkingOptions(TargetTokens: 80, MaxTokens: 120, MinTokens: 1, OverlapUnits: 0)),
            [coordinate],
            [token],
            [new BibleEntityCorpusInput(
                "entity:step:G3972G", "Male", "Paul", ["Paul", "Saul", "Παῦλος", "Σαῦλος"],
                "Apostle", ["G3972", "G4569"], "step-tipnr")],
            [
                new BibleGraphEdgeCorpusInput(
                    "edge:mention:Acts.13.9:entity:step:G3972G", "passage:Acts.13.9", "mentions",
                    "entity:step:G3972G", "text_explicit", "published", 1.0, "public_shared",
                    [new BiblePackageEvidence("step-tagnt", "Acts.13.9", "original_token", [token.Id])],
                    "deterministic_import"),
                new BibleGraphEdgeCorpusInput(
                    "candidate:Acts.13.9:entity:step:G3972G", "passage:Acts.13.9", "proposed_identity",
                    "entity:step:G3972G", "source_proposed", "candidate", 0.9, "internal_review",
                    [new BiblePackageEvidence("candidate-fixture", "Acts.13.9", "dataset_record")],
                    "deterministic_candidate_import")
            ]);
        var services = new ServiceCollection();
        services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(
            "Host=localhost;Port=54329;Database=slogs;Username=slogs;Password=slogs_dev_password"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await InvokeEnsureSchemaAsync(db);
        var corpus = new KnowledgeCorpusService(factory, CreateEmbeddingService());
        var checkpointPath = Path.Combine(Path.GetTempPath(), $"slogs-bible-original-checkpoint-{suffix}.json");

        try
        {
            await new BibleCorpusImportRunner(corpus).RunAsync(
                KnowledgeCorpusActor.User("dimohy", isAdmin: true), plan, new string('C', 64), checkpointPath);
            var recalled = await corpus.RecallAsync(
                KnowledgeCorpusActor.User("reader"), "Σαῦλος G4569 형태론", limit: 3, maxGraphHops: 2);
            var result = Assert.Single(recalled, value => value.CollectionId == collectionId);
            Assert.Contains("morphology=N-NSM-P", result.Text);
            Assert.DoesNotContain(coordinate.Text, result.Text);
            var mention = Assert.Single(result.Relations, value => value.RelationType == "mentions");
            Assert.Equal("Paul", mention.ToLabel);
            Assert.Contains("Saul", mention.ToAliases!);
            Assert.Contains(mention.Evidence, value =>
                value.SourceId == "step-tagnt" && value.ChunkIds!.Contains(result.ChunkId, StringComparer.Ordinal));
            Assert.DoesNotContain(result.Relations, value => value.RelationType == "proposed_identity");
        }
        finally
        {
            await db.Database.ExecuteSqlAsync(
                $"DELETE FROM \"LlmWikiKnowledgeCollections\" WHERE \"CollectionId\" = {collectionId};");
            if (File.Exists(checkpointPath))
            {
                File.Delete(checkpointPath);
            }
        }
    }

    private static async Task InvokeEnsureSchemaAsync(SlogsDbContext db)
    {
        var method = typeof(SlogsDbInitializer).GetMethod("EnsureSchemaAsync", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("EnsureSchemaAsync를 찾을 수 없습니다.");
        var task = method.Invoke(null, [db]) as Task
            ?? throw new InvalidOperationException("EnsureSchemaAsync 호출 결과가 Task가 아닙니다.");
        await task;
    }

    private static EmbeddingGemmaService CreateEmbeddingService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmbeddingGemma:Endpoint"] = "http://embedding.test/api/embed"
            })
            .Build();
        return new EmbeddingGemmaService(new HttpClient(new DeterministicEmbeddingHandler()), configuration);
    }

    private sealed class DeterministicEmbeddingHandler : HttpMessageHandler
    {
        private static readonly string ResponseJson =
            $"{{\"embeddings\":[[{string.Join(',', Enumerable.Repeat("0.01", 768))}]]}}";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json")
            });
    }
}
