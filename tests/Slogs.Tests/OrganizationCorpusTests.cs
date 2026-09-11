using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class OrganizationCorpusTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task ServiceTokensMustHaveMatchingOrganizationAndReadScope(bool hasOrganization, bool matches, bool read)
    {
        var organization = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-service"),
            new(OrganizationClaimTypes.ActorKind, OrganizationActorKinds.Service),
            new(OrganizationClaimTypes.TokenScope, read ? OrganizationTokenScopes.Read : OrganizationTokenScopes.Propose)
        };
        if (hasOrganization) claims.Add(new(OrganizationClaimTypes.OrganizationId, (matches ? organization : Guid.NewGuid()).ToString("D")));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        } };
        var tools = new OrganizationCorpusMcpTools(accessor, new OrganizationActorResolver(null!), null!);
        await Assert.ThrowsAsync<OrganizationAccessDeniedException>(() => tools.StatusAsync(organization, "test-corpus"));
        await Assert.ThrowsAsync<OrganizationAccessDeniedException>(() => tools.RecallAsync(organization, "test-corpus", "question"));
    }

    [Fact]
    public async Task UnauthenticatedCallerCannotReadCorpus()
    {
        var tools = new OrganizationCorpusMcpTools(new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new OrganizationActorResolver(null!), null!);
        await Assert.ThrowsAsync<OrganizationAccessDeniedException>(() => tools.StatusAsync(Guid.NewGuid(), "test-corpus"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 11)]
    public async Task InvalidBudgetsFailBeforeDatabaseOrEmbedding(int hops, int limit)
    {
        var service = new KnowledgeCorpusService(null!, new CountingEmbedding());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RecallOrganizationAsync(Actor(Guid.NewGuid()), "test", "question", limit, hops));
    }

    [Fact]
    public async Task UnapprovedOrMissingEvidenceCannotBecomeGraphFacts()
    {
        var service = new KnowledgeCorpusService(null!, new CountingEmbedding());
        var organization = Guid.NewGuid();
        var plan = Plan("invalid-evidence", organization, "test-owner");
        var actor = Owner(organization, "test-owner");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.IngestAsync(actor,
            plan with { Relations = [plan.Relations[0] with { Evidence = [] }] }));
    }

    [OrganizationCorpusIntegrationTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ScopedCorpusUsesRealApprovedPathsAndExactlyOneDeepRerank(int hops)
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service;
        var result = await service.RecallOrganizationAsync(Actor(fixture.Organization), fixture.Collection,
            "진료과 공개 의료진 시간표 공식 프로필", limit: 1, maxGraphHops: hops, anchorChunkId: "chunk:department");
        Assert.Equal(hops, result.Chunks.Count);
        Assert.Equal(hops, result.Chunks.Max(chunk => chunk.GraphDepth));
        Assert.All(result.Chunks, chunk => Assert.Equal(fixture.Collection, chunk.CollectionId));
        Assert.All(result.Chunks, chunk => Assert.DoesNotContain("POISON", chunk.Text));
        Assert.All(result.Chunks.SelectMany(chunk => chunk.Relations), relation =>
        {
            Assert.Equal("approved", relation.ReviewStatus);
            Assert.NotEmpty(relation.RelationId);
            Assert.NotEmpty(relation.Evidence);
            Assert.Equal(relation.GraphDepth + 1, relation.SemanticPath!.Count);
            Assert.InRange(relation.GraphDepth, 1, hops - 1);
        });
        Assert.DoesNotContain(result.Chunks, chunk => chunk.ChunkId == "chunk:unapproved");
        Assert.Equal(hops == 1 ? 0 : 1, result.Diagnostics.PairScoreCalls);
        Assert.Equal(result.Diagnostics.PairScoreCalls, fixture.Embedding.PairScoreCalls);
        Assert.Equal(hops == 1 ? 0 : 1, result.Diagnostics.PairScoreCandidates);
        Assert.Equal(0, fixture.Embedding.QueryCalls);
        Assert.Equal(hops == 1 ? "general-bge-m3-dense" : "relational-bge-m3-full", result.Diagnostics.RetrievalProfile);
        if (hops == 3)
            Assert.Equal(["chunk:department", "chunk:schedule", "chunk:profile"],
                Assert.Single(result.Chunks, chunk => chunk.GraphDepth == 3).SemanticPath);
    }

    [Fact]
    public void BoundedGraphEvidencePreservesACompleteDeepPathBeforeShallowSiblings()
    {
        var siblings = Enumerable.Range(0, 12).Select(index => new KnowledgeChunkRecall("collection", "1", "test", "doc", "title",
            $"chunk:schedule-{index}", "text", "start", "end", 0, [], GraphDepth: 2,
            SemanticPath: ["chunk:department", $"chunk:schedule-{index}"])).ToList();
        siblings.Add(new("collection", "1", "test", "doc", "title", "chunk:profile", "text", "start", "end", 0, [],
            GraphDepth: 3, SemanticPath: ["chunk:department", "chunk:schedule-9", "chunk:profile"]));
        var selected = KnowledgeCorpusService.SelectOrganizationGraphEvidence(siblings, 3);
        Assert.Equal(3, selected.Count);
        Assert.Contains(selected, chunk => chunk.ChunkId == "chunk:schedule-9");
        Assert.Contains(selected, chunk => chunk.ChunkId == "chunk:profile");
    }

    [OrganizationCorpusIntegrationFact]
    public async Task ExistingPersonalRecallRetainsPublicVisibilityAndNewRelationFields()
    {
        await using var fixture = await Fixture.CreateAsync();
        var results = await fixture.Service.RecallAsync(KnowledgeCorpusActor.User("public-outsider"), "진료과 공개", limit: 3, maxGraphHops: 2);
        Assert.NotEmpty(results);
        Assert.All(results, chunk => Assert.Equal("public-owner", chunk.StorageOwnerUserName));
        Assert.All(results.SelectMany(chunk => chunk.Relations), relation => Assert.NotEmpty(relation.RelationId));
    }

    [OrganizationCorpusIntegrationFact]
    public async Task OrganizationClockTimeQuestionDoesNotApplyPersonalLocatorCompaction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.RecallOrganizationAsync(Actor(fixture.Organization), fixture.Collection,
            "9:30 접수 안내", limit: 1, maxGraphHops: 1, anchorChunkId: "chunk:department");
        var source = Assert.Single(result.Chunks);
        Assert.Contains("진료과 공개", source.Text);
        Assert.Contains("9:30 접수", source.Text);
        Assert.Equal("source/department", source.StartLocator);
        Assert.Equal(0, result.Diagnostics.PairScoreCalls);
    }

    [OrganizationCorpusIntegrationFact]
    public async Task VectorLexicalAndExactLocatorCandidatesCannotCompeteAcrossScope()
    {
        await using var fixture = await Fixture.CreateAsync();
        foreach (var query in new[] { "진료과 공개", "검증서 1:1" })
        {
            var result = await fixture.Service.RecallOrganizationAsync(Actor(fixture.Organization), fixture.Collection, query, limit: 3);
            Assert.NotEmpty(result.Chunks);
            Assert.All(result.Chunks, chunk =>
            {
                Assert.Equal(fixture.Collection, chunk.CollectionId);
                Assert.Equal(fixture.OwnerName, chunk.StorageOwnerUserName);
                Assert.DoesNotContain("POISON", chunk.Text);
            });
            Assert.Equal(0, result.Diagnostics.PairScoreCalls);
        }
        var foreignOnly = fixture.Collection + "-foreign-only";
        await Assert.ThrowsAsync<OrganizationNotFoundException>(() => fixture.Service.ReadOrganizationStatusAsync(Actor(fixture.Organization), foreignOnly));
        await Assert.ThrowsAsync<OrganizationNotFoundException>(() => fixture.Service.RecallOrganizationAsync(
            Actor(fixture.Organization), fixture.Collection, "question", anchorChunkId: "chunk:foreign-only"));
    }

    private static OrganizationActorContext Actor(Guid organization) => new(organization, "reader-service",
        OrganizationActorKinds.Service, "service", new HashSet<string> { OrganizationTokenScopes.Read }, null, Guid.NewGuid(), null);

    private static KnowledgeCorpusActor Owner(Guid organization, string name) => new(name, false,
        new Dictionary<string, string> { [organization.ToString("D")] = OrganizationRoles.Owner });

    private static KnowledgeCorpusIngestRequest Plan(string collection, Guid organization, string owner, string poison = "")
    {
        var chunks = new[]
        {
            ("department", "진료과 공개: 의사 시간표를 연결합니다.\n9:30 접수 안내를 확인하세요."),
            ("schedule", "홍길동 의사의 월요일 오전 외래진료 안내입니다."),
            ("profile", "홍길동 의사의 전문분야 및 공식 프로필입니다."),
            ("unapproved", "검토되지 않은 의학적 주장입니다.")
        }.Select((value, index) => new KnowledgeChunkInput($"chunk:{value.Item1}", $"doc:{value.Item1}", null,
            index, poison + value.Item2, $"source/{value.Item1}", $"source/{value.Item1}", null, null, 0, 12,
            "unicode-word-estimate-v1", ["Test.1.1"])).ToArray();
        KnowledgeRelationInput Edge(string id, string from, string to, string status) => new($"relation:{id}",
            $"chunk:{from}", "source_links", $"chunk:{to}", "source_explicit", status, 1,
            [new(collection, $"source/{from}", "source_explicit", [$"chunk:{from}", $"chunk:{to}"])], "fixed-test-source");
        return new(new(collection, "1.0.0", "조직 코퍼스", "hospital-public-information", "ko", "test-only", "urn:test:corpus",
                "organization", organization.ToString("D"), "organization", organization.ToString("D"), false, chunks.Length),
            chunks.Select((chunk, index) => new KnowledgeDocumentInput(chunk.DocumentId, index == 0 ? "검증서" : chunk.DocumentId,
                "webpage", index, chunk.StartLocator)).ToArray(), [], chunks, [],
            [Edge("a-b", "department", "schedule", "approved"), Edge("b-c", "schedule", "profile", "approved"),
                Edge("candidate", "department", "unapproved", "candidate")], Activate: true);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required KnowledgeCorpusService Service { get; init; }
        public required CountingEmbedding Embedding { get; init; }
        public Guid Organization { get; } = Guid.NewGuid();
        public string Collection { get; } = $"organization-recall-{Guid.NewGuid():N}";
        public string OwnerName { get; } = $"owner-{Guid.NewGuid():N}";
        public static async Task<Fixture> CreateAsync()
        {
            var connection = Environment.GetEnvironmentVariable("SLOGS_ORGANIZATION_CORPUS_TEST_CONNECTION")
                ?? throw new InvalidOperationException("An explicit isolated test database connection is required.");
            var services = new ServiceCollection();
            services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(connection));
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            var initialize = typeof(SlogsDbInitializer).GetMethod("EnsureSchemaAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            await (Task)initialize.Invoke(null, [db])!;
            var embedding = new CountingEmbedding();
            var fixture = new Fixture { Provider = provider, Embedding = embedding, Service = new(factory, embedding) };
            var own = Plan(fixture.Collection, fixture.Organization, fixture.OwnerName);
            await fixture.Service.IngestAsync(Owner(fixture.Organization, fixture.OwnerName), own);
            await fixture.Service.IngestAsync(Owner(fixture.Organization, fixture.OwnerName),
                Plan(fixture.Collection + "-other", fixture.Organization, fixture.OwnerName, "POISON other collection "));
            var foreign = Guid.NewGuid();
            var foreignOwner = "foreign-" + Guid.NewGuid().ToString("N");
            await fixture.Service.IngestAsync(Owner(foreign, foreignOwner), Plan(fixture.Collection, foreign, foreignOwner, "POISON other organization "));
            await fixture.Service.IngestAsync(Owner(foreign, foreignOwner), Plan(fixture.Collection + "-foreign-only", foreign, foreignOwner));
            var publicPlan = own with
            {
                Collection = own.Collection with { OwnerKind = "user", OwnerKey = "public-owner", Visibility = "public_shared", ScopeKey = null, RedistributionAllowed = true },
                Chunks = own.Chunks.Select(chunk => chunk with { Text = "POISON public " + chunk.Text }).ToArray()
            };
            await fixture.Service.IngestAsync(KnowledgeCorpusActor.User("public-owner", true), publicPlan);
            embedding.QueryCalls = 0; embedding.PairScoreCalls = 0;
            return fixture;
        }
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class CountingEmbedding : IKnowledgeEmbeddingService
    {
        public string Model => "bge-m3-test";
        public int Dimensions => 1024;
        public bool SupportsFullFunctionReranking => true;
        public int QueryCalls { get; set; }
        public int PairScoreCalls { get; set; }
        private static IReadOnlyList<float> Vector => Enumerable.Repeat(0.01f, 1024).ToArray();
        public Task<IReadOnlyList<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken)
        { QueryCalls++; return Task.FromResult(Vector); }
        public Task<IReadOnlyList<float>> EmbedDocumentAsync(string document, CancellationToken cancellationToken) => Task.FromResult(Vector);
        public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedDocumentsAsync(IReadOnlyList<string> documents, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>(documents.Select(_ => Vector).ToArray());
        public Task<IReadOnlyList<KnowledgeRerankScore>> ScorePairsAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken)
        { PairScoreCalls++; return Task.FromResult<IReadOnlyList<KnowledgeRerankScore>>(passages.Select(_ => new KnowledgeRerankScore(1, 1, 1, 1)).ToArray()); }
    }
}

public sealed class OrganizationCorpusIntegrationFactAttribute : FactAttribute
{
    public OrganizationCorpusIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SLOGS_ORGANIZATION_CORPUS_INTEGRATION") != "1")
            Skip = "Requires explicit isolated PostgreSQL corpus integration environment.";
    }
}
public sealed class OrganizationCorpusIntegrationTheoryAttribute : TheoryAttribute
{
    public OrganizationCorpusIntegrationTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("SLOGS_ORGANIZATION_CORPUS_INTEGRATION") != "1")
            Skip = "Requires explicit isolated PostgreSQL corpus integration environment.";
    }
}
