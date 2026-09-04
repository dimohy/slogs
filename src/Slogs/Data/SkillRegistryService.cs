using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Slogs.Data;

public sealed class SkillRegistryService(IDbContextFactory<SlogsDbContext> dbFactory)
{
    public async Task<PreparedSkillPackage> PrepareAsync(
        string slug,
        string version,
        string description,
        string skillMarkdown,
        string license,
        string visibility,
        string provenanceJson,
        string verifiedPlatformsJson,
        string? supportingFilesJson,
        string? searchAliasesJson = null)
        => await Task.FromResult(SkillRegistryContract.Prepare(
            slug, version, description, skillMarkdown, license, visibility, provenanceJson, verifiedPlatformsJson,
            supportingFilesJson, searchAliasesJson));

    public async Task<RegisteredSkillVersion> SubmitCandidateAsync(
        string actor,
        string slug,
        string version,
        string description,
        string skillMarkdown,
        string license,
        string visibility,
        string provenanceJson,
        string verifiedPlatformsJson,
        string? supportingFilesJson,
        string candidateEvidenceJson,
        string validationReportJson,
        string evaluationPayloadJson,
        string expectedContentHash,
        CancellationToken cancellationToken = default)
        => await SubmitCandidateAsync(
            actor, slug, version, description, skillMarkdown, license, visibility, provenanceJson, verifiedPlatformsJson,
            supportingFilesJson, candidateEvidenceJson, validationReportJson, evaluationPayloadJson, expectedContentHash,
            searchAliasesJson: null, cancellationToken);

