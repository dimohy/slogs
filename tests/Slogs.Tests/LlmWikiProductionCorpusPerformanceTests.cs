using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class LlmWikiProductionCorpusPerformanceTests
{
    private const int SamplesPerDepth = 31;
    private static readonly string SemanticDisabledCommandText = BuildSemanticDisabledCommandText();

    [Fact]
    [Trait("Category", "PostgreSqlProductionCorpus")]
    public async Task Frozen_production_backup_keeps_multi_hop_search_bounded_and_deterministic()
    {
        var connectionString = Environment.GetEnvironmentVariable("SLOGS_PRODUCTION_CORPUS_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await RebuildGraphStatisticsAsync(dataSource);
        var owner = await ScalarAsync<string>(dataSource,
            """
            SELECT "OwnerUserName"
            FROM "LlmWikiEntries"
            GROUP BY "OwnerUserName"
            ORDER BY COUNT(*) DESC, "OwnerUserName"
            LIMIT 1;
            """);
        var queryVector = await ScalarAsync<string>(dataSource,
            """
            SELECT idx."Embedding"::text
            FROM "LlmWikiEntryEmbeddings" AS idx
            WHERE idx."OwnerUserName" = @owner
            ORDER BY idx."EntryId"
            LIMIT 1;
            """,
            new NpgsqlParameter("owner", owner));
        var queryNodeKey = await ScalarAsync<string>(dataSource,
            """
            SELECT nodes."NodeKey"
            FROM "LlmWikiEntryGraphNodes" AS nodes
            WHERE nodes."OwnerUserName" = @owner
            GROUP BY nodes."NodeKey"
            ORDER BY COUNT(DISTINCT nodes."EntryId") DESC, nodes."NodeKey"
            LIMIT 1;
            """,
            new NpgsqlParameter("owner", owner));

        var measurements = new List<DepthMeasurement>();
        IReadOnlyList<Guid>? firstDepth1 = null;
        foreach (var depth in new[] { 1, 2, 3 })
        {
            await ExecuteSearchAsync(dataSource, owner, queryVector, queryNodeKey, depth);
            await ExecuteSearchAsync(dataSource, owner, queryVector, queryNodeKey, depth);

            var elapsed = new List<double>();
            IReadOnlyList<Guid>? firstResult = null;
            for (var sample = 0; sample < SamplesPerDepth; sample++)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await ExecuteSearchAsync(dataSource, owner, queryVector, queryNodeKey, depth);
                stopwatch.Stop();
                elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
                firstResult ??= result;
                Assert.Equal(firstResult, result);
            }

            if (depth == 1)
            {
                firstDepth1 = firstResult;
            }

            var plan = await ExplainSearchAsync(dataSource, owner, queryVector, queryNodeKey, depth);
            var p95 = Percentile(elapsed, 0.95);
            var budget = depth switch
            {
                1 => 250,
                2 => 500,
                _ => 750
            };
            Assert.InRange(p95, 0, budget);
            Assert.InRange(MaximumActualRows(plan.RootElement), 1, 100_000);
            if (depth == 1)
            {
                Assert.Equal(0, RecursiveUnionActualRows(plan.RootElement));
            }

            measurements.Add(new DepthMeasurement(
                depth,
                elapsed.Count,
                elapsed.Min(),
                Percentile(elapsed, 0.50),
                p95,
                elapsed.Max(),
                elapsed,
                firstResult!.Count,
                MaximumActualRows(plan.RootElement),
                SumPlanMetric(plan.RootElement, "Shared Hit Blocks"),
                SumPlanMetric(plan.RootElement, "Shared Read Blocks"),
                PlanExecutionTime(plan.RootElement)));
        }

        Assert.NotNull(firstDepth1);
        var outputPath = Environment.GetEnvironmentVariable("SLOGS_PRODUCTION_CORPUS_RESULT");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                corpus = new
                {
                    entries = await CountAsync(dataSource, "LlmWikiEntries"),
                    sources = await CountAsync(dataSource, "LlmWikiEntrySources"),
                    embeddings = await CountAsync(dataSource, "LlmWikiEntryEmbeddings"),
                    graphNodes = await CountAsync(dataSource, "LlmWikiEntryGraphNodes")
                },
                owner,
                queryNodeKey,
                samplesPerDepth = SamplesPerDepth,
                measurements
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    internal static async Task<IReadOnlyList<Guid>> ExecuteSearchAsync(
        NpgsqlDataSource dataSource,
        string owner,
        string queryVector,
        string queryNodeKey,
        int depth,
        bool semanticEnabled = true)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var sql = semanticEnabled ? LlmWikiGraphSearchCommand.CommandText : SemanticDisabledCommandText;
        await using var command = CreateCommand(connection, sql, owner, queryVector, queryNodeKey, depth);
        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetGuid(0));
        }
        return result;
    }

    internal static async Task<JsonDocument> ExplainSearchAsync(
        NpgsqlDataSource dataSource,
        string owner,
        string queryVector,
        string queryNodeKey,
        int depth,
        bool semanticEnabled = true)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var sql = semanticEnabled ? LlmWikiGraphSearchCommand.CommandText : SemanticDisabledCommandText;
        await using var command = CreateCommand(
            connection,
            $"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) {sql}",
            owner,
            queryVector,
            queryNodeKey,
            depth);
        return JsonDocument.Parse((string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("EXPLAIN returned no plan.")));
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string sql,
        string owner,
        string queryVector,
        string queryNodeKey,
        int depth)
    {
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("publicOnly", false);
        command.Parameters.AddWithValue("model", "embeddinggemma");
        command.Parameters.AddWithValue("dimensions", 768);
        command.Parameters.AddWithValue("queryVector", queryVector);
        command.Parameters.AddWithValue("categoryPath", string.Empty);
        command.Parameters.AddWithValue("categoryPrefix", string.Empty);
        command.Parameters.Add(new NpgsqlParameter("queryNodeKeys", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = new[] { queryNodeKey }
        });
        command.Parameters.AddWithValue("seedLimit", 100);
        command.Parameters.AddWithValue("graphSeedLimit", depth == 1 ? 100 : 8);
        command.Parameters.AddWithValue("graphFanout", 4);
        command.Parameters.AddWithValue("semanticFanout", 8);
        command.Parameters.AddWithValue("maxGraphHops", depth);
        command.Parameters.AddWithValue("graphIndexVersion", LlmWikiGraphSearchCommand.GraphIndexVersion);
        command.Parameters.AddWithValue("offset", 0);
        command.Parameters.AddWithValue("limit", 10);
        command.Parameters.AddWithValue("minRelevancePercent", 0);
        return command;
    }

    private static string BuildSemanticDisabledCommandText()
    {
        const string cteStartMarker = "active_semantic_version AS (";
        const string cteEndMarker = "semantic_walk AS (";
        var cteStart = LlmWikiGraphSearchCommand.CommandText.IndexOf(cteStartMarker, StringComparison.Ordinal);
        var cteEnd = LlmWikiGraphSearchCommand.CommandText.IndexOf(cteEndMarker, cteStart, StringComparison.Ordinal);
        var predicate = LlmWikiGraphSearchCommand.CommandText.IndexOf("WHERE", cteStart, StringComparison.Ordinal);
        if (cteStart < 0 || cteEnd <= cteStart || predicate <= cteStart || predicate >= cteEnd)
        {
            throw new InvalidOperationException("The active semantic-version CTE no longer matches the baseline contract.");
        }
        return LlmWikiGraphSearchCommand.CommandText.Insert(predicate + "WHERE".Length, " FALSE AND");
    }

    internal static async Task<T> ScalarAsync<T>(NpgsqlDataSource dataSource, string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The production corpus query returned no value."));
    }

    private static Task<long> CountAsync(NpgsqlDataSource dataSource, string table)
        => ScalarAsync<long>(dataSource, $"SELECT COUNT(*) FROM \"{table}\";");

    internal static async Task RebuildGraphStatisticsAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS "LlmWikiGraphNodeStatistics" (
                "OwnerUserName" character varying(80) NOT NULL,
                "NodeKey" character varying(180) NOT NULL,
                "EntryCount" integer NOT NULL,
                "IndexVersion" character varying(80) NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                PRIMARY KEY ("OwnerUserName", "NodeKey")
            );
            CREATE TABLE IF NOT EXISTS "LlmWikiGraphIndexStates" (
                "OwnerUserName" character varying(80) PRIMARY KEY,
                "IndexVersion" character varying(80) NOT NULL,
                "SourceNodeCount" bigint NOT NULL,
                "BuiltAt" timestamp with time zone NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "LlmWikiGraphEdges" (
                "OwnerUserName" character varying(80) NOT NULL,
                "FromEntryId" uuid NOT NULL,
                "ToEntryId" uuid NOT NULL,
                "EdgeScore" double precision NOT NULL,
                "IndexVersion" character varying(80) NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                PRIMARY KEY ("OwnerUserName", "FromEntryId", "ToEntryId")
            );
            CREATE TABLE IF NOT EXISTS "LlmWikiSemanticGraphVersions" (
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "State" character varying(24) NOT NULL,
                PRIMARY KEY ("OwnerUserName", "Version")
            );
            CREATE TABLE IF NOT EXISTS "LlmWikiSemanticMentions" (
                "Id" uuid PRIMARY KEY,
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "EntityKey" character varying(180) NOT NULL,
                "EntryId" uuid NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "LlmWikiSemanticRelations" (
                "Id" uuid PRIMARY KEY,
                "OwnerUserName" character varying(80) NOT NULL,
                "Version" character varying(80) NOT NULL,
                "FromEntityKey" character varying(180) NOT NULL,
                "ToEntityKey" character varying(180) NOT NULL,
                "RelationType" character varying(40) NOT NULL,
                "Confidence" double precision NOT NULL,
                "State" character varying(24) NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiGraphEdges_Owner_From_Score_To"
                ON "LlmWikiGraphEdges" ("OwnerUserName", "FromEntryId", "EdgeScore" DESC, "ToEntryId");
            TRUNCATE TABLE "LlmWikiGraphEdges", "LlmWikiGraphNodeStatistics", "LlmWikiGraphIndexStates";
            INSERT INTO "LlmWikiGraphNodeStatistics"
                ("OwnerUserName", "NodeKey", "EntryCount", "IndexVersion", "UpdatedAt")
            SELECT "OwnerUserName", "NodeKey", COUNT(DISTINCT "EntryId")::integer,
                   @graphIndexVersion, NOW()
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
                   @graphIndexVersion, NOW()
            FROM ranked_edges
            WHERE edge_rank <= 4;
            INSERT INTO "LlmWikiGraphIndexStates"
                ("OwnerUserName", "IndexVersion", "SourceNodeCount", "BuiltAt")
            SELECT "OwnerUserName", @graphIndexVersion, COUNT(*)::bigint, NOW()
            FROM "LlmWikiEntryGraphNodes"
            GROUP BY "OwnerUserName";
            """,
            connection);
        command.Parameters.AddWithValue("graphIndexVersion", LlmWikiGraphSearchCommand.GraphIndexVersion);
        await command.ExecuteNonQueryAsync();
    }

    internal static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var ordered = values.Order().ToArray();
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    internal static long MaximumActualRows(JsonElement element)
    {
        var maximum = 0L;
        Visit(element, value =>
        {
            if (value.TryGetProperty("Actual Rows", out var rows) && rows.TryGetInt64(out var count))
            {
                maximum = Math.Max(maximum, count);
            }
        });
        return maximum;
    }

    private static long RecursiveUnionActualRows(JsonElement element)
    {
        var rows = -1L;
        Visit(element, value =>
        {
            if (value.TryGetProperty("Node Type", out var type)
                && type.GetString() == "Recursive Union"
                && value.TryGetProperty("Actual Rows", out var actual)
                && actual.TryGetInt64(out var count))
            {
                rows = count;
            }
        });
        return rows;
    }

    private static long SumPlanMetric(JsonElement element, string propertyName)
    {
        var total = 0L;
        Visit(element, value =>
        {
            if (value.TryGetProperty(propertyName, out var metric) && metric.TryGetInt64(out var count))
            {
                total += count;
            }
        });
        return total;
    }

    private static double PlanExecutionTime(JsonElement element)
        => element[0].GetProperty("Execution Time").GetDouble();

    private static void Visit(JsonElement element, Action<JsonElement> visitor)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            visitor(element);
            foreach (var property in element.EnumerateObject())
            {
                Visit(property.Value, visitor);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Visit(item, visitor);
            }
        }
    }

    private sealed record DepthMeasurement(
        int Depth,
        int Samples,
        double MinimumMs,
        double P50Ms,
        double P95Ms,
        double MaximumMs,
        IReadOnlyList<double> ElapsedMs,
        int ResultCount,
        long MaximumActualRows,
        long SharedHitBlocks,
        long SharedReadBlocks,
        double ExplainExecutionMs);
}
