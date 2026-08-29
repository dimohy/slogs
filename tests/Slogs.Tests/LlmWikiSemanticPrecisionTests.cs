using System.Text.Json;
using Npgsql;
using Xunit;

namespace Slogs.Tests;

public sealed class LlmWikiSemanticPrecisionTests
{
    [Fact]
    [Trait("Category", "PostgreSqlSemanticPrecision")]
    public async Task Reviewed_semantic_holdout_returns_only_the_expected_typed_paths()
    {
        var connectionString = Environment.GetEnvironmentVariable("SLOGS_PRODUCTION_CORPUS_POSTGRES");
        var fixturePath = Environment.GetEnvironmentVariable("SLOGS_SEMANTIC_PRECISION_HOLDOUT");
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(fixturePath))
        {
            return;
        }

        var fixture = JsonSerializer.Deserialize<SemanticHoldout>(
            await File.ReadAllTextAsync(fixturePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Semantic precision holdout is empty.");
        Assert.Equal(1, fixture.SchemaVersion);
        Assert.NotEmpty(fixture.Cases);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await AssertBaselineHasNoTypedPathsAsync(dataSource, fixture);

        var results = new List<SemanticHoldoutResult>();
        var failures = new List<string>();
        foreach (var testCase in fixture.Cases)
        {
            var result = await FindTargetAsync(dataSource, fixture.Owner, testCase);
            if (result is null)
            {
                failures.Add($"{testCase.Name}: target {testCase.TargetEntryId} was not returned.");
                continue;
            }
            if (result.GraphDepth != testCase.ExpectedGraphDepth || result.SemanticPath != testCase.ExpectedSemanticPath)
            {
                failures.Add(
                    $"{testCase.Name}: expected depth/path {testCase.ExpectedGraphDepth}/'{testCase.ExpectedSemanticPath}', got {result.GraphDepth}/'{result.SemanticPath}'.");
            }
            results.Add(new SemanticHoldoutResult(
                testCase.Name,
                testCase.SourceEntryId,
                testCase.TargetEntryId,
                result.GraphDepth,
                result.SemanticPath));
        }

        var resultPath = Environment.GetEnvironmentVariable("SLOGS_SEMANTIC_PRECISION_RESULT");
        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                fixture = Path.GetFileName(fixturePath),
                fixture.Cases.Count,
                passed = results.Count,
                failures,
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task AssertBaselineHasNoTypedPathsAsync(
        NpgsqlDataSource dataSource,
        SemanticHoldout fixture)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var deactivate = new NpgsqlCommand(
            "UPDATE \"LlmWikiSemanticGraphVersions\" SET \"State\"='validated', \"ActivatedAt\"=NULL WHERE \"OwnerUserName\"=@owner AND \"State\"='active';",
            connection,
            transaction))
        {
            deactivate.Parameters.AddWithValue("owner", fixture.Owner);
            Assert.True(await deactivate.ExecuteNonQueryAsync() > 0, "The holdout requires one active semantic graph version.");
        }

        foreach (var testCase in fixture.Cases)
        {
            var result = await FindTargetAsync(connection, transaction, fixture.Owner, testCase);
            Assert.True(result is null || string.IsNullOrEmpty(result.SemanticPath),
                $"Baseline unexpectedly returned typed path '{result?.SemanticPath}' for '{testCase.Name}'.");
        }

        await transaction.RollbackAsync();
    }

    private static async Task<SearchResult?> FindTargetAsync(
        NpgsqlDataSource dataSource,
        string owner,
        SemanticHoldoutCase testCase)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await FindTargetAsync(connection, null, owner, testCase);
    }

    private static async Task<SearchResult?> FindTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string owner,
        SemanticHoldoutCase testCase)
    {
        await using var command = new NpgsqlCommand(
            """
            WITH RECURSIVE active_version AS (
                SELECT "Version"
                FROM "LlmWikiSemanticGraphVersions"
                WHERE "OwnerUserName"=@owner AND "State"='active'
                LIMIT 1
            ), semantic_walk AS (
                SELECT
                    @sourceEntityKey::text AS entity_key,
                    0 AS depth,
                    ARRAY[@sourceEntityKey::text] AS visited,
                    ARRAY[]::text[] AS relation_path
                FROM active_version
                UNION ALL
                SELECT
                    edge.to_entity_key,
                    walk.depth + 1,
                    walk.visited || edge.to_entity_key,
                    walk.relation_path || edge.path_label
                FROM semantic_walk AS walk
                INNER JOIN active_version AS active ON TRUE
                INNER JOIN LATERAL (
                    SELECT directed.to_entity_key, directed.confidence, directed.path_label
                    FROM (
                        SELECT
                            relation."ToEntityKey" AS to_entity_key,
                            relation."Confidence" AS confidence,
                            relation."RelationType" AS relation_type,
                            relation."RelationType" AS path_label
                        FROM "LlmWikiSemanticRelations" AS relation
                        WHERE relation."OwnerUserName"=@owner
                          AND relation."Version"=active."Version"
                          AND relation."State"='active'
                          AND relation."FromEntityKey"=walk.entity_key
                        UNION ALL
                        SELECT
                            relation."FromEntityKey" AS to_entity_key,
                            relation."Confidence" AS confidence,
                            relation."RelationType" AS relation_type,
                            'inverse:' || relation."RelationType" AS path_label
                        FROM "LlmWikiSemanticRelations" AS relation
                        WHERE relation."OwnerUserName"=@owner
                          AND relation."Version"=active."Version"
                          AND relation."State"='active'
                          AND relation."ToEntityKey"=walk.entity_key
                    ) AS directed
                    ORDER BY
                        CASE WHEN directed.relation_type='part-of' THEN 1 ELSE 0 END,
                        directed.confidence DESC,
                        directed.to_entity_key,
                        directed.path_label
                    LIMIT 8
                ) AS edge ON TRUE
                WHERE walk.depth < @maxGraphHops
                  AND NOT edge.to_entity_key=ANY(walk.visited)
            )
            SELECT mention."EntryId", walk.depth, array_to_string(walk.relation_path, ' > ')
            FROM semantic_walk AS walk
            INNER JOIN active_version AS active ON TRUE
            INNER JOIN "LlmWikiSemanticMentions" AS mention
                ON mention."OwnerUserName"=@owner
               AND mention."Version"=active."Version"
               AND mention."EntityKey"=walk.entity_key
            WHERE mention."EntryId"=@targetEntryId
              AND walk.depth BETWEEN 1 AND @maxGraphHops
            ORDER BY walk.depth, array_to_string(walk.relation_path, ' > ')
            LIMIT 1;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("sourceEntityKey", $"memory:{testCase.SourceEntryId}");
        command.Parameters.AddWithValue("targetEntryId", testCase.TargetEntryId);
        command.Parameters.AddWithValue("maxGraphHops", testCase.MaxGraphHops);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new SearchResult(reader.GetInt32(1), reader.GetString(2));
        }
        return null;
    }

    private sealed record SemanticHoldout(int SchemaVersion, string Owner, IReadOnlyList<SemanticHoldoutCase> Cases);
    private sealed record SemanticHoldoutCase(
        string Name,
        Guid SourceEntryId,
        Guid TargetEntryId,
        int MaxGraphHops,
        int ExpectedGraphDepth,
        string ExpectedSemanticPath);
    private sealed record SearchResult(int GraphDepth, string SemanticPath);
    private sealed record SemanticHoldoutResult(
        string Name,
        Guid SourceEntryId,
        Guid TargetEntryId,
        int GraphDepth,
        string SemanticPath);
}