    public async Task<RegisteredSkillVersion> SubmitCandidateAsync(
        string actor,
        string slug,
        string version,
        string description,
        string skillMarkdown,
        string license,
        string visibility,
        string provenanceJson,
        string verifiedPlatformsJson,
        string? supportingFilesJson,
        string candidateEvidenceJson,
        string validationReportJson,
        string evaluationPayloadJson,
        string expectedContentHash,
        string? searchAliasesJson,
        CancellationToken cancellationToken = default)
    {
        var prepared = SkillRegistryContract.Prepare(
            slug, version, description, skillMarkdown, license, visibility, provenanceJson, verifiedPlatformsJson,
            supportingFilesJson, searchAliasesJson);
        SkillRegistryContract.ValidateCandidateEvidence(candidateEvidenceJson);
        var validation = SkillRegistryContract.ValidateEvidence(validationReportJson, evaluationPayloadJson);
        if (!string.Equals(prepared.ContentHash, expectedContentHash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Skill package content hash가 prepare 결과와 일치하지 않습니다.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await ReadVersionAsync(db, prepared.Payload.Name, prepared.Payload.Version, transaction.GetDbTransaction(), cancellationToken, validatedOnly: false);
        if (existing is not null)
        {
            if (!string.Equals(existing.ContentHash, prepared.ContentHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("같은 slug/version의 스킬은 변경할 수 없습니다. 새 버전을 등록하세요.");
            }

            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        var latest = await ReadLatestAsync(db, prepared.Payload.Name, transaction.GetDbTransaction(), cancellationToken);
        if (latest is not null && CompareVersion(prepared, latest.Version) <= 0)
        {
            throw new InvalidOperationException($"새 스킬 버전은 현재 최신 {latest.Version}보다 높아야 합니다.");
        }

        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO "SkillRegistryVersions"
                ("Id", "Slug", "Version", "VersionMajor", "VersionMinor", "VersionPatch",
                 "Description", "ContentHash", "PackageJson", "CandidateEvidenceJson", "ValidationReportJson", "ValidationReportHash",
                 "EvaluationPayloadJson", "ReviewEvidenceJson", "Status", "SubmittedBy", "ValidatedBy", "CreatedAt")
            VALUES
                (@id, @slug, @version, @major, @minor, @patch,
                 @description, @contentHash, CAST(@packageJson AS jsonb), CAST(@candidateEvidence AS jsonb), CAST(@validation AS jsonb), @validationHash,
                 CAST(@evaluationPayload AS jsonb), NULL, 'validated-candidate', @actor, NULL, @createdAt);
            """;
        AddParameter(command, "id", id);
        AddParameter(command, "slug", prepared.Payload.Name);
        AddParameter(command, "version", prepared.Payload.Version);
        AddParameter(command, "major", prepared.VersionMajor);
        AddParameter(command, "minor", prepared.VersionMinor);
        AddParameter(command, "patch", prepared.VersionPatch);
        AddParameter(command, "description", prepared.Payload.Description);
        AddParameter(command, "contentHash", prepared.ContentHash);
        AddParameter(command, "packageJson", prepared.PackageJson);
        AddParameter(command, "candidateEvidence", candidateEvidenceJson);
        AddParameter(command, "validation", validationReportJson);
        AddParameter(command, "validationHash", validation.OutputSha256.ToLowerInvariant());
        AddParameter(command, "evaluationPayload", evaluationPayloadJson);
        AddParameter(command, "actor", actor.ToLowerInvariant());
        AddParameter(command, "createdAt", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(id, prepared.Payload.Name, prepared.Payload.Version, prepared.Payload.Description,
            prepared.ContentHash, prepared.PackageJson, validationReportJson, validation.OutputSha256.ToLowerInvariant(),
            evaluationPayloadJson, candidateEvidenceJson, null, "validated-candidate", actor.ToLowerInvariant(), null, createdAt);
    }

    public async Task<RegisteredSkillVersion> ValidateCandidateAsync(
        string actor,
        Guid candidateId,
        string expectedContentHash,
        string expectedValidationReportHash,
        string reviewEvidenceJson,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(actor, "dimohy", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("공유 스킬 후보 승인은 현재 @dimohy만 수행할 수 있습니다.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var candidate = await ReadByIdAsync(db, candidateId, transaction.GetDbTransaction(), cancellationToken)
            ?? throw new InvalidOperationException("스킬 후보를 찾을 수 없습니다.");
        if (candidate.Status != "validated-candidate"
            || !string.Equals(candidate.ContentHash, expectedContentHash.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.ValidationReportHash, expectedValidationReportHash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("후보 상태 또는 검증 해시가 일치하지 않아 승인을 거부했습니다.");
        }
        SkillRegistryContract.ValidateEvidence(candidate.ValidationReportJson, candidate.EvaluationPayloadJson);
        SkillRegistryContract.ValidateReviewEvidence(
            reviewEvidenceJson, actor, candidate.ContentHash, candidate.ValidationReportHash);

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            UPDATE "SkillRegistryVersions"
            SET "Status" = 'validated', "ValidatedBy" = @actor, "ReviewEvidenceJson" = CAST(@reviewEvidence AS jsonb)
            WHERE "Id" = @id AND "Status" = 'validated-candidate';
            """;
        AddParameter(command, "actor", actor.ToLowerInvariant());
        AddParameter(command, "reviewEvidence", reviewEvidenceJson);
        AddParameter(command, "id", candidateId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("스킬 후보 상태가 동시에 변경되어 승인을 거부했습니다.");
        }
        await transaction.CommitAsync(cancellationToken);
        return candidate with { Status = "validated", ValidatedBy = actor.ToLowerInvariant(), ReviewEvidenceJson = reviewEvidenceJson };
    }

    public async Task<IReadOnlyList<RegisteredSkillVersion>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 20);
        var terms = SkillRegistryContract.TokenizeSearchQuery(query);
        if (terms.Count == 0)
        {
            return [];
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            WITH latest AS (
                SELECT DISTINCT ON ("Slug") *
                FROM "SkillRegistryVersions"
                WHERE "Status" = 'validated'
                ORDER BY "Slug", "VersionMajor" DESC, "VersionMinor" DESC, "VersionPatch" DESC
            ), matches AS (
                SELECT latest.*,
                    CASE
                        WHEN lower("Slug") = @normalizedQuery THEN 3
                        WHEN NOT EXISTS (
                            SELECT 1
                            FROM unnest(CAST(@terms AS text[])) AS requested("Term")
                            WHERE (' ' || regexp_replace(lower("Slug" || ' ' || "Description"), '[^[:alnum:]]+', ' ', 'g') || ' ')
                                  NOT LIKE '% ' || requested."Term" || ' %'
                        ) THEN 2
                        ELSE 1
                    END AS "MatchRank",
                    COALESCE((
                        SELECT MAX(cardinality(regexp_split_to_array(alias."Value", ' +')))
                        FROM jsonb_array_elements_text(COALESCE("PackageJson" -> 'searchAliases', '[]'::jsonb)) AS alias("Value")
                        WHERE NOT EXISTS (
                            SELECT 1
                            FROM unnest(regexp_split_to_array(alias."Value", ' +')) AS aliasTerm("Term")
                            WHERE aliasTerm."Term" <> ALL(CAST(@terms AS text[]))
                        )
                    ), 0) AS "AliasSpecificity"
                FROM latest
                WHERE lower("Slug") = @normalizedQuery
                   OR NOT EXISTS (
                       SELECT 1
                       FROM unnest(CAST(@terms AS text[])) AS requested("Term")
                       WHERE (' ' || regexp_replace(lower("Slug" || ' ' || "Description"), '[^[:alnum:]]+', ' ', 'g') || ' ')
                             NOT LIKE '% ' || requested."Term" || ' %'
                   )
                   OR EXISTS (
                       SELECT 1
                       FROM jsonb_array_elements_text(COALESCE("PackageJson" -> 'searchAliases', '[]'::jsonb)) AS alias("Value")
                       WHERE NOT EXISTS (
                           SELECT 1
                           FROM unnest(regexp_split_to_array(alias."Value", ' +')) AS aliasTerm("Term")
                           WHERE aliasTerm."Term" <> ALL(CAST(@terms AS text[]))
                       )
                   )
            )
            SELECT
                "Id", "Slug", "Version", "Description", "ContentHash", "PackageJson"::text,
                "ValidationReportJson"::text, "ValidationReportHash", "EvaluationPayloadJson"::text,
                "CandidateEvidenceJson"::text, "ReviewEvidenceJson"::text, "Status", "SubmittedBy", "ValidatedBy", "CreatedAt"
            FROM matches
            ORDER BY "MatchRank" DESC, "AliasSpecificity" DESC, "Slug"
            LIMIT @limit;
            """;
        AddParameter(command, "terms", terms.ToArray());
        AddParameter(command, "normalizedQuery", query.Trim().ToLowerInvariant());
        AddParameter(command, "limit", safeLimit);
        return await ReadManyAsync(command, cancellationToken);
    }

    public async Task<SkillSelection> ChooseAsync(
        string owner,
        string skillSlug,
        string choice,
        string? projectKey,
        bool choicePrompted,
        string decisionEvidence,
        bool autoUpdate,
        string? pinnedVersion,
        CancellationToken cancellationToken = default)
    {
        var slug = SkillRegistryContract.NormalizeSlug(skillSlug);
        var scope = SkillRegistryContract.NormalizeChoice(choice);
        if (!choicePrompted || string.IsNullOrWhiteSpace(decisionEvidence))
        {
            throw new InvalidOperationException("최초 사용 선택은 사용자에게 선택지를 제시한 근거가 필요합니다.");
        }
        var normalizedProject = scope == "project"
            ? SkillRegistryContract.NormalizeProjectKey(projectKey ?? string.Empty)
            : null;
        if (scope == "disabled" && !string.IsNullOrWhiteSpace(projectKey))
        {
            normalizedProject = SkillRegistryContract.NormalizeProjectKey(projectKey);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        var latest = await ReadLatestAsync(db, slug, null, cancellationToken)
            ?? throw new InvalidOperationException("등록된 스킬을 찾을 수 없습니다.");
        var selectedVersion = autoUpdate || scope == "disabled"
            ? null
            : string.IsNullOrWhiteSpace(pinnedVersion) ? latest.Version : pinnedVersion.Trim();
        if (selectedVersion is not null
            && await ReadVersionAsync(db, slug, selectedVersion, null, cancellationToken) is null)
        {
            throw new InvalidOperationException("고정하려는 검증 스킬 버전을 찾을 수 없습니다.");
        }
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using (var delete = db.Database.GetDbConnection().CreateCommand())
        {
            delete.Transaction = transaction.GetDbTransaction();
            delete.CommandText = normalizedProject is null
                ? "DELETE FROM \"SkillRegistrySelections\" WHERE \"OwnerUserName\" = @owner AND \"SkillSlug\" = @slug AND \"ProjectKey\" IS NULL;"
                : "DELETE FROM \"SkillRegistrySelections\" WHERE \"OwnerUserName\" = @owner AND \"SkillSlug\" = @slug AND \"ProjectKey\" = @projectKey;";
            AddParameter(delete, "owner", owner.ToLowerInvariant());
            AddParameter(delete, "slug", slug);
            if (normalizedProject is not null)
            {
                AddParameter(delete, "projectKey", normalizedProject);
            }
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO "SkillRegistrySelections"
                ("Id", "OwnerUserName", "SkillSlug", "ScopeKind", "ProjectKey", "ChoicePrompted", "AutoUpdate", "PinnedVersion", "DecisionEvidence", "CreatedAt", "UpdatedAt")
            VALUES (@id, @owner, @slug, @scope, @projectKey, TRUE, @autoUpdate, @pinnedVersion, @decisionEvidence, @now, @now);
            """;
        AddParameter(command, "id", Guid.NewGuid());
        AddParameter(command, "owner", owner.ToLowerInvariant());
        AddParameter(command, "slug", slug);
        AddParameter(command, "scope", scope);
        AddParameter(command, "projectKey", (object?)normalizedProject ?? DBNull.Value);
        AddParameter(command, "autoUpdate", autoUpdate);
        AddParameter(command, "pinnedVersion", (object?)selectedVersion ?? DBNull.Value);
        AddParameter(command, "decisionEvidence", decisionEvidence.Trim());
        AddParameter(command, "now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(slug, scope, normalizedProject, true, autoUpdate, selectedVersion, decisionEvidence.Trim(), now);
    }

    public async Task<SkillResolution> ResolveAsync(
        string owner,
        string skillSlug,
        string? projectKey,
        CancellationToken cancellationToken = default)
    {
        var slug = skillSlug.Trim().ToLowerInvariant();
        var normalizedProject = string.IsNullOrWhiteSpace(projectKey)
            ? null
            : SkillRegistryContract.NormalizeProjectKey(projectKey);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        var selection = await ReadSelectionAsync(db, owner, slug, normalizedProject, cancellationToken);
        if (selection is null)
        {
            return new(true, slug, null, normalizedProject, null, null);
        }
        if (!SkillRegistryContract.CanReleasePackage(selection))
        {
            return new(false, slug, "disabled", selection.ProjectKey, null, null);
        }

        var latest = await ReadLatestAsync(db, slug, null, cancellationToken)
            ?? throw new InvalidOperationException("등록된 스킬을 찾을 수 없습니다.");
        var package = selection.AutoUpdate
            ? latest
            : await ReadVersionAsync(db, slug, selection.PinnedVersion
                ?? throw new InvalidOperationException("고정 스킬 선택에 버전이 없습니다."), null, cancellationToken);
        return new(false, slug, selection.ScopeKind, selection.ProjectKey, latest.Version,
            package ?? throw new InvalidOperationException("선택된 스킬 버전을 찾을 수 없습니다."));
    }

    private static int CompareVersion(PreparedSkillPackage candidate, string existing)
    {
        var existingParts = existing.Split('.').Select(int.Parse).ToArray();
        return (candidate.VersionMajor, candidate.VersionMinor, candidate.VersionPatch)
            .CompareTo((existingParts[0], existingParts[1], existingParts[2]));
    }

    private static async Task<SkillSelection?> ReadSelectionAsync(
        SlogsDbContext db, string owner, string slug, string? projectKey, CancellationToken cancellationToken)
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

    private static async Task<RegisteredSkillVersion?> ReadLatestAsync(
        SlogsDbContext db, string slug, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT "Id", "Slug", "Version", "Description", "ContentHash", "PackageJson"::text,
                   "ValidationReportJson"::text, "ValidationReportHash", "EvaluationPayloadJson"::text,
                   "CandidateEvidenceJson"::text, "ReviewEvidenceJson"::text, "Status", "SubmittedBy", "ValidatedBy", "CreatedAt"
            FROM "SkillRegistryVersions" WHERE "Slug" = @slug AND "Status" = 'validated'
            ORDER BY "VersionMajor" DESC, "VersionMinor" DESC, "VersionPatch" DESC LIMIT 1;
            """;
        AddParameter(command, "slug", slug);
        return (await ReadManyAsync(command, cancellationToken)).SingleOrDefault();
    }

    private static async Task<RegisteredSkillVersion?> ReadVersionAsync(
        SlogsDbContext db, string slug, string version, DbTransaction? transaction, CancellationToken cancellationToken,
        bool validatedOnly = true)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = validatedOnly ? """
            SELECT "Id", "Slug", "Version", "Description", "ContentHash", "PackageJson"::text,
                   "ValidationReportJson"::text, "ValidationReportHash", "EvaluationPayloadJson"::text,
                   "CandidateEvidenceJson"::text, "ReviewEvidenceJson"::text, "Status", "SubmittedBy", "ValidatedBy", "CreatedAt"
            FROM "SkillRegistryVersions" WHERE "Slug" = @slug AND "Version" = @version AND "Status" = 'validated';
            """ : """
            SELECT "Id", "Slug", "Version", "Description", "ContentHash", "PackageJson"::text,
                   "ValidationReportJson"::text, "ValidationReportHash", "EvaluationPayloadJson"::text,
                   "CandidateEvidenceJson"::text, "ReviewEvidenceJson"::text, "Status", "SubmittedBy", "ValidatedBy", "CreatedAt"
            FROM "SkillRegistryVersions" WHERE "Slug" = @slug AND "Version" = @version;
            """;
        AddParameter(command, "slug", slug);
        AddParameter(command, "version", version);
        return (await ReadManyAsync(command, cancellationToken)).SingleOrDefault();
    }

    private static async Task<IReadOnlyList<RegisteredSkillVersion>> ReadManyAsync(
        DbCommand command, CancellationToken cancellationToken)
    {
        var results = new List<RegisteredSkillVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadCurrent(reader));
        }
        return results;
    }

    private static async Task<RegisteredSkillVersion?> ReadByIdAsync(
        SlogsDbContext db, Guid id, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT "Id", "Slug", "Version", "Description", "ContentHash", "PackageJson"::text,
                   "ValidationReportJson"::text, "ValidationReportHash", "EvaluationPayloadJson"::text,
                   "CandidateEvidenceJson"::text, "ReviewEvidenceJson"::text, "Status", "SubmittedBy", "ValidatedBy", "CreatedAt"
            FROM "SkillRegistryVersions" WHERE "Id" = @id FOR UPDATE;
            """;
        AddParameter(command, "id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCurrent(reader) : null;
    }

    private static RegisteredSkillVersion ReadCurrent(DbDataReader reader)
        => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
            reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11), reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13), reader.GetFieldValue<DateTimeOffset>(14));

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
