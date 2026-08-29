using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Slogs.Data;

public static class LlmWikiSemanticGraphImporter
{
    public static async Task<LlmWikiSemanticImportResult> ImportAsync(
        IServiceProvider services,
        string manifestPath,
        string corpusDirectory,
        string version,
        bool activate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A semantic graph version is required.", nameof(version));
        }
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var manifest = JsonSerializer.Deserialize<LlmWikiSemanticGraphManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken), jsonOptions)
            ?? throw new InvalidOperationException("The semantic graph manifest is empty.");
        using var corpusManifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(corpusDirectory, "corpus-manifest.json"), cancellationToken));
        var corpusSha256 = corpusManifest.RootElement.GetProperty("corpusSha256").GetString()
            ?? throw new InvalidOperationException("The corpus manifest has no corpusSha256.");
        var corpusEntries = File.ReadLines(Path.Combine(corpusDirectory, "entries.jsonl"))
            .Select(ParseEntry)
            .Where(x => x.OwnerUserName == manifest.OwnerUserName)
            .ToDictionary(x => x.Id);
        var corpusSources = File.ReadLines(Path.Combine(corpusDirectory, "sources.jsonl"))
            .Select(ParseSource)
            .Where(x => x.OwnerUserName == manifest.OwnerUserName)
            .ToDictionary(x => x.Id);
        var errors = LlmWikiSemanticGraphValidator.Validate(manifest, corpusEntries, corpusSources, corpusSha256);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Semantic graph validation failed:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }

        using var scope = services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var currentEntries = await db.LlmWikiEntries.AsNoTracking()
            .Where(x => x.OwnerUserName == manifest.OwnerUserName)
            .Select(x => new LlmWikiSemanticCorpusEntry(
                x.Id, x.OwnerUserName, x.Title, x.Summary, x.CategoryPath, x.SourcePrompt, x.Content))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var currentSources = await db.LlmWikiEntrySources.AsNoTracking()
            .Where(x => x.OwnerUserName == manifest.OwnerUserName)
            .Select(x => new LlmWikiSemanticCorpusSource(x.Id, x.EntryId, x.OwnerUserName, x.Prompt, x.Content))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (!DictionaryEquals(corpusEntries, currentEntries) || !DictionaryEquals(corpusSources, currentSources))
        {
            throw new InvalidOperationException("The live LLM Wiki corpus drifted from the frozen semantic-analysis corpus.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        var exists = await VersionExistsAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM \"LlmWikiSemanticGraphVersions\" WHERE \"OwnerUserName\"=@owner AND \"Version\"=@version);",
            manifest.OwnerUserName, version, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException($"Semantic graph version '{version}' already exists for '{manifest.OwnerUserName}'.");
        }

        await ExecuteAsync(connection,
            """
            INSERT INTO "LlmWikiSemanticGraphVersions"
                ("OwnerUserName", "Version", "SchemaVersion", "CorpusSha256", "Generator", "GeneratorVersion", "State", "CreatedAt", "ActivatedAt")
            VALUES (@owner, @version, @schemaVersion, @corpusSha256, @generator, @generatorVersion, @state, @createdAt,
                    CASE WHEN @state='active' THEN @createdAt ELSE NULL END);
            """,
            [
                new("owner", manifest.OwnerUserName), new("version", version), new("schemaVersion", manifest.SchemaVersion),
                new("corpusSha256", manifest.CorpusSha256), new("generator", manifest.Generator),
                new("generatorVersion", manifest.GeneratorVersion), new("state", activate ? "active" : "validated"),
                new("createdAt", manifest.GeneratedAt)
            ], cancellationToken);

        foreach (var entity in manifest.Entities)
        {
            await ExecuteAsync(connection,
                "INSERT INTO \"LlmWikiSemanticEntities\" (\"OwnerUserName\",\"Version\",\"EntityKey\",\"CanonicalName\",\"EntityType\",\"Description\") VALUES (@owner,@version,@key,@name,@type,@description);",
                [new("owner", manifest.OwnerUserName), new("version", version), new("key", entity.Key), new("name", entity.CanonicalName), new("type", entity.EntityType), new("description", entity.Description)], cancellationToken);
        }
        foreach (var mention in manifest.Mentions)
        {
            await ExecuteAsync(connection,
                "INSERT INTO \"LlmWikiSemanticMentions\" (\"Id\",\"OwnerUserName\",\"Version\",\"EntityKey\",\"EntryId\",\"SourceId\",\"EvidenceField\",\"EvidenceQuote\",\"Confidence\") VALUES (@id,@owner,@version,@entity,@entry,@source,@field,@quote,@confidence);",
                [new("id", StableId($"mention|{manifest.OwnerUserName}|{version}|{mention.EntityKey}|{mention.EntryId}|{mention.SourceId}|{mention.EvidenceField}|{mention.EvidenceQuote}")), new("owner", manifest.OwnerUserName), new("version", version), new("entity", mention.EntityKey), new("entry", mention.EntryId), NullableUuid("source", mention.SourceId), new("field", mention.EvidenceField), new("quote", mention.EvidenceQuote), new("confidence", mention.Confidence)], cancellationToken);
        }
        foreach (var relation in manifest.Relations)
        {
            var relationId = StableId($"relation|{manifest.OwnerUserName}|{version}|{relation.FromEntityKey}|{relation.RelationType}|{relation.ToEntityKey}");
            await ExecuteAsync(connection,
                "INSERT INTO \"LlmWikiSemanticRelations\" (\"Id\",\"OwnerUserName\",\"Version\",\"FromEntityKey\",\"ToEntityKey\",\"RelationType\",\"Confidence\",\"State\") VALUES (@id,@owner,@version,@from,@to,@type,@confidence,@state);",
                [new("id", relationId), new("owner", manifest.OwnerUserName), new("version", version), new("from", relation.FromEntityKey), new("to", relation.ToEntityKey), new("type", relation.RelationType), new("confidence", relation.Confidence), new("state", activate ? "active" : "validated")], cancellationToken);
            for (var index = 0; index < relation.Evidence.Count; index++)
            {
                var evidence = relation.Evidence[index];
                await ExecuteAsync(connection,
                    "INSERT INTO \"LlmWikiSemanticRelationEvidence\" (\"Id\",\"RelationId\",\"EntryId\",\"SourceId\",\"EvidenceField\",\"EvidenceQuote\") VALUES (@id,@relation,@entry,@source,@field,@quote);",
                    [new("id", StableId($"evidence|{relationId}|{index}")), new("relation", relationId), new("entry", evidence.EntryId), NullableUuid("source", evidence.SourceId), new("field", evidence.EvidenceField), new("quote", evidence.EvidenceQuote)], cancellationToken);
            }
        }
        for (var index = 0; index < manifest.SplitProposals.Count; index++)
        {
            var split = manifest.SplitProposals[index];
            await ExecuteAsync(connection,
                "INSERT INTO \"LlmWikiMemorySplitProposals\" (\"Id\",\"OwnerUserName\",\"Version\",\"SourceEntryId\",\"CreatedEntryId\",\"ProposedTitle\",\"ProposedCategoryPath\",\"ProposedPrompt\",\"ProposedContent\",\"Reason\",\"EvidenceJson\",\"State\",\"CreatedAt\",\"ActivatedAt\") VALUES (@id,@owner,@version,@sourceEntry,NULL,@title,@category,@prompt,@content,@reason,CAST(@evidence AS jsonb),'validated',@createdAt,NULL);",
                [new("id", StableId($"split|{manifest.OwnerUserName}|{version}|{split.SourceEntryId}|{index}")), new("owner", manifest.OwnerUserName), new("version", version), new("sourceEntry", split.SourceEntryId), new("title", split.ProposedTitle), new("category", split.ProposedCategoryPath), new("prompt", split.ProposedPrompt), new("content", split.ProposedContent), new("reason", split.Reason), new("evidence", JsonSerializer.Serialize(split.Evidence, jsonOptions)), new("createdAt", manifest.GeneratedAt)], cancellationToken);
        }
        if (activate)
        {
            await ExecuteAsync(connection,
                "UPDATE \"LlmWikiSemanticGraphVersions\" SET \"State\"='retired', \"ActivatedAt\"=NULL WHERE \"OwnerUserName\"=@owner AND \"Version\"<>@version AND \"State\"='active';",
                [new("owner", manifest.OwnerUserName), new("version", version)], cancellationToken);
            await ExecuteAsync(connection,
                "UPDATE \"LlmWikiSemanticRelations\" SET \"State\"='rejected' WHERE \"OwnerUserName\"=@owner AND \"Version\"<>@version AND \"State\"='active';",
                [new("owner", manifest.OwnerUserName), new("version", version)], cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(manifest.OwnerUserName, version, manifest.Entities.Count, manifest.Mentions.Count, manifest.Relations.Count, manifest.SplitProposals.Count, activate);
    }

    private static bool DictionaryEquals<T>(IReadOnlyDictionary<Guid, T> expected, IReadOnlyDictionary<Guid, T> actual)
        where T : notnull
        => expected.Count == actual.Count && expected.All(pair => actual.TryGetValue(pair.Key, out var value) && pair.Value.Equals(value));

    private static Guid StableId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static NpgsqlParameter NullableUuid(string name, Guid? value)
        => new(name, NpgsqlDbType.Uuid) { Value = value ?? (object)DBNull.Value };

    private static async Task<bool> VersionExistsAsync(NpgsqlConnection connection, string sql, string owner, string version, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("version", version);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) { command.Parameters.Add(parameter); }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LlmWikiSemanticCorpusEntry ParseEntry(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        return new(root.GetProperty("id").GetGuid(), root.GetProperty("ownerUserName").GetString()!, root.GetProperty("title").GetString()!, root.GetProperty("summary").GetString()!, root.GetProperty("categoryPath").GetString()!, root.GetProperty("sourcePrompt").GetString()!, root.GetProperty("content").GetString()!);
    }

    private static LlmWikiSemanticCorpusSource ParseSource(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        return new(root.GetProperty("id").GetGuid(), root.GetProperty("entryId").GetGuid(), root.GetProperty("ownerUserName").GetString()!, root.GetProperty("prompt").GetString()!, root.GetProperty("content").ValueKind == JsonValueKind.Null ? null : root.GetProperty("content").GetString());
    }
}

public sealed record LlmWikiSemanticImportResult(string OwnerUserName, string Version, int EntityCount, int MentionCount, int RelationCount, int SplitProposalCount, bool Activated);
