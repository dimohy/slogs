using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Slogs.Tests;

public sealed class LlmWikiSemanticPerformanceComparisonTests
{
    private const int BlocksPerModeAndDepth = 4;
    private const int SamplesPerBlock = 16;
    private const int WarmupSamplesPerModeAndDepth = 32;
    private const int SamplesPerModeAndDepth = BlocksPerModeAndDepth * SamplesPerBlock;

    [Fact]
    [Trait("Category", "PostgreSqlSemanticPerformanceComparison")]
    public async Task Active_semantic_graph_does_not_regress_against_interleaved_baseline()
    {
        var connectionString = Environment.GetEnvironmentVariable("SLOGS_PRODUCTION_CORPUS_POSTGRES");
        var semanticVersion = Environment.GetEnvironmentVariable("SLOGS_SEMANTIC_COMPARISON_VERSION");
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(semanticVersion))
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await LlmWikiProductionCorpusPerformanceTests.RebuildGraphStatisticsAsync(dataSource);
        var owner = await LlmWikiProductionCorpusPerformanceTests.ScalarAsync<string>(dataSource,
            """
            SELECT "OwnerUserName"
            FROM "LlmWikiEntries"
            GROUP BY "OwnerUserName"
            ORDER BY COUNT(*) DESC, "OwnerUserName"
            LIMIT 1;
            """);
        var queryVector = await LlmWikiProductionCorpusPerformanceTests.ScalarAsync<string>(dataSource,
            """
            SELECT "Embedding"::text
            FROM "LlmWikiEntryEmbeddings"
            WHERE "OwnerUserName"=@owner
            ORDER BY "EntryId"
            LIMIT 1;
            """,
            new NpgsqlParameter("owner", owner));
        var queryNodeKey = await LlmWikiProductionCorpusPerformanceTests.ScalarAsync<string>(dataSource,
            """
            SELECT "NodeKey"
            FROM "LlmWikiEntryGraphNodes"
            WHERE "OwnerUserName"=@owner
            GROUP BY "NodeKey"
            ORDER BY COUNT(DISTINCT "EntryId") DESC, "NodeKey"
            LIMIT 1;
            """,
            new NpgsqlParameter("owner", owner));
        var activeVersionCount = await LlmWikiProductionCorpusPerformanceTests.ScalarAsync<long>(dataSource,
            "SELECT COUNT(*) FROM \"LlmWikiSemanticGraphVersions\" WHERE \"OwnerUserName\"=@owner AND \"Version\"=@version AND \"State\"='active';",
            new NpgsqlParameter("owner", owner),
            new NpgsqlParameter("version", semanticVersion));
        Assert.Equal(1, activeVersionCount);

