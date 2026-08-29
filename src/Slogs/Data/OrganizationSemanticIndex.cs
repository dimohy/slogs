using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public interface IOrganizationSemanticIndex
{
    Task IndexAsync(OrganizationMemoryRecord memory, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, double>> ScoreAsync(
        Guid organizationId,
        string query,
        IReadOnlyList<Guid> candidateIds,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationSemanticIndex(
    IDbContextFactory<OrganizationDbContext> dbFactory,
    EmbeddingGemmaService embeddingService) : IOrganizationSemanticIndex
{
    private const string IndexVersion = "2026-08-25-organization-memory-v1";
    private const int MaxEmbeddingContentLength = 18_000;

    public async Task IndexAsync(OrganizationMemoryRecord memory, CancellationToken cancellationToken = default)
    {
        var document = BuildDocument(memory);
        var embedding = await embeddingService.EmbedDocumentAsync(document, cancellationToken);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document))).ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            INSERT INTO organization."OrganizationMemoryEmbeddings"
                ("MemoryId", "OrganizationId", "Model", "Dimensions", "ContentHash", "IndexVersion", "Embedding", "UpdatedAt")
            VALUES
                (@memoryId, @organizationId, @model, @dimensions, @contentHash, @indexVersion, CAST(@embedding AS vector), @updatedAt)
            ON CONFLICT ("MemoryId") DO UPDATE SET
                "OrganizationId" = EXCLUDED."OrganizationId",
                "Model" = EXCLUDED."Model",
                "Dimensions" = EXCLUDED."Dimensions",
                "ContentHash" = EXCLUDED."ContentHash",
                "IndexVersion" = EXCLUDED."IndexVersion",
                "Embedding" = EXCLUDED."Embedding",
                "UpdatedAt" = EXCLUDED."UpdatedAt";
            """;
        AddParameter(command, "memoryId", memory.Id);
        AddParameter(command, "organizationId", memory.OrganizationId);
        AddParameter(command, "model", embeddingService.Model);
        AddParameter(command, "dimensions", embeddingService.Dimensions);
        AddParameter(command, "contentHash", contentHash);
        AddParameter(command, "indexVersion", IndexVersion);
        AddParameter(command, "embedding", ToVectorLiteral(embedding));
        AddParameter(command, "updatedAt", DateTime.UtcNow);
        await EnsureOpenAsync(command.Connection!, cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, double>> ScoreAsync(
        Guid organizationId,
        string query,
        IReadOnlyList<Guid> candidateIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (candidateIds.Count == 0)
        {
            return new Dictionary<Guid, double>();
        }

        var queryEmbedding = await embeddingService.EmbedQueryAsync(query, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT
                "MemoryId",
                GREATEST(0.0, 1 - ("Embedding" <=> CAST(@queryEmbedding AS vector))) AS "Score"
            FROM organization."OrganizationMemoryEmbeddings"
            WHERE "OrganizationId" = @organizationId
              AND "MemoryId" = ANY(@candidateIds)
              AND "Model" = @model
              AND "Dimensions" = @dimensions
              AND "IndexVersion" = @indexVersion
            ORDER BY "Embedding" <=> CAST(@queryEmbedding AS vector)
            LIMIT @limit;
            """;
        AddParameter(command, "organizationId", organizationId);
        AddParameter(command, "candidateIds", candidateIds.ToArray());
        AddParameter(command, "model", embeddingService.Model);
        AddParameter(command, "dimensions", embeddingService.Dimensions);
        AddParameter(command, "indexVersion", IndexVersion);
        AddParameter(command, "queryEmbedding", ToVectorLiteral(queryEmbedding));
        AddParameter(command, "limit", Math.Clamp(limit, 1, 5000));
        await EnsureOpenAsync(command.Connection!, cancellationToken);
        var scores = new Dictionary<Guid, double>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            scores[reader.GetGuid(0)] = reader.GetDouble(1);
        }

        return scores;
    }

    private static string BuildDocument(OrganizationMemoryRecord memory)
    {
        var text = string.Join(
            "\n",
            memory.Title,
            memory.Summary,
            memory.SourcePrompt,
            memory.Content,
            memory.CategoryPath,
            memory.TagsJson);
        return text.Length <= MaxEmbeddingContentLength ? text : text[..MaxEmbeddingContentLength];
    }

    private static string ToVectorLiteral(IReadOnlyList<float> values)
        => $"[{string.Join(',', values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))}]";

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task EnsureOpenAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }
}
