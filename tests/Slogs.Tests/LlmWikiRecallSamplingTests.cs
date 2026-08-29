using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class LlmWikiRecallSamplingTests
{
    [Fact]
    [Trait("Category", "PostgreSqlRecallSampling")]
    public async Task Every_authoritative_row_survives_one_thousand_three_hop_recall_samples()
    {
        var connectionString = Environment.GetEnvironmentVariable("SLOGS_PRODUCTION_CORPUS_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var sampleCount = int.TryParse(Environment.GetEnvironmentVariable("SLOGS_RECALL_SAMPLE_COUNT"), out var requested)
            ? requested
            : 1_000;
        Assert.InRange(sampleCount, 1, 10_000);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await LlmWikiProductionCorpusPerformanceTests.RebuildGraphStatisticsAsync(dataSource);
        var before = await ReadEveryAuthoritativeRowAsync(dataSource);
        Assert.Equal(450, before.EntryCount);
        Assert.Equal(1_834, before.SourceCount);

        var candidates = await LoadRecallCandidatesAsync(dataSource);
        Assert.Equal(before.EntryCount, candidates.Count);
        var random = new Random(20260829);
        var samples = new List<RecallSample>(sampleCount);
        var legacyElapsed = new List<double>(sampleCount);
        var legacyRanks = new List<int>(sampleCount);
        var elapsedByDepth = new Dictionary<int, List<double>>
        {
            [1] = [],
            [2] = [],
            [3] = []
        };
        var ranksByDepth = new Dictionary<int, List<int>>
        {
            [1] = [],
            [2] = [],
            [3] = []
        };
        var graphDepth2Observations = 0;
        var graphDepth3Observations = 0;

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var candidate = candidates[random.Next(candidates.Count)];
            var legacyStopwatch = Stopwatch.StartNew();
            var legacyResult = await ExecuteRecallAsync(dataSource, candidate, 1, legacy: true);
            legacyStopwatch.Stop();
            var legacyRank = legacyResult.FindIndex(x => x.Id == candidate.Id) + 1;
            Assert.InRange(legacyRank, 1, 10);
            legacyElapsed.Add(legacyStopwatch.Elapsed.TotalMilliseconds);
            legacyRanks.Add(legacyRank);

            var depthResults = new List<DepthRecall>(3);
            foreach (var depth in new[] { 1, 2, 3 })
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await ExecuteRecallAsync(dataSource, candidate, depth, legacy: false);
                stopwatch.Stop();
                elapsedByDepth[depth].Add(stopwatch.Elapsed.TotalMilliseconds);

                var recallRank = result.FindIndex(x => x.Id == candidate.Id) + 1;
                Assert.InRange(recallRank, 1, 10);
                ranksByDepth[depth].Add(recallRank);
                if (depth == 1)
                {
                    Assert.Equal(legacyResult.Select(x => x.Id), result.Select(x => x.Id));
                }
                Assert.All(result, item => Assert.InRange(item.GraphDepth, 0, depth));
                if (depth == 2 && result.Any(x => x.GraphDepth == 2))
                {
                    graphDepth2Observations++;
                }
                if (depth == 3 && result.Any(x => x.GraphDepth == 3))
                {
                    graphDepth3Observations++;
                }

                depthResults.Add(new DepthRecall(
                    depth,
                    stopwatch.Elapsed.TotalMilliseconds,
                    recallRank,
                    result.Count,
                    result.Count == 0 ? 0 : result.Max(x => x.GraphDepth)));
            }
            samples.Add(new RecallSample(
                sampleIndex,
                candidate.Id,
                candidate.Owner,
                candidate.NodeKey,
                new LegacyRecall(legacyStopwatch.Elapsed.TotalMilliseconds, legacyRank, legacyResult.Count),
                depthResults));
        }

        var after = await ReadEveryAuthoritativeRowAsync(dataSource);
        Assert.Equal(before, after);

        var outputPath = Environment.GetEnvironmentVariable("SLOGS_PRODUCTION_CORPUS_RESULT");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                deterministicSeed = 20260829,
                authoritativeRead = before,
                sampleCount,
                queryExecutions = sampleCount * 3,
                comparisonQueryExecutions = sampleCount * 4,
                graphDepth2Observations,
                graphDepth3Observations,
                legacy = new
                {
                    sourceCommit = "153465004c2768d8497e82b137198d64fa36396f",
                    latency = Summarize(legacyElapsed),
                    quality = SummarizeQuality(legacyRanks)
                },
                quality = new
                {
                    depth1 = SummarizeQuality(ranksByDepth[1]),
                    depth2 = SummarizeQuality(ranksByDepth[2]),
                    depth3 = SummarizeQuality(ranksByDepth[3])
                },
                latency = new
                {
                    depth1 = Summarize(elapsedByDepth[1]),
                    depth2 = Summarize(elapsedByDepth[2]),
                    depth3 = Summarize(elapsedByDepth[3])
                },
                samples
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        if (sampleCount >= 1_000)
        {
            Assert.True(graphDepth2Observations > 0, "The production sample never observed a depth-2 graph result.");
            Assert.True(graphDepth3Observations > 0, "The production sample never observed a depth-3 graph result.");

            var legacyQuality = SummarizeQuality(legacyRanks);
            var legacyP95 = Percentile(legacyElapsed, 0.95);
            foreach (var depth in new[] { 1, 2, 3 })
            {
                var currentQuality = SummarizeQuality(ranksByDepth[depth]);
                Assert.True(
                    currentQuality.MeanReciprocalRank + 1e-12 >= legacyQuality.MeanReciprocalRank,
                    $"Depth {depth} MRR regressed from the frozen legacy query.");
                Assert.True(
                    currentQuality.HitAt10 >= legacyQuality.HitAt10,
                    $"Depth {depth} Hit@10 regressed from the frozen legacy query.");
                Assert.True(
                    Percentile(elapsedByDepth[depth], 0.95) <= legacyP95 * 1.25,
                    $"Depth {depth} p95 exceeded the frozen legacy query by more than 25%.");
            }
        }
        Assert.InRange(Percentile(elapsedByDepth[1], 0.95), 0, 500);
        Assert.InRange(Percentile(elapsedByDepth[2], 0.95), 0, 500);
        Assert.InRange(Percentile(elapsedByDepth[3], 0.95), 0, 750);
    }

    private static async Task<List<RecallCandidate>> LoadRecallCandidatesAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                e."Id",
                e."OwnerUserName",
                idx."Embedding"::text,
                selected_node."NodeKey"
            FROM "LlmWikiEntries" AS e
            INNER JOIN "LlmWikiEntryEmbeddings" AS idx
                ON idx."EntryId" = e."Id"
            INNER JOIN LATERAL (
                SELECT nodes."NodeKey"
                FROM "LlmWikiEntryGraphNodes" AS nodes
                WHERE nodes."EntryId" = e."Id"
                ORDER BY nodes."Weight" DESC, nodes."NodeKey"
                LIMIT 1
            ) AS selected_node ON TRUE
            ORDER BY e."Id";
            """,
            connection);
        var result = new List<RecallCandidate>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new RecallCandidate(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        return result;
    }

    private static async Task<List<RecallResult>> ExecuteRecallAsync(
        NpgsqlDataSource dataSource,
        RecallCandidate candidate,
        int depth,
        bool legacy)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var commandText = legacy
            ? LlmWikiLegacyGraphSearchCommand.CommandText
            : LlmWikiGraphSearchCommand.CommandText;
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddWithValue("owner", candidate.Owner);
        command.Parameters.AddWithValue("publicOnly", false);
        command.Parameters.AddWithValue("model", "embeddinggemma");
        command.Parameters.AddWithValue("dimensions", 768);
        command.Parameters.AddWithValue("queryVector", candidate.Vector);
        command.Parameters.AddWithValue("categoryPath", string.Empty);
        command.Parameters.AddWithValue("categoryPrefix", string.Empty);
        command.Parameters.Add(new NpgsqlParameter("queryNodeKeys", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = new[] { candidate.NodeKey }
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

        var result = new List<RecallResult>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new RecallResult(reader.GetGuid(0), reader.GetInt32(2)));
        }
        return result;
    }

    private static async Task<AuthoritativeRead> ReadEveryAuthoritativeRowAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT 'entry' AS kind, e."Id"::text AS id, row_to_json(e)::text AS payload
            FROM "LlmWikiEntries" AS e
            UNION ALL
            SELECT 'source' AS kind, s."Id"::text AS id, row_to_json(s)::text AS payload
            FROM "LlmWikiEntrySources" AS s
            ORDER BY kind, id;
            """,
            connection);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entryCount = 0;
        var sourceCount = 0;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var kind = reader.GetString(0);
            if (kind == "entry")
            {
                entryCount++;
            }
            else
            {
                sourceCount++;
            }
            var line = $"{kind}\t{reader.GetString(1)}\t{reader.GetString(2)}\n";
            hash.AppendData(Encoding.UTF8.GetBytes(line));
        }
        return new AuthoritativeRead(entryCount, sourceCount, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var ordered = values.Order().ToArray();
        return ordered[Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1)];
    }

    private static LatencySummary Summarize(IReadOnlyList<double> values)
        => new(values.Min(), Percentile(values, 0.50), Percentile(values, 0.95), values.Max());

    private static QualitySummary SummarizeQuality(IReadOnlyList<int> ranks)
        => new(
            ranks.Count(x => x <= 1),
            ranks.Count(x => x <= 5),
            ranks.Count(x => x <= 10),
            ranks.Average(x => 1.0 / x),
            ranks.Average());

    private sealed record RecallCandidate(Guid Id, string Owner, string Vector, string NodeKey);
    private sealed record RecallResult(Guid Id, int GraphDepth);
    private sealed record AuthoritativeRead(int EntryCount, int SourceCount, string Sha256);
    private sealed record LegacyRecall(double ElapsedMs, int RecallRank, int ResultCount);
    private sealed record DepthRecall(int Depth, double ElapsedMs, int RecallRank, int ResultCount, int MaximumGraphDepth);
    private sealed record RecallSample(
        int SampleIndex,
        Guid EntryId,
        string Owner,
        string NodeKey,
        LegacyRecall Legacy,
        IReadOnlyList<DepthRecall> Depths);
    private sealed record LatencySummary(double MinimumMs, double P50Ms, double P95Ms, double MaximumMs);
    private sealed record QualitySummary(int HitAt1, int HitAt5, int HitAt10, double MeanReciprocalRank, double MeanRank);
}
