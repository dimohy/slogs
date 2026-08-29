using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Slogs.Data;

public sealed record BgeM3ShadowIndexResult(
    string Phase,
    int PersonalEntries,
    int OrganizationMemories,
    int EmbeddedDocuments,
    string Model,
    int Dimensions);

public sealed class BgeM3ShadowIndexMigration(
    IDbContextFactory<SlogsDbContext> slogsFactory,
    IDbContextFactory<OrganizationDbContext> organizationFactory,
    BgeM3EmbeddingService embeddingService)
{
    private const int SafeBatchSize = 8;
    private const string PersonalStage = "LlmWikiEntryEmbeddingsBgeM3Stage";
    private const string OrganizationStage = "OrganizationMemoryEmbeddingsBgeM3Stage";

    public async Task<BgeM3ShadowIndexResult> PrepareAsync(CancellationToken cancellationToken = default)
    {
        await embeddingService.VerifyRuntimeAsync(cancellationToken);
        await EnsureStageTablesAsync(cancellationToken);

        await using var slogs = await slogsFactory.CreateDbContextAsync(cancellationToken);
        var personalSources = (await slogs.LlmWikiEntries.AsNoTracking()
                .OrderBy(entry => entry.Id)
                .ToListAsync(cancellationToken))
            .Select(LlmWikiService.BuildBgeM3SourceDocument)
            .ToArray();

        await using var organization = await organizationFactory.CreateDbContextAsync(cancellationToken);
        var organizationRecords = await organization.OrganizationMemories.AsNoTracking()
            .Where(memory => memory.State == OrganizationMemoryStates.Active)
            .OrderBy(memory => memory.Id)
            .ToListAsync(cancellationToken);
        var organizationSources = organizationRecords.Select(memory =>
        {
            var text = OrganizationSemanticIndex.BuildDocument(memory);
            return new OrganizationSource(
                memory.Id,
                memory.OrganizationId,
                memory.UpdatedAt,
                text,
                Sha256($"{OrganizationSemanticIndex.IndexVersion}\n{text}"));
        }).ToArray();

        var personalEmbedded = await PreparePersonalAsync(personalSources, cancellationToken);
        var organizationEmbedded = await PrepareOrganizationAsync(organizationSources, cancellationToken);
        await ValidatePreparedAsync(personalSources, organizationSources, cancellationToken);
        return new BgeM3ShadowIndexResult(
            "prepared",
            personalSources.Length,
            organizationSources.Length,
            personalEmbedded + organizationEmbedded,
            embeddingService.Model,
            embeddingService.Dimensions);
    }

    public async Task<BgeM3ShadowIndexResult> ActivateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await slogsFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await ExecuteAsync(db,
            $"""
            LOCK TABLE "LlmWikiEntries", "{PersonalStage}",
                organization."OrganizationMemories", organization."{OrganizationStage}" IN ACCESS EXCLUSIVE MODE;
            """, cancellationToken);

        var personalMismatch = await ScalarAsync<long>(db,
            $"""
            SELECT COUNT(*) FROM (
                SELECT e."Id"
                FROM "LlmWikiEntries" e
                FULL JOIN "{PersonalStage}" s ON s."EntryId"=e."Id"
                WHERE e."Id" IS NULL OR s."EntryId" IS NULL OR s."SourceUpdatedAt"<>e."UpdatedAt"
            ) mismatch;
            """, cancellationToken);
        var organizationMismatch = await ScalarAsync<long>(db,
            $"""
            SELECT COUNT(*) FROM (
                SELECT COALESCE(m."Id", s."MemoryId")
                FROM (SELECT * FROM organization."OrganizationMemories" WHERE "State"='{OrganizationMemoryStates.Active}') m
                FULL JOIN organization."{OrganizationStage}" s ON s."MemoryId"=m."Id"
                WHERE m."Id" IS NULL OR s."MemoryId" IS NULL OR s."SourceUpdatedAt"<>m."UpdatedAt"
            ) mismatch;
            """, cancellationToken);
        if (personalMismatch != 0 || organizationMismatch != 0)
        {
            throw new InvalidOperationException(
                $"BGE-M3 shadow index is stale: personalMismatch={personalMismatch}, organizationMismatch={organizationMismatch}.");
        }

        var legacyExists = await ScalarAsync<bool>(db,
            """
            SELECT to_regclass('"LlmWikiEntryEmbeddingsEmbeddingGemmaLegacy"') IS NOT NULL
                OR to_regclass('organization."OrganizationMemoryEmbeddingsEmbeddingGemmaLegacy"') IS NOT NULL;
            """, cancellationToken);
        if (legacyExists)
        {
            throw new InvalidOperationException("EmbeddingGemma legacy tables already exist; activation is not repeatable.");
        }

        await ExecuteAsync(db,
            $"""
            ALTER TABLE "LlmWikiEntryEmbeddings" RENAME TO "LlmWikiEntryEmbeddingsEmbeddingGemmaLegacy";
            ALTER TABLE "{PersonalStage}" RENAME TO "LlmWikiEntryEmbeddings";
            ALTER TABLE "LlmWikiEntryEmbeddings" DROP COLUMN "SourceUpdatedAt";
            ALTER TABLE organization."OrganizationMemoryEmbeddings" RENAME TO "OrganizationMemoryEmbeddingsEmbeddingGemmaLegacy";
            ALTER TABLE organization."{OrganizationStage}" RENAME TO "OrganizationMemoryEmbeddings";
            ALTER TABLE organization."OrganizationMemoryEmbeddings" DROP COLUMN "SourceUpdatedAt";
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BgeM3ShadowIndexResult(
            "activated",
            await CountAsync("\"LlmWikiEntryEmbeddings\"", cancellationToken),
            await CountAsync("organization.\"OrganizationMemoryEmbeddings\"", cancellationToken),
            0,
            embeddingService.Model,
            embeddingService.Dimensions);
    }

    public async Task<BgeM3ShadowIndexResult> ValidateActiveAsync(CancellationToken cancellationToken = default)
    {
        await embeddingService.VerifyRuntimeAsync(cancellationToken);
        await using var db = await slogsFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        var personalSources = await ScalarAsync<long>(db,
            "SELECT COUNT(*) FROM \"LlmWikiEntries\";", cancellationToken);
        var personalValid = await ScalarAsync<long>(db,
            $"""
            SELECT COUNT(*) FROM "LlmWikiEntryEmbeddings"
            WHERE "Model"='{embeddingService.Model}'
              AND "Dimensions"={embeddingService.Dimensions}
              AND "IndexVersion"='{LlmWikiService.SearchIndexVersion}';
            """, cancellationToken);
        var organizationSources = await ScalarAsync<long>(db,
            $"SELECT COUNT(*) FROM organization.\"OrganizationMemories\" WHERE \"State\"='{OrganizationMemoryStates.Active}';",
            cancellationToken);
        var organizationValid = await ScalarAsync<long>(db,
            $"""
            SELECT COUNT(*) FROM organization."OrganizationMemoryEmbeddings"
            WHERE "Model"='{embeddingService.Model}'
              AND "Dimensions"={embeddingService.Dimensions}
              AND "IndexVersion"='{OrganizationSemanticIndex.IndexVersion}';
            """, cancellationToken);
        if (personalSources != personalValid || organizationSources != organizationValid)
        {
            throw new InvalidOperationException(
                $"BGE-M3 active index validation failed: personal={personalValid}/{personalSources}, organization={organizationValid}/{organizationSources}.");
        }

        var vectorProbe = await ScalarAsync<double>(db,
            """
            SELECT COALESCE(MIN("Embedding" <=> "Embedding"), 0.0)
            FROM "LlmWikiEntryEmbeddings";
            """, cancellationToken);
        if (Math.Abs(vectorProbe) > 0.000001)
        {
            throw new InvalidOperationException($"BGE-M3 vector distance probe failed: {vectorProbe}.");
        }

        return new BgeM3ShadowIndexResult(
            "validated",
            checked((int)personalValid),
            checked((int)organizationValid),
            0,
            embeddingService.Model,
            embeddingService.Dimensions);
    }

    public async Task<BgeM3ShadowIndexResult> RollbackAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await slogsFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await ExecuteAsync(db,
            """
            LOCK TABLE "LlmWikiEntryEmbeddings", "LlmWikiEntryEmbeddingsEmbeddingGemmaLegacy",
                organization."OrganizationMemoryEmbeddings",
                organization."OrganizationMemoryEmbeddingsEmbeddingGemmaLegacy" IN ACCESS EXCLUSIVE MODE;
            """, cancellationToken);
        var rollbackExists = await ScalarAsync<bool>(db,
            """
            SELECT to_regclass('"LlmWikiEntryEmbeddingsBgeM3Rollback"') IS NOT NULL
                OR to_regclass('organization."OrganizationMemoryEmbeddingsBgeM3Rollback"') IS NOT NULL;
            """, cancellationToken);
        if (rollbackExists)
        {
            throw new InvalidOperationException("BGE-M3 rollback tables already exist; refusing to overwrite them.");
        }
        await ExecuteAsync(db,
            """
            ALTER TABLE "LlmWikiEntryEmbeddings" RENAME TO "LlmWikiEntryEmbeddingsBgeM3Rollback";
            ALTER TABLE "LlmWikiEntryEmbeddingsEmbeddingGemmaLegacy" RENAME TO "LlmWikiEntryEmbeddings";
            ALTER TABLE organization."OrganizationMemoryEmbeddings" RENAME TO "OrganizationMemoryEmbeddingsBgeM3Rollback";
            ALTER TABLE organization."OrganizationMemoryEmbeddingsEmbeddingGemmaLegacy" RENAME TO "OrganizationMemoryEmbeddings";
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BgeM3ShadowIndexResult(
            "rolled-back",
            await CountAsync("\"LlmWikiEntryEmbeddings\"", cancellationToken),
            await CountAsync("organization.\"OrganizationMemoryEmbeddings\"", cancellationToken),
            0,
            "embeddinggemma",
            768);
    }

    public async Task<BgeM3ShadowIndexResult> FinalizeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await slogsFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        var invalid = await ScalarAsync<long>(db,
            """
            SELECT
                (SELECT COUNT(*) FROM "LlmWikiEntryEmbeddings" WHERE "Model"<>'bge-m3' OR "Dimensions"<>1024)
              + (SELECT COUNT(*) FROM organization."OrganizationMemoryEmbeddings" WHERE "Model"<>'bge-m3' OR "Dimensions"<>1024);
            """, cancellationToken);
        if (invalid != 0)
        {
            throw new InvalidOperationException($"Cannot remove EmbeddingGemma: {invalid} active embedding rows violate the BGE-M3 contract.");
        }
        await ExecuteAsync(db,
            """
            DROP TABLE "LlmWikiEntryEmbeddingsEmbeddingGemmaLegacy";
            DROP TABLE organization."OrganizationMemoryEmbeddingsEmbeddingGemmaLegacy";
            """, cancellationToken);
        return new BgeM3ShadowIndexResult(
            "finalized",
            await CountAsync("\"LlmWikiEntryEmbeddings\"", cancellationToken),
            await CountAsync("organization.\"OrganizationMemoryEmbeddings\"", cancellationToken),
            0,
            embeddingService.Model,
            embeddingService.Dimensions);
    }

    private async Task EnsureStageTablesAsync(CancellationToken cancellationToken)
    {
        await using var db = await slogsFactory.CreateDbContextAsync(cancellationToken);
        await ExecuteAsync(db,
            $"""
            CREATE TABLE IF NOT EXISTS "{PersonalStage}" (
                "EntryId" uuid PRIMARY KEY REFERENCES "LlmWikiEntries"("Id") ON DELETE CASCADE,
                "OwnerUserName" character varying(80) NOT NULL,
                "Model" character varying(80) NOT NULL CHECK ("Model"='bge-m3'),
                "Dimensions" integer NOT NULL CHECK ("Dimensions"=1024),
                "ContentHash" character varying(64) NOT NULL,
                "IndexVersion" character varying(40) NOT NULL,
                "Embedding" vector(1024) NOT NULL,
                "SourceUpdatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryEmbeddingsBgeM3Stage_Owner"
                ON "{PersonalStage}" ("OwnerUserName", "Model", "Dimensions", "IndexVersion");
            CREATE INDEX IF NOT EXISTS "IX_LlmWikiEntryEmbeddingsBgeM3Stage_Hnsw"
                ON "{PersonalStage}" USING hnsw ("Embedding" vector_cosine_ops);

            CREATE TABLE IF NOT EXISTS organization."{OrganizationStage}" (
                "MemoryId" uuid PRIMARY KEY REFERENCES organization."OrganizationMemories"("Id") ON DELETE CASCADE,
                "OrganizationId" uuid NOT NULL REFERENCES organization."Organizations"("Id") ON DELETE CASCADE,
                "Model" character varying(80) NOT NULL CHECK ("Model"='bge-m3'),
                "Dimensions" integer NOT NULL CHECK ("Dimensions"=1024),
                "ContentHash" character varying(64) NOT NULL,
                "IndexVersion" character varying(80) NOT NULL,
                "Embedding" vector(1024) NOT NULL,
                "SourceUpdatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_OrganizationMemoryEmbeddingsBgeM3Stage_Organization"
                ON organization."{OrganizationStage}" ("OrganizationId", "Model", "Dimensions", "IndexVersion");
            CREATE INDEX IF NOT EXISTS "IX_OrganizationMemoryEmbeddingsBgeM3Stage_Hnsw"
                ON organization."{OrganizationStage}" USING hnsw ("Embedding" vector_cosine_ops);
            """, cancellationToken);
    }

    private async Task<int> PreparePersonalAsync(
        IReadOnlyList<BgeM3SourceDocument> sources,
        CancellationToken cancellationToken)
    {
        var prepared = await ReadPreparedAsync(PersonalStage, "EntryId", cancellationToken);
        var pending = sources.Where(source =>
            !prepared.TryGetValue(source.Id, out var current)
            || current.SourceUpdatedAt != source.SourceUpdatedAt
            || current.ContentHash != source.ContentHash).ToArray();
        for (var offset = 0; offset < pending.Length; offset += SafeBatchSize)
        {
            var batch = pending.Skip(offset).Take(SafeBatchSize).ToArray();
            var embeddings = await embeddingService.EmbedDocumentsAsync(batch.Select(x => x.Text).ToArray(), cancellationToken);
            await using var db = await slogsFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            for (var index = 0; index < batch.Length; index++)
            {
                var source = batch[index];
                var owner = await db.LlmWikiEntries.Where(x => x.Id == source.Id)
                    .Select(x => x.OwnerUserName).SingleAsync(cancellationToken);
                await UpsertPersonalAsync(db, source, owner, embeddings[index], cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            Console.WriteLine($"BGE_M3_REINDEX personal={Math.Min(offset + batch.Length, pending.Length)}/{pending.Length}");
        }
        return pending.Length;
    }

    private async Task<int> PrepareOrganizationAsync(
        IReadOnlyList<OrganizationSource> sources,
        CancellationToken cancellationToken)
    {
        var prepared = await ReadPreparedAsync(OrganizationStage, "MemoryId", cancellationToken, "organization");
        var pending = sources.Where(source =>
            !prepared.TryGetValue(source.Id, out var current)
            || current.SourceUpdatedAt != source.SourceUpdatedAt
            || current.ContentHash != source.ContentHash).ToArray();
        for (var offset = 0; offset < pending.Length; offset += SafeBatchSize)
        {
            var batch = pending.Skip(offset).Take(SafeBatchSize).ToArray();
            var embeddings = await embeddingService.EmbedDocumentsAsync(batch.Select(x => x.Text).ToArray(), cancellationToken);
            await using var db = await slogsFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            for (var index = 0; index < batch.Length; index++)
            {
                await UpsertOrganizationAsync(db, batch[index], embeddings[index], cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            Console.WriteLine($"BGE_M3_REINDEX organization={Math.Min(offset + batch.Length, pending.Length)}/{pending.Length}");
        }
        return pending.Length;
    }

    private async Task ValidatePreparedAsync(
        IReadOnlyList<BgeM3SourceDocument> personal,
        IReadOnlyList<OrganizationSource> organization,
        CancellationToken cancellationToken)
    {
        var preparedPersonal = await ReadPreparedAsync(PersonalStage, "EntryId", cancellationToken);
        var preparedOrganization = await ReadPreparedAsync(OrganizationStage, "MemoryId", cancellationToken, "organization");
        var personalValid = preparedPersonal.Count == personal.Count && personal.All(source =>
            preparedPersonal.TryGetValue(source.Id, out var value)
            && value.SourceUpdatedAt == source.SourceUpdatedAt && value.ContentHash == source.ContentHash);
        var organizationValid = preparedOrganization.Count == organization.Count && organization.All(source =>
            preparedOrganization.TryGetValue(source.Id, out var value)
            && value.SourceUpdatedAt == source.SourceUpdatedAt && value.ContentHash == source.ContentHash);
        if (!personalValid || !organizationValid)
        {
            throw new InvalidOperationException(
                $"BGE-M3 shadow validation failed: personal={preparedPersonal.Count}/{personal.Count}, organization={preparedOrganization.Count}/{organization.Count}.");
        }
    }

    private async Task<Dictionary<Guid, PreparedSource>> ReadPreparedAsync(
        string table,
        string idColumn,
        CancellationToken cancellationToken,
        string? schema = null)
    {
        await using var db = await slogsFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        var qualified = schema is null ? $"\"{table}\"" : $"{schema}.\"{table}\"";
        command.CommandText = $"SELECT \"{idColumn}\", \"SourceUpdatedAt\", \"ContentHash\" FROM {qualified};";
        var result = new Dictionary<Guid, PreparedSource>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetGuid(0), new PreparedSource(reader.GetDateTime(1), reader.GetString(2)));
        }
        return result;
    }

    private async Task UpsertPersonalAsync(
        SlogsDbContext db,
        BgeM3SourceDocument source,
        string owner,
        IReadOnlyList<float> embedding,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"""
            INSERT INTO "{PersonalStage}"
                ("EntryId","OwnerUserName","Model","Dimensions","ContentHash","IndexVersion","Embedding","SourceUpdatedAt","UpdatedAt")
            VALUES (@id,@owner,'bge-m3',1024,@hash,@version,CAST(@embedding AS vector),@sourceUpdatedAt,@now)
            ON CONFLICT ("EntryId") DO UPDATE SET
                "OwnerUserName"=EXCLUDED."OwnerUserName", "ContentHash"=EXCLUDED."ContentHash",
                "IndexVersion"=EXCLUDED."IndexVersion", "Embedding"=EXCLUDED."Embedding",
                "SourceUpdatedAt"=EXCLUDED."SourceUpdatedAt", "UpdatedAt"=EXCLUDED."UpdatedAt";
            """;
        Add(command, "id", source.Id); Add(command, "owner", owner); Add(command, "hash", source.ContentHash);
        Add(command, "version", LlmWikiService.SearchIndexVersion); Add(command, "embedding", VectorLiteral(embedding));
        Add(command, "sourceUpdatedAt", source.SourceUpdatedAt); Add(command, "now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertOrganizationAsync(
        SlogsDbContext db,
        OrganizationSource source,
        IReadOnlyList<float> embedding,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"""
            INSERT INTO organization."{OrganizationStage}"
                ("MemoryId","OrganizationId","Model","Dimensions","ContentHash","IndexVersion","Embedding","SourceUpdatedAt","UpdatedAt")
            VALUES (@id,@organizationId,'bge-m3',1024,@hash,@version,CAST(@embedding AS vector),@sourceUpdatedAt,@now)
            ON CONFLICT ("MemoryId") DO UPDATE SET
                "OrganizationId"=EXCLUDED."OrganizationId", "ContentHash"=EXCLUDED."ContentHash",
                "IndexVersion"=EXCLUDED."IndexVersion", "Embedding"=EXCLUDED."Embedding",
                "SourceUpdatedAt"=EXCLUDED."SourceUpdatedAt", "UpdatedAt"=EXCLUDED."UpdatedAt";
            """;
        Add(command, "id", source.Id); Add(command, "organizationId", source.OrganizationId);
        Add(command, "hash", source.ContentHash); Add(command, "version", OrganizationSemanticIndex.IndexVersion);
        Add(command, "embedding", VectorLiteral(embedding)); Add(command, "sourceUpdatedAt", source.SourceUpdatedAt);
        Add(command, "now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> CountAsync(string qualifiedTable, CancellationToken cancellationToken)
    {
        await using var db = await slogsFactory.CreateDbContextAsync(cancellationToken);
        return checked((int)await ScalarAsync<long>(db, $"SELECT COUNT(*) FROM {qualifiedTable};", cancellationToken));
    }

    private static async Task ExecuteAsync(SlogsDbContext db, string sql, CancellationToken cancellationToken)
    {
        if (db.Database.GetDbConnection().State != ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(SlogsDbContext db, string sql, CancellationToken cancellationToken)
    {
        if (db.Database.GetDbConnection().State != ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
        {
            throw new InvalidOperationException("BGE-M3 migration scalar query returned no value.");
        }
        if (value is T typed)
        {
            return typed;
        }
        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string VectorLiteral(IReadOnlyList<float> values)
        => $"[{string.Join(',', values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))}]";

    private static string Sha256(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record OrganizationSource(
        Guid Id,
        Guid OrganizationId,
        DateTime SourceUpdatedAt,
        string Text,
        string ContentHash);
    private sealed record PreparedSource(DateTime SourceUpdatedAt, string ContentHash);
}
