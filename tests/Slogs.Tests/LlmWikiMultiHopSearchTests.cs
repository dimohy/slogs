using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class LlmWikiMultiHopSearchTests
{
    [Fact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Search_reaches_exactly_one_two_and_three_hops_without_crossing_scopes()
    {
        var connectionString = Environment.GetEnvironmentVariable("SLOGS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await using var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", connection);
            await extension.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<SlogsDbContext>()
            .UseNpgsql(dataSource)
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            await CreateSearchTablesAsync(db);
            await SeedCorpusAsync(db);
        }
        var before = await AuthoritativeSnapshotAsync(factory);

        using var httpClient = new HttpClient(new FixedEmbeddingHandler());
        var embedding = new EmbeddingGemmaService(httpClient, new ConfigurationBuilder().Build());
        var service = new LlmWikiService(factory, embedding);

        var depth1 = await service.SearchAsync("owner", "seedalpha", 10, minRelevancePercent: 0, categoryPath: "graph/test", maxGraphHops: 1);
        AssertDepth(depth1, "hop-1", 1);
        AssertDepth(depth1, "hop-2", 0);
        AssertDepth(depth1, "hop-3", 0);

        var depth2 = await service.SearchAsync("owner", "seedalpha", 10, minRelevancePercent: 0, categoryPath: "graph/test", maxGraphHops: 2);
        AssertDepth(depth2, "hop-1", 1);
        AssertDepth(depth2, "hop-2", 2);
        AssertDepth(depth2, "hop-3", 0);

        var depth3 = await service.SearchAsync("owner", "seedalpha", 10, minRelevancePercent: 0, categoryPath: "graph/test", maxGraphHops: 3);
        AssertDepth(depth3, "hop-1", 1);
        AssertDepth(depth3, "hop-2", 2);
        AssertDepth(depth3, "hop-3", 3);
        Assert.Equal("inverse:implements", depth3.Single(x => x.Slug == "hop-1").SemanticPath);
        Assert.Equal("inverse:implements > documents", depth3.Single(x => x.Slug == "hop-2").SemanticPath);
        Assert.Equal("inverse:implements > documents > depends-on", depth3.Single(x => x.Slug == "hop-3").SemanticPath);
        Assert.InRange(depth3.Count(x => x.SemanticPath == "part-of"), 1, 5);
        Assert.DoesNotContain(depth3, x => x.Slug is "other-owner" or "other-category");

        var publicDepth3 = await service.SearchPublicAsync("owner", "seedalpha", 10, minRelevancePercent: 0, categoryPath: "graph/test", maxGraphHops: 3);
        Assert.DoesNotContain(publicDepth3, x => x.Slug == "private-neighbor");

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "owner"),
            new Claim(ClaimTypes.Name, "Owner")
        ], "llm-wiki-multihop-test"));
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        var tools = new LlmWikiMcpTools(accessor, service, null!);
        foreach (var depth in new[] { 1, 2, 3 })
        {
            var mcpSearch = await tools.SearchAsync("seedalpha", 10, "graph/test", 0, depth);
            Assert.Contains($"- maxGraphHops: {depth}", mcpSearch, StringComparison.Ordinal);
            if (depth > 1)
            {
                Assert.Contains($"graphDepth={depth}", mcpSearch, StringComparison.Ordinal);
            }
        }

        var mcpRecall = await tools.RecallAsync("seedalpha", 5, 0, 3);
        Assert.Contains("- maxGraphHops: 3", mcpRecall, StringComparison.Ordinal);
        Assert.Contains("- graphDepth: 3", mcpRecall, StringComparison.Ordinal);

        var plan = await ExplainProductionSearchAsync(dataSource);
        Assert.Contains("IX_TestGraphNode_Owner_NodeKey", plan, StringComparison.Ordinal);
        using var planJson = JsonDocument.Parse(plan);
        Assert.InRange(MaximumActualRows(planJson.RootElement), 1, 10_000);

        var depth1Plan = await ExplainProductionSearchAsync(dataSource, maxGraphHops: 1);
        using var depth1PlanJson = JsonDocument.Parse(depth1Plan);
        Assert.Equal(0, RecursiveUnionActualRows(depth1PlanJson.RootElement));

        var after = await AuthoritativeSnapshotAsync(factory);
        Assert.Equal(before, after);
    }

    private static void AssertDepth(IReadOnlyList<LlmWikiSearchResult> results, string slug, int expected)
    {
        var result = results.SingleOrDefault(x => x.Slug == slug);
        if (expected == 0)
        {
            Assert.True(result is null || result.GraphDepth == 0);
            return;
        }

        Assert.NotNull(result);
        Assert.Equal(expected, result.GraphDepth);
    }

    private static async Task CreateSearchTablesAsync(SlogsDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "LlmWikiEntryEmbeddings" (
                "EntryId" uuid PRIMARY KEY REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE,
                "OwnerUserName" character varying(80) NOT NULL,
                "Model" character varying(80) NOT NULL,
                "Dimensions" integer NOT NULL,
                "ContentHash" character varying(64) NOT NULL,
                "IndexVersion" character varying(40) NOT NULL,
                "Embedding" vector(768) NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            CREATE TABLE "LlmWikiEntryGraphNodes" (
                "EntryId" uuid NOT NULL REFERENCES "LlmWikiEntries" ("Id") ON DELETE CASCADE,
                "OwnerUserName" character varying(80) NOT NULL,
                "NodeKey" character varying(180) NOT NULL,
                "NodeText" character varying(120) NOT NULL,
                "NodeType" character varying(40) NOT NULL,
                "Weight" double precision NOT NULL,
                PRIMARY KEY ("EntryId", "NodeKey")
            );
            CREATE TABLE "LlmWikiGraphNodeStatistics" (
                "OwnerUserName" character varying(80) NOT NULL,
                "NodeKey" character varying(180) NOT NULL,
                "EntryCount" integer NOT NULL,
                "IndexVersion" character varying(80) NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                PRIMARY KEY ("OwnerUserName", "NodeKey")
            );
            CREATE TABLE "LlmWikiGraphIndexStates" (
                "OwnerUserName" character varying(80) PRIMARY KEY,
                "IndexVersion" character varying(80) NOT NULL,
                "SourceNodeCount" bigint NOT NULL,
                "BuiltAt" timestamp with time zone NOT NULL
            );
            CREATE TABLE "LlmWikiGraphEdges" (
                "OwnerUserName" character varying(80) NOT NULL,
                "FromEntryId" uuid NOT NULL,
                "ToEntryId" uuid NOT NULL,
                "EdgeScore" double precision NOT NULL,
                "IndexVersion" character varying(80) NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                PRIMARY KEY ("OwnerUserName", "FromEntryId", "ToEntryId")
            );
            CREATE TABLE "LlmWikiSemanticGraphVersions" (
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "State" character varying(24) NOT NULL,
                PRIMARY KEY ("OwnerUserName", "Version")
            );
            CREATE TABLE "LlmWikiSemanticEntities" (
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "EntityKey" character varying(180) NOT NULL,
                PRIMARY KEY ("OwnerUserName", "Version", "EntityKey")
            );
            CREATE TABLE "LlmWikiSemanticMentions" (
                "Id" uuid PRIMARY KEY,
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "EntityKey" character varying(180) NOT NULL,
                "EntryId" uuid NOT NULL
            );
            CREATE TABLE "LlmWikiSemanticRelations" (
                "Id" uuid PRIMARY KEY,
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "FromEntityKey" character varying(180) NOT NULL,
                "ToEntityKey" character varying(180) NOT NULL,
                "RelationType" character varying(40) NOT NULL,
                "Confidence" double precision NOT NULL,
                "State" character varying(24) NOT NULL
            );
            CREATE TABLE "LlmWikiMcpAudits" (
                "Id" uuid PRIMARY KEY,
                "OwnerUserName" character varying(80) NOT NULL,
                "ToolName" character varying(80) NOT NULL,
                "ResponseMode" character varying(80) NOT NULL,
                "QueryHash" character varying(64) NOT NULL DEFAULT '',
                "QueryPreview" character varying(240) NOT NULL DEFAULT '',
                "CategoryPath" character varying(240) NOT NULL DEFAULT '',
                "RequestedLimit" integer NULL,
                "EffectiveLimit" integer NULL,
                "MinRelevancePercent" integer NULL,
                "ResultCount" integer NOT NULL DEFAULT 0,
                "ResultIdsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
                "ElapsedMs" integer NOT NULL DEFAULT 0,
                "ResponseChars" integer NOT NULL DEFAULT 0,
                "Succeeded" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX "IX_TestGraphEdges_Owner_From_Score_To"
                ON "LlmWikiGraphEdges" ("OwnerUserName", "FromEntryId", "EdgeScore" DESC, "ToEntryId");
            CREATE INDEX "IX_TestGraphNode_Owner_NodeKey"
                ON "LlmWikiEntryGraphNodes" ("OwnerUserName", "NodeKey");
            CREATE INDEX "IX_TestGraphNode_EntryId"
                ON "LlmWikiEntryGraphNodes" ("EntryId");
            CREATE INDEX "IX_TestSemanticMentions_Entry"
                ON "LlmWikiSemanticMentions" ("OwnerUserName", "Version", "EntryId");
            CREATE INDEX "IX_TestSemanticMentions_Entity"
                ON "LlmWikiSemanticMentions" ("OwnerUserName", "Version", "EntityKey");
            CREATE INDEX "IX_TestSemanticRelations_From"
                ON "LlmWikiSemanticRelations" ("OwnerUserName", "Version", "FromEntityKey");
            CREATE INDEX "IX_TestSemanticRelations_To"
                ON "LlmWikiSemanticRelations" ("OwnerUserName", "Version", "ToEntityKey");
            CREATE INDEX "IX_TestEmbedding_Hnsw"
                ON "LlmWikiEntryEmbeddings" USING hnsw ("Embedding" vector_cosine_ops);
            """);
    }

    private static async Task SeedCorpusAsync(SlogsDbContext db)
    {
        var now = DateTime.UtcNow.AddMinutes(-1);
        db.Users.AddRange(
            User("owner", now),
            User("other", now));
        var entries = new List<LlmWikiEntryRecord>
        {
            Entry(1, "owner", "seed", "graph/test", true, now),
            Entry(1001, "owner", "hop-1", "graph/test", true, now),
            Entry(1002, "owner", "hop-2", "graph/test", true, now),
            Entry(1003, "owner", "hop-3", "graph/test", true, now),
            Entry(1004, "owner", "private-neighbor", "graph/test", false, now),
            Entry(1005, "owner", "other-category", "graph/other", true, now),
            Entry(1006, "other", "other-owner", "graph/test", true, now)
        };
        entries.AddRange(Enumerable.Range(2, 110).Select(index => Entry(index, "owner", $"seed-distractor-{index}", "graph/test", true, now)));
        db.LlmWikiEntries.AddRange(entries);
        entries[0].Sources.Add(new LlmWikiEntrySourceRecord
        {
            EntryId = entries[0].Id,
            OwnerUserName = entries[0].OwnerUserName,
            Action = "test-fixture",
            Prompt = "raw provenance must remain byte-identical",
            Content = "source content",
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var queryVector = VectorLiteral(1, 0);
        var distractorVector = VectorLiteral(0.01, 1);
        var otherVector = VectorLiteral(0, 1);
        const string model = "embeddinggemma";
        const string contentHash = "test";
        const string indexVersion = "2026-06-27-public-sharing-v1";
        foreach (var entry in entries)
        {
            var vector = entry.Slug == "seed"
                ? queryVector
                : entry.Slug.StartsWith("seed-distractor-", StringComparison.Ordinal)
                    ? distractorVector
                    : otherVector;
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "LlmWikiEntryEmbeddings"
                    ("EntryId", "OwnerUserName", "Model", "Dimensions", "ContentHash", "IndexVersion", "Embedding", "UpdatedAt")
                VALUES
                    ({entry.Id}, {entry.OwnerUserName}, {model}, {768}, {contentHash}, {indexVersion}, CAST({vector} AS vector), {DateTime.UtcNow});
                """);
        }

        await InsertNodeAsync(db, entries[0], "bridge-1");
        await InsertNodeAsync(db, entries[1], "bridge-1");
        await InsertNodeAsync(db, entries[1], "prompt-term:seedalpha");
        await InsertNodeAsync(db, entries[1], "bridge-2");
        await InsertNodeAsync(db, entries[2], "bridge-2");
        await InsertNodeAsync(db, entries[2], "bridge-3");
        await InsertNodeAsync(db, entries[1], "bridge-cycle");
        await InsertNodeAsync(db, entries[0], "bridge-cycle");
        await InsertNodeAsync(db, entries[3], "bridge-3");
        await InsertNodeAsync(db, entries[4], "bridge-1");
        await InsertNodeAsync(db, entries[5], "bridge-1");
        await InsertNodeAsync(db, entries[6], "bridge-1");
        await RebuildGraphStatisticsAsync(db);
        await SeedSemanticGraphAsync(db, entries);
        await SeedHighDegreeTaxonomyAsync(db, entries);
    }

    private static Task<int> SeedSemanticGraphAsync(
        SlogsDbContext db,
        IReadOnlyList<LlmWikiEntryRecord> entries)
        => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "LlmWikiSemanticGraphVersions" ("OwnerUserName", "Version", "State")
            VALUES ({"owner"}, {"semantic-test-v1"}, {"active"});
            INSERT INTO "LlmWikiSemanticEntities" ("OwnerUserName", "Version", "EntityKey")
            VALUES
                ({"owner"}, {"semantic-test-v1"}, {"seed"}),
                ({"owner"}, {"semantic-test-v1"}, {"hop-1"}),
                ({"owner"}, {"semantic-test-v1"}, {"hop-2"}),
                ({"owner"}, {"semantic-test-v1"}, {"hop-3"}),
                ({"owner"}, {"semantic-test-v1"}, {"private-neighbor"}),
                ({"owner"}, {"semantic-test-v1"}, {"other-category"});
            INSERT INTO "LlmWikiSemanticMentions" ("Id", "OwnerUserName", "Version", "EntityKey", "EntryId")
            VALUES
                ({Guid.Parse("10000000-0000-0000-0000-000000000001")}, {"owner"}, {"semantic-test-v1"}, {"seed"}, {entries[0].Id}),
                ({Guid.Parse("10000000-0000-0000-0000-000000000002")}, {"owner"}, {"semantic-test-v1"}, {"hop-1"}, {entries[1].Id}),
                ({Guid.Parse("10000000-0000-0000-0000-000000000003")}, {"owner"}, {"semantic-test-v1"}, {"hop-2"}, {entries[2].Id}),
                ({Guid.Parse("10000000-0000-0000-0000-000000000004")}, {"owner"}, {"semantic-test-v1"}, {"hop-3"}, {entries[3].Id}),
                ({Guid.Parse("10000000-0000-0000-0000-000000000005")}, {"owner"}, {"semantic-test-v1"}, {"private-neighbor"}, {entries[4].Id}),
                ({Guid.Parse("10000000-0000-0000-0000-000000000006")}, {"owner"}, {"semantic-test-v1"}, {"other-category"}, {entries[5].Id});
            INSERT INTO "LlmWikiSemanticRelations"
                ("Id", "OwnerUserName", "Version", "FromEntityKey", "ToEntityKey", "RelationType", "Confidence", "State")
            VALUES
                ({Guid.Parse("20000000-0000-0000-0000-000000000001")}, {"owner"}, {"semantic-test-v1"}, {"hop-1"}, {"seed"}, {"implements"}, {0.99}, {"active"}),
                ({Guid.Parse("20000000-0000-0000-0000-000000000002")}, {"owner"}, {"semantic-test-v1"}, {"hop-1"}, {"hop-2"}, {"documents"}, {0.98}, {"active"}),
                ({Guid.Parse("20000000-0000-0000-0000-000000000003")}, {"owner"}, {"semantic-test-v1"}, {"hop-2"}, {"hop-3"}, {"depends-on"}, {0.97}, {"active"}),
                ({Guid.Parse("20000000-0000-0000-0000-000000000004")}, {"owner"}, {"semantic-test-v1"}, {"seed"}, {"private-neighbor"}, {"supports"}, {0.96}, {"active"}),
                ({Guid.Parse("20000000-0000-0000-0000-000000000005")}, {"owner"}, {"semantic-test-v1"}, {"seed"}, {"other-category"}, {"related-to"}, {0.95}, {"active"});
            """);

    private static async Task SeedHighDegreeTaxonomyAsync(
        SlogsDbContext db,
        IReadOnlyList<LlmWikiEntryRecord> entries)
    {
        for (var index = 0; index < 12; index++)
        {
            var entityKey = $"taxonomy-{index:D2}";
            var entryId = entries[7 + index].Id;
            var mentionId = Guid.Parse($"30000000-0000-0000-0000-{index + 1:D12}");
            var relationId = Guid.Parse($"40000000-0000-0000-0000-{index + 1:D12}");
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "LlmWikiSemanticEntities" ("OwnerUserName", "Version", "EntityKey")
                VALUES ({"owner"}, {"semantic-test-v1"}, {entityKey});
                INSERT INTO "LlmWikiSemanticMentions" ("Id", "OwnerUserName", "Version", "EntityKey", "EntryId")
                VALUES ({mentionId}, {"owner"}, {"semantic-test-v1"}, {entityKey}, {entryId});
                INSERT INTO "LlmWikiSemanticRelations"
                    ("Id", "OwnerUserName", "Version", "FromEntityKey", "ToEntityKey", "RelationType", "Confidence", "State")
                VALUES ({relationId}, {"owner"}, {"semantic-test-v1"}, {"seed"}, {entityKey}, {"part-of"}, {0.90}, {"active"});
                """);
        }
    }

    private static Task<int> RebuildGraphStatisticsAsync(SlogsDbContext db)
        => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "LlmWikiGraphNodeStatistics"
                ("OwnerUserName", "NodeKey", "EntryCount", "IndexVersion", "UpdatedAt")
            SELECT
                "OwnerUserName",
                "NodeKey",
                COUNT(DISTINCT "EntryId")::integer,
                {LlmWikiGraphSearchCommand.GraphIndexVersion},
                {DateTime.UtcNow}
            FROM "LlmWikiEntryGraphNodes"
            GROUP BY "OwnerUserName", "NodeKey";
            WITH scored_edges AS (
                SELECT
                    source_nodes."OwnerUserName",
                    source_nodes."EntryId" AS "FromEntryId",
                    neighbor_nodes."EntryId" AS "ToEntryId",
                    LEAST(SUM(
                        LEAST(source_nodes."Weight", neighbor_nodes."Weight")
                        / LN(2.0 + frequency."EntryCount")
                    ), 1.0) AS "EdgeScore"
                FROM "LlmWikiEntryGraphNodes" AS source_nodes
                INNER JOIN "LlmWikiEntryGraphNodes" AS neighbor_nodes
                    ON neighbor_nodes."OwnerUserName" = source_nodes."OwnerUserName"
                   AND neighbor_nodes."NodeKey" = source_nodes."NodeKey"
                   AND neighbor_nodes."EntryId" <> source_nodes."EntryId"
                INNER JOIN "LlmWikiGraphNodeStatistics" AS frequency
                    ON frequency."OwnerUserName" = source_nodes."OwnerUserName"
                   AND frequency."NodeKey" = source_nodes."NodeKey"
                GROUP BY source_nodes."OwnerUserName", source_nodes."EntryId", neighbor_nodes."EntryId"
            ), ranked_edges AS (
                SELECT *, ROW_NUMBER() OVER (
                    PARTITION BY "OwnerUserName", "FromEntryId"
                    ORDER BY "EdgeScore" DESC, "ToEntryId"
                ) AS edge_rank
                FROM scored_edges
            )
            INSERT INTO "LlmWikiGraphEdges"
                ("OwnerUserName", "FromEntryId", "ToEntryId", "EdgeScore", "IndexVersion", "UpdatedAt")
            SELECT "OwnerUserName", "FromEntryId", "ToEntryId", "EdgeScore",
                   {LlmWikiGraphSearchCommand.GraphIndexVersion}, {DateTime.UtcNow}
            FROM ranked_edges
            WHERE edge_rank <= 4;
            INSERT INTO "LlmWikiGraphIndexStates"
                ("OwnerUserName", "IndexVersion", "SourceNodeCount", "BuiltAt")
            SELECT
                "OwnerUserName",
                {LlmWikiGraphSearchCommand.GraphIndexVersion},
                COUNT(*)::bigint,
                {DateTime.UtcNow}
            FROM "LlmWikiEntryGraphNodes"
            GROUP BY "OwnerUserName";
            """);

    private static LlmWikiEntryRecord Entry(int id, string owner, string slug, string category, bool isPublic, DateTime updatedAt)
        => new()
        {
            Id = Guid.Parse($"00000000-0000-0000-0000-{id:D12}"),
            OwnerUserName = owner,
            Slug = slug,
            Title = slug,
            Summary = slug,
            SourcePrompt = slug == "seed" ? "seedalpha" : slug,
            Content = slug,
            TagsJson = "[]",
            CategoryPath = category,
            CategoryDepth = 2,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
            IsPublic = isPublic,
            PublishedAt = isPublic ? updatedAt : null
        };

    private static UserRecord User(string userName, DateTime registeredAt)
        => new()
        {
            UserName = userName,
            DisplayName = userName,
            Email = $"{userName}@example.invalid",
            Password = "test-only",
            RegisteredAt = registeredAt
        };

    private static Task<int> InsertNodeAsync(SlogsDbContext db, LlmWikiEntryRecord entry, string node)
    {
        const string nodeType = "tag";
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "LlmWikiEntryGraphNodes"
                ("EntryId", "OwnerUserName", "NodeKey", "NodeText", "NodeType", "Weight")
            VALUES ({entry.Id}, {entry.OwnerUserName}, {node}, {node}, {nodeType}, {1.0});
            """);
    }

    private static string VectorLiteral(double first, double second)
        => $"[{first.ToString(System.Globalization.CultureInfo.InvariantCulture)},{second.ToString(System.Globalization.CultureInfo.InvariantCulture)},{string.Join(',', Enumerable.Repeat("0", 766))}]";

    private static async Task<string> AuthoritativeSnapshotAsync(IDbContextFactory<SlogsDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.LlmWikiEntries.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.OwnerUserName,
                x.Slug,
                x.Title,
                x.Summary,
                x.Content,
                x.SourcePrompt,
                x.TagsJson,
                x.CategoryPath,
                x.CategoryDepth,
                x.CreatedAt,
                x.UpdatedAt,
                x.IsPublic,
                x.PublishedAt
            })
            .ToListAsync();
        var sources = await db.LlmWikiEntrySources.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.EntryId,
                x.OwnerUserName,
                x.Action,
                x.Prompt,
                x.Content,
                x.Title,
                x.Tags,
                x.CategoryPath,
                x.CreatedAt
            })
            .ToListAsync();
        return JsonSerializer.Serialize(new { entries, sources });
    }

    private static async Task<string> ExplainProductionSearchAsync(
        NpgsqlDataSource dataSource,
        int maxGraphHops = 3)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using (var settings = new NpgsqlCommand("SET enable_seqscan = off;", connection))
        {
            await settings.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            $"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) {LlmWikiGraphSearchCommand.CommandText}",
            connection);
        command.Parameters.AddWithValue("owner", "owner");
        command.Parameters.AddWithValue("publicOnly", false);
        command.Parameters.AddWithValue("model", "embeddinggemma");
        command.Parameters.AddWithValue("dimensions", 768);
        command.Parameters.AddWithValue("queryVector", VectorLiteral(1, 0));
        command.Parameters.AddWithValue("categoryPath", "graph/test");
        command.Parameters.AddWithValue("categoryPrefix", "graph/test/%");
        command.Parameters.Add(new NpgsqlParameter("queryNodeKeys", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = new[] { "prompt-term:seedalpha" }
        });
        command.Parameters.AddWithValue("seedLimit", 100);
        command.Parameters.AddWithValue("graphSeedLimit", 8);
        command.Parameters.AddWithValue("graphFanout", 4);
        command.Parameters.AddWithValue("semanticFanout", 8);
        command.Parameters.AddWithValue("maxGraphHops", maxGraphHops);
        command.Parameters.AddWithValue("graphIndexVersion", LlmWikiGraphSearchCommand.GraphIndexVersion);
        command.Parameters.AddWithValue("offset", 0);
        command.Parameters.AddWithValue("limit", 10);
        command.Parameters.AddWithValue("minRelevancePercent", 0);
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("EXPLAIN returned no plan."));
    }

    private static long MaximumActualRows(JsonElement element)
    {
        var maximum = 0L;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("Actual Rows") && property.Value.TryGetInt64(out var rows))
                {
                    maximum = Math.Max(maximum, rows);
                }
                maximum = Math.Max(maximum, MaximumActualRows(property.Value));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                maximum = Math.Max(maximum, MaximumActualRows(item));
            }
        }
        return maximum;
    }

    private static long RecursiveUnionActualRows(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var isRecursiveUnion = element.TryGetProperty("Node Type", out var nodeType)
                && nodeType.GetString() == "Recursive Union";
            if (isRecursiveUnion
                && element.TryGetProperty("Actual Rows", out var rows)
                && rows.TryGetInt64(out var actualRows))
            {
                return actualRows;
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = RecursiveUnionActualRows(property.Value);
                if (nested >= 0)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = RecursiveUnionActualRows(item);
                if (nested >= 0)
                {
                    return nested;
                }
            }
        }

        return -1;
    }

    private sealed class TestDbContextFactory(DbContextOptions<SlogsDbContext> options) : IDbContextFactory<SlogsDbContext>
    {
        public SlogsDbContext CreateDbContext() => new(options);

        public Task<SlogsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private sealed class FixedEmbeddingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var vector = VectorLiteral(1, 0);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"embeddings\":[{vector}]}}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

}