        var measurements = new List<PairedDepthMeasurement>();
        var failures = new List<string>();
        foreach (var depth in new[] { 1, 2, 3 })
        {
            var baselineWarmup = new List<double>(WarmupSamplesPerModeAndDepth);
            var activeWarmup = new List<double>(WarmupSamplesPerModeAndDepth);
            if (depth % 2 == 1)
            {
                await MeasureBlockAsync(dataSource, owner, queryVector, queryNodeKey, depth, false, baselineWarmup, WarmupSamplesPerModeAndDepth);
                await MeasureBlockAsync(dataSource, owner, queryVector, queryNodeKey, depth, true, activeWarmup, WarmupSamplesPerModeAndDepth);
            }
            else
            {
                await MeasureBlockAsync(dataSource, owner, queryVector, queryNodeKey, depth, true, activeWarmup, WarmupSamplesPerModeAndDepth);
                await MeasureBlockAsync(dataSource, owner, queryVector, queryNodeKey, depth, false, baselineWarmup, WarmupSamplesPerModeAndDepth);
            }

            var baselineElapsed = new List<double>(SamplesPerModeAndDepth);
            var activeElapsed = new List<double>(SamplesPerModeAndDepth);
            IReadOnlyList<Guid>? baselineResult = null;
            IReadOnlyList<Guid>? activeResult = null;
            for (var block = 0; block < BlocksPerModeAndDepth; block++)
            {
                if (block % 2 == 0)
                {
                    baselineResult = await MeasureBlockAsync(dataSource, owner, queryVector, queryNodeKey, depth, false, baselineElapsed, SamplesPerBlock);
                    activeResult = await MeasureBlockAsync(dataSource, owner, queryVector, queryNodeKey, depth, true, activeElapsed, SamplesPerBlock);
                }
                else
                {
                    activeResult = await MeasureBlockAsync(dataSource, owner, queryVector, queryNodeKey, depth, true, activeElapsed, SamplesPerBlock);
                    baselineResult = await MeasureBlockAsync(dataSource, owner, queryVector, queryNodeKey, depth, false, baselineElapsed, SamplesPerBlock);
                }
            }

            Assert.NotNull(baselineResult);
            Assert.NotNull(activeResult);
            if (baselineResult.Count != activeResult.Count)
            {
                failures.Add($"Depth {depth} changed result count from {baselineResult.Count} to {activeResult.Count}.");
            }
            if (depth == 1 && !baselineResult.SequenceEqual(activeResult))
            {
                failures.Add("Depth 1 changed the compatibility result order.");
            }

            using var baselinePlan = await LlmWikiProductionCorpusPerformanceTests.ExplainSearchAsync(
                dataSource, owner, queryVector, queryNodeKey, depth, false);
            using var activePlan = await LlmWikiProductionCorpusPerformanceTests.ExplainSearchAsync(
                dataSource, owner, queryVector, queryNodeKey, depth, true);

            var baselineP95 = LlmWikiProductionCorpusPerformanceTests.Percentile(baselineElapsed, 0.95);
            var activeP95 = LlmWikiProductionCorpusPerformanceTests.Percentile(activeElapsed, 0.95);
            var coldStartBudgetMs = depth switch
            {
                1 => 250,
                2 => 500,
                _ => 750
            };
            var baselineWarmupP95 = LlmWikiProductionCorpusPerformanceTests.Percentile(baselineWarmup, 0.95);
            var activeWarmupP95 = LlmWikiProductionCorpusPerformanceTests.Percentile(activeWarmup, 0.95);
            var baselineRows = LlmWikiProductionCorpusPerformanceTests.MaximumActualRows(baselinePlan.RootElement);
            var activeRows = LlmWikiProductionCorpusPerformanceTests.MaximumActualRows(activePlan.RootElement);
            if (baselineWarmupP95 > coldStartBudgetMs || activeWarmupP95 > coldStartBudgetMs)
            {
                failures.Add($"Depth {depth} cold-cache p95 exceeded the {coldStartBudgetMs}ms budget (baseline {baselineWarmupP95:F2}ms, active {activeWarmupP95:F2}ms).");
            }
            if (activeP95 > baselineP95 * 1.25)
            {
                failures.Add($"Depth {depth} active p95 {activeP95:F2}ms exceeded baseline {baselineP95:F2}ms by more than 25%.");
            }
            if (activeRows > baselineRows * 5)
            {
                failures.Add($"Depth {depth} active plan rows {activeRows} exceeded baseline rows {baselineRows} by more than 5x.");
            }

            measurements.Add(new PairedDepthMeasurement(
                depth,
                SamplesPerModeAndDepth,
                baselineWarmup,
                activeWarmup,
                baselineElapsed,
                activeElapsed,
                LlmWikiProductionCorpusPerformanceTests.Percentile(baselineElapsed, 0.50),
                baselineP95,
                LlmWikiProductionCorpusPerformanceTests.Percentile(activeElapsed, 0.50),
                activeP95,
                activeP95 / baselineP95,
                baselineResult.Count,
                activeResult.Count,
                baselineRows,
                activeRows));
        }

        var resultPath = Environment.GetEnvironmentVariable("SLOGS_SEMANTIC_COMPARISON_RESULT");
        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                comparison = "same-process-alternating-stable-blocks",
                owner,
                semanticVersion,
                samplesPerModeAndDepth = SamplesPerModeAndDepth,
                measurements
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task<IReadOnlyList<Guid>> MeasureBlockAsync(
        NpgsqlDataSource dataSource,
        string owner,
        string queryVector,
        string queryNodeKey,
        int depth,
        bool active,
        ICollection<double> elapsed,
        int sampleCount)
    {
        IReadOnlyList<Guid>? firstResult = null;
        for (var sample = 0; sample < sampleCount; sample++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await LlmWikiProductionCorpusPerformanceTests.ExecuteSearchAsync(
                dataSource, owner, queryVector, queryNodeKey, depth, active);
            stopwatch.Stop();
            elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
            firstResult ??= result;
            Assert.Equal(firstResult, result);
        }
        return firstResult!;
    }

    private sealed record PairedDepthMeasurement(
        int Depth,
        int SamplesPerMode,
        IReadOnlyList<double> BaselineWarmupMs,
        IReadOnlyList<double> ActiveWarmupMs,
        IReadOnlyList<double> BaselineElapsedMs,
        IReadOnlyList<double> ActiveElapsedMs,
        double BaselineP50Ms,
        double BaselineP95Ms,
        double ActiveP50Ms,
        double ActiveP95Ms,
        double P95Ratio,
        int BaselineResultCount,
        int ActiveResultCount,
        long BaselineMaximumActualRows,
        long ActiveMaximumActualRows);
}
