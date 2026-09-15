using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Slogs.Data;

public sealed class ExternalSkillRegistryService(
    IDbContextFactory<SlogsDbContext> dbFactory,
    GitHubExternalSkillClient sourceClient)
{
    public async Task<ExternalSkillResolution> RegisterGlobalAsync(
        string actor,
        string slug,
        string sourceUrl,
        string trackingRef,
        string entrypointPath,
        string description,
        string license,
        string searchAliasesJson,
        string decisionEvidence,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(actor, "dimohy", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("외부 원본 스킬 등록은 현재 @dimohy만 수행할 수 있습니다.");
        }
        if (string.IsNullOrWhiteSpace(decisionEvidence))
        {
            throw new InvalidOperationException("전역 적용에는 사용자의 명시적 선택 근거가 필요합니다.");
        }

        var descriptor = ExternalSkillSourceContract.Normalize(
            slug, sourceUrl, trackingRef, entrypointPath, description, license, searchAliasesJson);
        var revision = await sourceClient.GetLatestRevisionAsync(descriptor, cancellationToken);
        var content = await sourceClient.ReadFileAsync(descriptor, revision, descriptor.EntrypointPath, cancellationToken);
        var contentHash = ExternalSkillSourceContract.ValidateEntrypoint(descriptor.Slug, content);
        var now = DateTimeOffset.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                INSERT INTO "ExternalSkillSources"
                    ("Slug", "SourceUrl", "RepositoryOwner", "RepositoryName", "TrackingRef", "EntrypointPath",
                     "Description", "License", "SearchAliasesJson", "RegisteredBy", "CreatedAt", "UpdatedAt")
                VALUES
                    (@slug, @sourceUrl, @repositoryOwner, @repositoryName, @trackingRef, @entrypointPath,
                     @description, @license, CAST(@aliases AS jsonb), @actor, @now, @now)
                ON CONFLICT ("Slug") DO UPDATE SET
                    "SourceUrl" = EXCLUDED."SourceUrl",
                    "RepositoryOwner" = EXCLUDED."RepositoryOwner",
                    "RepositoryName" = EXCLUDED."RepositoryName",
                    "TrackingRef" = EXCLUDED."TrackingRef",
                    "EntrypointPath" = EXCLUDED."EntrypointPath",
                    "Description" = EXCLUDED."Description",
                    "License" = EXCLUDED."License",
                    "SearchAliasesJson" = EXCLUDED."SearchAliasesJson",
                    "RegisteredBy" = EXCLUDED."RegisteredBy",
                    "UpdatedAt" = EXCLUDED."UpdatedAt";
                """;
            AddParameter(command, "slug", descriptor.Slug);
            AddParameter(command, "sourceUrl", descriptor.SourceUrl);
            AddParameter(command, "repositoryOwner", descriptor.RepositoryOwner);
            AddParameter(command, "repositoryName", descriptor.RepositoryName);
            AddParameter(command, "trackingRef", descriptor.TrackingRef);
            AddParameter(command, "entrypointPath", descriptor.EntrypointPath);
            AddParameter(command, "description", descriptor.Description);
            AddParameter(command, "license", descriptor.License);
            AddParameter(command, "aliases", descriptor.SearchAliasesJson);
            AddParameter(command, "actor", actor.ToLowerInvariant());
            AddParameter(command, "now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delete = db.Database.GetDbConnection().CreateCommand())
        {
            delete.Transaction = transaction.GetDbTransaction();
            delete.CommandText = """
                DELETE FROM "SkillRegistrySelections"
                WHERE "OwnerUserName" = @owner AND "SkillSlug" = @slug AND "ProjectKey" IS NULL;
                """;
            AddParameter(delete, "owner", actor.ToLowerInvariant());
            AddParameter(delete, "slug", descriptor.Slug);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var insert = db.Database.GetDbConnection().CreateCommand())
        {
            insert.Transaction = transaction.GetDbTransaction();
            insert.CommandText = """
                INSERT INTO "SkillRegistrySelections"
                    ("Id", "OwnerUserName", "SkillSlug", "ScopeKind", "ProjectKey", "ChoicePrompted", "AutoUpdate",
                     "PinnedVersion", "DecisionEvidence", "CreatedAt", "UpdatedAt")
                VALUES (@id, @owner, @slug, 'global', NULL, TRUE, TRUE, NULL, @evidence, @now, @now);
                """;
            AddParameter(insert, "id", Guid.NewGuid());
            AddParameter(insert, "owner", actor.ToLowerInvariant());
            AddParameter(insert, "slug", descriptor.Slug);
            AddParameter(insert, "evidence", decisionEvidence.Trim());
            AddParameter(insert, "now", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(false, false, descriptor.Slug, "global", null, descriptor,
            new(revision, content, contentHash, "upstream-live", now),
            await ReadValidatedOverlayAsync(db, descriptor.Slug, cancellationToken));
    }

    public async Task<ExternalSkillResolution?> ResolveAsync(
        string owner,
        string skillSlug,
        string? projectKey,
        CancellationToken cancellationToken = default)
    {
        var slug = SkillRegistryContract.NormalizeSlug(skillSlug);
        var normalizedProject = string.IsNullOrWhiteSpace(projectKey)
            ? null
            : SkillRegistryContract.NormalizeProjectKey(projectKey);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        var source = await ReadSourceAsync(db, slug, cancellationToken);
        if (source is null)
        {
            return null;
        }
        var selection = await ReadSelectionAsync(db, owner, slug, normalizedProject, cancellationToken);
        if (selection is null)
        {
            return new(true, false, slug, null, normalizedProject, source.Descriptor, null, null);
        }
        if (!SkillRegistryContract.CanReleasePackage(selection))
        {
            return new(false, true, slug, "disabled", selection.ProjectKey, source.Descriptor, null, null);
        }

        var revision = await sourceClient.GetLatestRevisionAsync(source.Descriptor, cancellationToken);
        var content = await sourceClient.ReadFileAsync(
            source.Descriptor, revision, source.Descriptor.EntrypointPath, cancellationToken);
        var contentHash = ExternalSkillSourceContract.ValidateEntrypoint(slug, content);
        var checkedAt = DateTimeOffset.UtcNow;
        return new(false, false, slug, selection.ScopeKind, selection.ProjectKey, source.Descriptor,
            new(revision, content, contentHash, "upstream-live", checkedAt),
            await ReadValidatedOverlayAsync(db, slug, cancellationToken));
    }

    public async Task<string> ReadFileAsync(
        string skillSlug,
        string revision,
        string path,
        CancellationToken cancellationToken = default)
    {
        var slug = SkillRegistryContract.NormalizeSlug(skillSlug);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        var source = await ReadSourceAsync(db, slug, cancellationToken)
            ?? throw new InvalidOperationException("등록된 외부 스킬 원본을 찾을 수 없습니다.");
        return await sourceClient.ReadFileAsync(source.Descriptor, revision,
            ExternalSkillSourceContract.NormalizeRepositoryPath(path), cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalSkillSearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var terms = SkillRegistryContract.TokenizeSearchQuery(query);
        if (terms.Count == 0)
        {
            return [];
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT "Slug", "SourceUrl", "RepositoryOwner", "RepositoryName", "TrackingRef", "EntrypointPath",
                   "Description", "License", "SearchAliasesJson"::text
            FROM "ExternalSkillSources" ORDER BY "Slug";
            """;
        var results = new List<ExternalSkillSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var aliases = JsonSerializer.Deserialize<string[]>(reader.GetString(8)) ?? [];
            var searchable = $"{reader.GetString(0)} {reader.GetString(6)} {string.Join(' ', aliases)}".ToLowerInvariant();
            if (terms.All(term => searchable.Contains(term, StringComparison.Ordinal)))
            {
                results.Add(new(reader.GetString(0), reader.GetString(6), reader.GetString(1), reader.GetString(4), reader.GetString(5), reader.GetString(7)));
            }
        }
        return results.Take(Math.Clamp(limit, 1, 20)).ToArray();
    }

    private static async Task<ExternalSkillSource?> ReadSourceAsync(
        SlogsDbContext db,
        string slug,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT "Slug", "SourceUrl", "RepositoryOwner", "RepositoryName", "TrackingRef", "EntrypointPath",
                   "Description", "License", "SearchAliasesJson"::text, "RegisteredBy", "CreatedAt", "UpdatedAt"
            FROM "ExternalSkillSources" WHERE "Slug" = @slug;
            """;
        AddParameter(command, "slug", slug);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        var descriptor = new ExternalSkillSourceDescriptor(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8));
        return new(descriptor, reader.GetString(9),
            reader.GetFieldValue<DateTimeOffset>(10), reader.GetFieldValue<DateTimeOffset>(11));
    }

    private static async Task<RegisteredSkillVersion?> ReadValidatedOverlayAsync(
        SlogsDbContext db,
        string externalSlug,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT "Id", "Slug", "Version", "Description", "ContentHash", "PackageJson"::text,
                   "ValidationReportJson"::text, "ValidationReportHash", "EvaluationPayloadJson"::text,
                   "CandidateEvidenceJson"::text, "ReviewEvidenceJson"::text, "Status", "SubmittedBy",
                   "ValidatedBy", "CreatedAt"
            FROM "SkillRegistryVersions"
            WHERE "Slug" = @overlaySlug AND "Status" = 'validated'
            ORDER BY "VersionMajor" DESC, "VersionMinor" DESC, "VersionPatch" DESC
            LIMIT 1;
            """;
        AddParameter(command, "overlaySlug", $"{externalSlug}-slogs-overlay");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11), reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13), reader.GetFieldValue<DateTimeOffset>(14));
    }

    private static async Task<SkillSelection?> ReadSelectionAsync(
        SlogsDbContext db,
        string owner,
        string slug,
        string? projectKey,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT "SkillSlug", "ScopeKind", "ProjectKey", "ChoicePrompted", "AutoUpdate", "PinnedVersion", "DecisionEvidence", "UpdatedAt"
            FROM "SkillRegistrySelections"
            WHERE "OwnerUserName" = @owner AND "SkillSlug" = @slug
              AND ((CAST(@projectKey AS text) IS NOT NULL AND "ProjectKey" = CAST(@projectKey AS text))
                   OR ("ProjectKey" IS NULL AND "ScopeKind" IN ('global', 'disabled')))
            ORDER BY CASE WHEN "ProjectKey" = CAST(@projectKey AS text) THEN 0 ELSE 1 END, "UpdatedAt" DESC
            LIMIT 1;
            """;
        AddParameter(command, "owner", owner.ToLowerInvariant());
        AddParameter(command, "slug", slug);
        AddParameter(command, "projectKey", (object?)projectKey ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetBoolean(3), reader.GetBoolean(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7))
            : null;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
