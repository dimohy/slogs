using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Slogs.Data;

public sealed class ObsidianVaultService(IDbContextFactory<SlogsDbContext> dbFactory)
{
    private const int MaxVaultNameLength = 120;
    private const int MaxPathLength = 700;
    private const int MaxMarkdownBytes = 2 * 1024 * 1024;
    private const int MaxSettingsBytes = 512 * 1024;
    private const int MaxAttachmentBytes = 25 * 1024 * 1024;
    private const int MaxMetadataJsonLength = 4_000;
    private const int MaxBatchSize = 500;
    private const int MaxClientTextLength = 120;
    private const string MarkdownMediaType = "text/markdown";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ObsidianVaultResponse>> GetVaultsAsync(
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.ObsidianVaults
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Name)
            .Select(x => new ObsidianVaultResponse(
                x.Id,
                x.Name,
                x.CurrentVersion,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ObsidianVaultResponse> GetOrCreateVaultAsync(
        string ownerUserName,
        ObsidianVaultCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var name = NormalizeVaultName(request.Name);
        var nameKey = NormalizeVaultNameKey(name);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.ObsidianVaults
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerUserName == owner && x.NameKey == nameKey, cancellationToken);
        if (existing is not null)
        {
            return ToVaultResponse(existing);
        }

        var now = DateTime.UtcNow;
        var vault = new ObsidianVaultRecord
        {
            Id = Guid.NewGuid(),
            OwnerUserName = owner,
            Name = name,
            NameKey = nameKey,
            CurrentVersion = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ObsidianVaults.Add(vault);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            var createdByRace = await db.ObsidianVaults
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OwnerUserName == owner && x.NameKey == nameKey, cancellationToken);
            if (createdByRace is null)
            {
                throw;
            }

            return ToVaultResponse(createdByRace);
        }

        return ToVaultResponse(vault);
    }

    public async Task<ObsidianVaultFileListResponse?> GetFilesAsync(
        string ownerUserName,
        Guid vaultId,
        long? sinceVersion,
        bool includeDeleted,
        int? limit = null,
        string? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var scopeFilter = ParseScopeFilter(scopes);
        var take = NormalizeLimit(limit, 0, 1000);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var vault = await db.ObsidianVaults
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == vaultId && x.OwnerUserName == owner, cancellationToken);
        if (vault is null)
        {
            return null;
        }

        var query = db.ObsidianVaultFiles
            .AsNoTracking()
            .Where(x => x.VaultId == vaultId && x.OwnerUserName == owner);

        if (sinceVersion is not null)
        {
            query = query.Where(x => x.Version > sinceVersion.Value);
        }

        if (!includeDeleted)
        {
            query = query.Where(x => !x.IsDeleted);
        }

        if (scopeFilter.Count > 0)
        {
            query = query.Where(x => scopeFilter.Contains(x.Scope));
        }

        query = query.OrderBy(x => x.Version).ThenBy(x => x.PathKey);
        var records = take == 0
            ? await query.ToListAsync(cancellationToken)
            : await query.Take(take + 1).ToListAsync(cancellationToken);
        var projected = records.Select(ToFileResponse).ToList();
        var hasMore = take > 0 && projected.Count > take;
        if (hasMore)
        {
            projected.RemoveAt(projected.Count - 1);
        }

        return new ObsidianVaultFileListResponse(
            vault.Id,
            vault.CurrentVersion,
            projected,
            hasMore,
            hasMore ? projected.LastOrDefault()?.Version : null);
    }

    public async Task<ObsidianVaultStatusResponse?> GetStatusAsync(
        string ownerUserName,
        Guid vaultId,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var vault = await db.ObsidianVaults
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == vaultId && x.OwnerUserName == owner, cancellationToken);
        if (vault is null)
        {
            return null;
        }

        var activeFileCount = await db.ObsidianVaultFiles
            .AsNoTracking()
            .CountAsync(x => x.OwnerUserName == owner && x.VaultId == vaultId && !x.IsDeleted, cancellationToken);
        var deletedFileCount = await db.ObsidianVaultFiles
            .AsNoTracking()
            .CountAsync(x => x.OwnerUserName == owner && x.VaultId == vaultId && x.IsDeleted, cancellationToken);
        var totalSizeBytes = await db.ObsidianVaultFiles
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner && x.VaultId == vaultId && !x.IsDeleted)
            .SumAsync(x => (long?)x.SizeBytes, cancellationToken) ?? 0;
        var quotaSnapshot = await ObsidianStorageQuotaService.GetSnapshotAsync(db, cancellationToken);
        var ownerUsedBytes = await ObsidianStorageQuotaService.GetOwnerUsedBytesAsync(db, owner, cancellationToken);
        var clients = await GetClientsCoreAsync(db, owner, vaultId, cancellationToken);

        return new ObsidianVaultStatusResponse(
            vault.Id,
            vault.Name,
            vault.CurrentVersion,
            activeFileCount,
            deletedFileCount,
            totalSizeBytes,
            quotaSnapshot.PerAccountStorageLimitBytes,
            ownerUsedBytes,
            Math.Max(0, quotaSnapshot.PerAccountStorageLimitBytes - ownerUsedBytes),
            clients);
    }

    public async Task<IReadOnlyList<ObsidianVaultClientResponse>?> GetClientsAsync(
        string ownerUserName,
        Guid vaultId,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var exists = await db.ObsidianVaults
            .AsNoTracking()
            .AnyAsync(x => x.Id == vaultId && x.OwnerUserName == owner, cancellationToken);
        return exists ? await GetClientsCoreAsync(db, owner, vaultId, cancellationToken) : null;
    }

    public async Task<ObsidianVaultClientResponse?> HeartbeatAsync(
        string ownerUserName,
        Guid vaultId,
        ObsidianVaultClientHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var clientId = NormalizeClientText(request.ClientId, nameof(request.ClientId));
        var clientName = NormalizeClientText(request.ClientName, nameof(request.ClientName));
        var clientKind = NormalizeClientText(request.ClientKind, nameof(request.ClientKind));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var vault = await GetVaultForOwnerAsync(db, owner, vaultId, cancellationToken);
        if (vault is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var client = await db.ObsidianVaultClients
            .FirstOrDefaultAsync(x => x.OwnerUserName == owner && x.VaultId == vaultId && x.ClientId == clientId, cancellationToken);
        if (client is null)
        {
            client = new ObsidianVaultClientRecord
            {
                VaultId = vaultId,
                OwnerUserName = owner,
                ClientId = clientId,
                CreatedAt = now
            };
            db.ObsidianVaultClients.Add(client);
        }

        client.ClientName = clientName;
        client.ClientKind = clientKind;
        client.LastSeenVersion = Math.Max(0, request.LastSeenVersion);
        client.LastSeenAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return ToClientResponse(client);
    }

    public async Task<ObsidianVaultFileMutationResult?> UpsertFileAsync(
        string ownerUserName,
        Guid vaultId,
        ObsidianVaultFileUpsertRequest request,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var vault = await GetVaultForOwnerAsync(db, owner, vaultId, cancellationToken);
        if (vault is null)
        {
            return null;
        }

        var result = await UpsertFileCoreAsync(db, vault, owner, request, clientId, 0, cancellationToken);
        if (result.IsConflict)
        {
            await transaction.RollbackAsync(cancellationToken);
            return result;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    public async Task<ObsidianVaultFileMutationResult?> DeleteFileAsync(
        string ownerUserName,
        Guid vaultId,
        ObsidianVaultFileDeleteRequest request,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var vault = await GetVaultForOwnerAsync(db, owner, vaultId, cancellationToken);
        if (vault is null)
        {
            return null;
        }

        var result = await DeleteFileCoreAsync(db, vault, owner, request, clientId, cancellationToken);
        if (result is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (result.IsConflict)
        {
            await transaction.RollbackAsync(cancellationToken);
            return result;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    public async Task<ObsidianVaultFileBatchMutationResponse?> UpsertFilesAsync(
        string ownerUserName,
        Guid vaultId,
        ObsidianVaultFileBatchUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Files.Count is < 1 or > MaxBatchSize)
        {
            throw new InvalidOperationException("obsidianBatchSizeInvalid");
        }

        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var vault = await GetVaultForOwnerAsync(db, owner, vaultId, cancellationToken);
        if (vault is null)
        {
            return null;
        }

        await TouchClientIfPresentAsync(db, vaultId, owner, request.ClientId, request.ClientName, request.ClientKind, vault.CurrentVersion, cancellationToken);

        var files = new List<ObsidianVaultFileResponse>();
        var conflicts = new List<ObsidianVaultConflictResponse>();
        var pendingActiveSizeDeltaBytes = 0L;
        foreach (var fileRequest in request.Files)
        {
            var result = await UpsertFileCoreAsync(
                db,
                vault,
                owner,
                fileRequest,
                request.ClientId,
                pendingActiveSizeDeltaBytes,
                cancellationToken);
            if (result.IsConflict)
            {
                conflicts.Add(new ObsidianVaultConflictResponse("obsidianConflict", result.RemoteFile!));
                continue;
            }

            pendingActiveSizeDeltaBytes += result.ActiveStorageDeltaBytes;
            files.Add(result.File!);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ObsidianVaultFileBatchMutationResponse(vault.Id, vault.CurrentVersion, files, conflicts);
    }

    public async Task<ObsidianVaultFileBatchMutationResponse?> DeleteFilesAsync(
        string ownerUserName,
        Guid vaultId,
        ObsidianVaultFileBatchDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Files.Count is < 1 or > MaxBatchSize)
        {
            throw new InvalidOperationException("obsidianBatchSizeInvalid");
        }

        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var vault = await GetVaultForOwnerAsync(db, owner, vaultId, cancellationToken);
        if (vault is null)
        {
            return null;
        }

        await TouchClientIfPresentAsync(db, vaultId, owner, request.ClientId, request.ClientName, request.ClientKind, vault.CurrentVersion, cancellationToken);

        var files = new List<ObsidianVaultFileResponse>();
        var conflicts = new List<ObsidianVaultConflictResponse>();
        foreach (var fileRequest in request.Files)
        {
            var result = await DeleteFileCoreAsync(db, vault, owner, fileRequest, request.ClientId, cancellationToken);
            if (result is null)
            {
                continue;
            }

            if (result.IsConflict)
            {
                conflicts.Add(new ObsidianVaultConflictResponse("obsidianConflict", result.RemoteFile!));
                continue;
            }

            files.Add(result.File!);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ObsidianVaultFileBatchMutationResponse(vault.Id, vault.CurrentVersion, files, conflicts);
    }

    public async Task<IReadOnlyList<ObsidianVaultFileVersionResponse>?> GetFileHistoryAsync(
        string ownerUserName,
        Guid vaultId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var normalizedPath = NormalizePath(path);
        var pathKey = NormalizePathKey(normalizedPath);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var vaultExists = await db.ObsidianVaults
            .AsNoTracking()
            .AnyAsync(x => x.Id == vaultId && x.OwnerUserName == owner, cancellationToken);
        if (!vaultExists)
        {
            return null;
        }

        return await db.ObsidianVaultFileVersions
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner && x.VaultId == vaultId && x.PathKey == pathKey)
            .OrderByDescending(x => x.Version)
            .Select(x => new ObsidianVaultFileVersionResponse(
                x.FileId,
                x.VaultId,
                x.Path,
                x.ContentHash,
                x.MediaType,
                x.SizeBytes,
                x.Version,
                x.IsDeleted,
                x.UpdatedAt,
                x.DeletedAt,
                x.Scope,
                x.Kind,
                x.Encoding,
                x.MetadataJson))
            .ToListAsync(cancellationToken);
    }

    public async Task<ObsidianVaultFileMutationResult?> RestoreFileAsync(
        string ownerUserName,
        Guid vaultId,
        ObsidianVaultFileRestoreRequest request,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var path = NormalizePath(request.Path);
        var pathKey = NormalizePathKey(path);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var vault = await GetVaultForOwnerAsync(db, owner, vaultId, cancellationToken);
        if (vault is null)
        {
            return null;
        }

        var existing = await db.ObsidianVaultFiles
            .FirstOrDefaultAsync(x => x.VaultId == vaultId && x.PathKey == pathKey, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (request.BaseVersion is not null && existing.Version != request.BaseVersion.Value)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ObsidianVaultFileMutationResult.Conflict(ToFileResponse(existing));
        }

        if (!existing.IsDeleted)
        {
            await transaction.CommitAsync(cancellationToken);
            return ObsidianVaultFileMutationResult.Updated(ToFileResponse(existing));
        }

        await ObsidianStorageQuotaService.AssertCanApplyActiveStorageDeltaAsync(
            db,
            owner,
            existing.SizeBytes,
            cancellationToken: cancellationToken);

        var now = DateTime.UtcNow;
        vault.CurrentVersion += 1;
        vault.UpdatedAt = now;
        existing.Version = vault.CurrentVersion;
        existing.IsDeleted = false;
        existing.UpdatedAt = now;
        existing.DeletedAt = null;
        existing.LastClientId = NormalizeOptionalClientId(clientId);
        db.ObsidianVaultFileVersions.Add(CreateVersionRecord(existing));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ObsidianVaultFileMutationResult.Updated(ToFileResponse(existing));
    }

    public async Task<ObsidianVaultFileResponse?> GetActiveMarkdownFileAsync(
        string ownerUserName,
        Guid vaultId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var normalizedPath = NormalizePath(path);
        var pathKey = NormalizePathKey(normalizedPath);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var file = await db.ObsidianVaultFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.OwnerUserName == owner
                && x.VaultId == vaultId
                && x.PathKey == pathKey
                && !x.IsDeleted
                && x.Scope == ObsidianSyncScopes.Markdown
                && x.Kind == ObsidianVaultFileKinds.Markdown,
                cancellationToken);
        return file is null ? null : ToFileResponse(file);
    }

    public async Task<ObsidianVaultPostMappingResponse?> MapToPostAsync(
        string ownerUserName,
        Guid vaultId,
        ObsidianVaultPostMappingRequest request,
        BlogService blogService,
        CancellationToken cancellationToken = default)
    {
        var file = await GetActiveMarkdownFileAsync(ownerUserName, vaultId, request.Path, cancellationToken);
        if (file is null)
        {
            return null;
        }

        var markdown = SplitFrontmatter(file.Content);
        var title = FirstNonEmpty(
            request.Title,
            GetFrontmatter(markdown.Frontmatter, "slogs.title", "title"),
            ExtractFirstHeading(markdown.Body),
            Path.GetFileNameWithoutExtension(file.Path),
            "Obsidian Note");
        var summary = FirstNonEmpty(
            request.Summary,
            GetFrontmatter(markdown.Frontmatter, "slogs.summary", "summary"),
            DeriveSummary(markdown.Body));
        var tags = FirstNonEmpty(request.Tags, GetFrontmatter(markdown.Frontmatter, "slogs.tags", "tags"), string.Empty);
        var series = FirstNonEmpty(request.Series, GetFrontmatter(markdown.Frontmatter, "slogs.series", "series"));
        var thumbnailUrl = FirstNonEmpty(
            request.ThumbnailUrl,
            GetFrontmatter(markdown.Frontmatter, "slogs.thumbnail", "thumbnail", "thumbnailUrl"));
        var slug = FirstNonEmpty(request.Slug, GetFrontmatter(markdown.Frontmatter, "slogs.slug", "slug"));
        var isDraft = request.IsDraft
            ?? (!GetFrontmatterBool(markdown.Frontmatter, defaultValue: false, "slogs.published", "published")
                || GetFrontmatterBool(markdown.Frontmatter, defaultValue: false, "slogs.draft", "draft"));

        BlogPost post;
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var existing = await blogService.GetBySlugAsync(slug, ownerUserName);
            post = existing is null
                ? await blogService.CreatePostAsync(title, ownerUserName, summary, markdown.Body, tags, series, thumbnailUrl, isDraft, slug)
                : await blogService.UpdatePostAsync(slug, ownerUserName, title, summary, markdown.Body, tags, series, thumbnailUrl, isDraft) ?? existing;
        }
        else
        {
            post = await blogService.CreatePostAsync(title, ownerUserName, summary, markdown.Body, tags, series, thumbnailUrl, isDraft);
        }

        return new ObsidianVaultPostMappingResponse(file.Path, post.Slug, post);
    }

    public async Task<ObsidianVaultLlmWikiMappingResponse?> MapToLlmWikiAsync(
        string ownerUserName,
        Guid vaultId,
        ObsidianVaultLlmWikiMappingRequest request,
        LlmWikiService llmWikiService,
        CancellationToken cancellationToken = default)
    {
        var file = await GetActiveMarkdownFileAsync(ownerUserName, vaultId, request.Path, cancellationToken);
        if (file is null)
        {
            return null;
        }

        var markdown = SplitFrontmatter(file.Content);
        var title = FirstNonEmpty(
            request.Title,
            GetFrontmatter(markdown.Frontmatter, "slogs.llmWiki.title", "llmWiki.title", "title"),
            ExtractFirstHeading(markdown.Body),
            Path.GetFileNameWithoutExtension(file.Path),
            "Obsidian Note");
        var tags = FirstNonEmpty(request.Tags, GetFrontmatter(markdown.Frontmatter, "slogs.llmWiki.tags", "tags"), "obsidian");
        var categoryPath = FirstNonEmpty(
            request.CategoryPath,
            GetFrontmatter(markdown.Frontmatter, "slogs.llmWiki.categoryPath", "categoryPath"),
            "slogs/obsidian-import");
        var prompt = $"Obsidian note import from {file.Path}. Preserve Raw Provenance by keeping this note body as imported content and this prompt as the source context.";
        var wikiRequest = new LlmWikiRememberRequest(prompt, markdown.Body, title, tags, categoryPath);
        var entry = string.IsNullOrWhiteSpace(request.EntryIdOrSlug)
            ? await llmWikiService.RememberAsync(ownerUserName, wikiRequest, cancellationToken)
            : await llmWikiService.UpdateAsync(
                ownerUserName,
                request.EntryIdOrSlug,
                new LlmWikiUpdateRequest(prompt, markdown.Body, title, tags, categoryPath),
                cancellationToken,
                "obsidian-import") ?? await llmWikiService.RememberAsync(ownerUserName, wikiRequest, cancellationToken);

        if (request.IsPublic == true || GetFrontmatterBool(markdown.Frontmatter, defaultValue: false, "slogs.llmWiki.public", "llmWiki.public"))
        {
            var published = await llmWikiService.PublishMatchingEntriesAsync(
                ownerUserName,
                $"Obsidian LLM Wiki mapping requested public visibility for {file.Path}.",
                entry.Title,
                limit: 3,
                categoryPath: entry.CategoryPath,
                cancellationToken: cancellationToken);
            entry = published.FirstOrDefault(x => x.Id == entry.Id) ?? entry;
        }

        return new ObsidianVaultLlmWikiMappingResponse(file.Path, entry.Id, entry.Slug, entry.CategoryPath, entry.IsPublic);
    }

    private async Task<ObsidianVaultFileMutationResult> UpsertFileCoreAsync(
        SlogsDbContext db,
        ObsidianVaultRecord vault,
        string owner,
        ObsidianVaultFileUpsertRequest request,
        string? clientId,
        long pendingActiveSizeDeltaBytes,
        CancellationToken cancellationToken)
    {
        var spec = NormalizeFileRequest(request);
        var existing = await db.ObsidianVaultFiles
            .FirstOrDefaultAsync(x => x.VaultId == vault.Id && x.PathKey == spec.PathKey, cancellationToken);

        if (existing is not null
            && request.BaseVersion is not null
            && existing.Version != request.BaseVersion.Value)
        {
            return ObsidianVaultFileMutationResult.Conflict(ToFileResponse(existing));
        }

        if (existing is not null
            && !existing.IsDeleted
            && existing.ContentHash.Equals(spec.ContentHash, StringComparison.Ordinal)
            && existing.Path.Equals(spec.Path, StringComparison.Ordinal)
            && existing.MediaType.Equals(spec.MediaType, StringComparison.Ordinal)
            && existing.Scope.Equals(spec.Scope, StringComparison.Ordinal)
            && existing.Kind.Equals(spec.Kind, StringComparison.Ordinal)
            && existing.Encoding.Equals(spec.Encoding, StringComparison.Ordinal)
            && existing.MetadataJson.Equals(spec.MetadataJson, StringComparison.Ordinal))
        {
            return ObsidianVaultFileMutationResult.Updated(ToFileResponse(existing));
        }

        var activeSizeDeltaBytes = spec.SizeBytes - (existing is not null && !existing.IsDeleted ? existing.SizeBytes : 0);
        await ObsidianStorageQuotaService.AssertCanApplyActiveStorageDeltaAsync(
            db,
            owner,
            activeSizeDeltaBytes,
            pendingOwnerDeltaBytes: pendingActiveSizeDeltaBytes,
            pendingTotalDeltaBytes: pendingActiveSizeDeltaBytes,
            cancellationToken);

        var now = DateTime.UtcNow;
        vault.CurrentVersion += 1;
        vault.UpdatedAt = now;

        if (existing is null)
        {
            existing = new ObsidianVaultFileRecord
            {
                Id = Guid.NewGuid(),
                VaultId = vault.Id,
                OwnerUserName = owner,
                CreatedAt = now
            };
            db.ObsidianVaultFiles.Add(existing);
        }

        existing.Path = spec.Path;
        existing.PathKey = spec.PathKey;
        existing.Content = spec.Content;
        existing.ContentHash = spec.ContentHash;
        existing.MediaType = spec.MediaType;
        existing.Scope = spec.Scope;
        existing.Kind = spec.Kind;
        existing.Encoding = spec.Encoding;
        existing.MetadataJson = spec.MetadataJson;
        existing.SizeBytes = spec.SizeBytes;
        existing.Version = vault.CurrentVersion;
        existing.IsDeleted = false;
        existing.UpdatedAt = now;
        existing.DeletedAt = null;
        existing.LastClientId = NormalizeOptionalClientId(clientId);
        db.ObsidianVaultFileVersions.Add(CreateVersionRecord(existing));

        return ObsidianVaultFileMutationResult.Updated(ToFileResponse(existing), activeSizeDeltaBytes);
    }

    private async Task<ObsidianVaultFileMutationResult?> DeleteFileCoreAsync(
        SlogsDbContext db,
        ObsidianVaultRecord vault,
        string owner,
        ObsidianVaultFileDeleteRequest request,
        string? clientId,
        CancellationToken cancellationToken)
    {
        var scope = NormalizeScope(request.Scope);
        var path = NormalizePath(request.Path);
        var pathKey = NormalizePathKey(path);
        ValidatePathScope(path, scope);
        var existing = await db.ObsidianVaultFiles
            .FirstOrDefaultAsync(x => x.VaultId == vault.Id && x.PathKey == pathKey, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (request.BaseVersion is not null && existing.Version != request.BaseVersion.Value)
        {
            return ObsidianVaultFileMutationResult.Conflict(ToFileResponse(existing));
        }

        if (existing.IsDeleted)
        {
            return ObsidianVaultFileMutationResult.Updated(ToFileResponse(existing));
        }

        var activeSizeDeltaBytes = -existing.SizeBytes;
        var now = DateTime.UtcNow;
        vault.CurrentVersion += 1;
        vault.UpdatedAt = now;

        existing.Version = vault.CurrentVersion;
        existing.IsDeleted = true;
        existing.UpdatedAt = now;
        existing.DeletedAt = now;
        existing.LastClientId = NormalizeOptionalClientId(clientId);
        db.ObsidianVaultFileVersions.Add(CreateVersionRecord(existing));

        return ObsidianVaultFileMutationResult.Updated(ToFileResponse(existing), activeSizeDeltaBytes);
    }

    private async Task TouchClientIfPresentAsync(
        SlogsDbContext db,
        Guid vaultId,
        string owner,
        string? clientId,
        string? clientName,
        string? clientKind,
        long lastSeenVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        var request = new ObsidianVaultClientHeartbeatRequest(
            clientId,
            string.IsNullOrWhiteSpace(clientName) ? "Obsidian client" : clientName,
            string.IsNullOrWhiteSpace(clientKind) ? "obsidian-plugin" : clientKind,
            lastSeenVersion);
        var normalizedClientId = NormalizeClientText(request.ClientId, nameof(request.ClientId));
        var now = DateTime.UtcNow;
        var client = await db.ObsidianVaultClients
            .FirstOrDefaultAsync(x => x.OwnerUserName == owner && x.VaultId == vaultId && x.ClientId == normalizedClientId, cancellationToken);
        if (client is null)
        {
            client = new ObsidianVaultClientRecord
            {
                VaultId = vaultId,
                OwnerUserName = owner,
                ClientId = normalizedClientId,
                CreatedAt = now
            };
            db.ObsidianVaultClients.Add(client);
        }

        client.ClientName = NormalizeClientText(request.ClientName, nameof(request.ClientName));
        client.ClientKind = NormalizeClientText(request.ClientKind, nameof(request.ClientKind));
        client.LastSeenVersion = Math.Max(0, request.LastSeenVersion);
        client.LastSeenAt = now;
    }

    private static async Task<ObsidianVaultRecord?> GetVaultForOwnerAsync(
        SlogsDbContext db,
        string owner,
        Guid vaultId,
        CancellationToken cancellationToken)
        => await db.ObsidianVaults
            .FirstOrDefaultAsync(x => x.Id == vaultId && x.OwnerUserName == owner, cancellationToken);

    private static async Task<IReadOnlyList<ObsidianVaultClientResponse>> GetClientsCoreAsync(
        SlogsDbContext db,
        string owner,
        Guid vaultId,
        CancellationToken cancellationToken)
        => await db.ObsidianVaultClients
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner && x.VaultId == vaultId)
            .OrderByDescending(x => x.LastSeenAt)
            .Select(x => new ObsidianVaultClientResponse(
                x.ClientId,
                x.VaultId,
                x.ClientName,
                x.ClientKind,
                x.LastSeenVersion,
                x.CreatedAt,
                x.LastSeenAt))
            .ToListAsync(cancellationToken);

    private static ObsidianVaultResponse ToVaultResponse(ObsidianVaultRecord vault)
        => new(vault.Id, vault.Name, vault.CurrentVersion, vault.CreatedAt, vault.UpdatedAt);

    private static ObsidianVaultFileResponse ToFileResponse(ObsidianVaultFileRecord file)
        => new(
            file.Id,
            file.VaultId,
            file.Path,
            file.Content,
            file.ContentHash,
            file.MediaType,
            file.SizeBytes,
            file.Version,
            file.IsDeleted,
            file.CreatedAt,
            file.UpdatedAt,
            file.DeletedAt,
            file.Scope,
            file.Kind,
            file.Encoding,
            file.MetadataJson,
            file.LastClientId);

    private static ObsidianVaultClientResponse ToClientResponse(ObsidianVaultClientRecord client)
        => new(
            client.ClientId,
            client.VaultId,
            client.ClientName,
            client.ClientKind,
            client.LastSeenVersion,
            client.CreatedAt,
            client.LastSeenAt);

    private static ObsidianVaultFileVersionRecord CreateVersionRecord(ObsidianVaultFileRecord file)
        => new()
        {
            Id = Guid.NewGuid(),
            FileId = file.Id,
            VaultId = file.VaultId,
            OwnerUserName = file.OwnerUserName,
            Path = file.Path,
            PathKey = file.PathKey,
            ContentHash = file.ContentHash,
            MediaType = file.MediaType,
            Scope = file.Scope,
            Kind = file.Kind,
            Encoding = file.Encoding,
            MetadataJson = file.MetadataJson,
            SizeBytes = file.SizeBytes,
            Version = file.Version,
            IsDeleted = file.IsDeleted,
            UpdatedAt = file.UpdatedAt,
            DeletedAt = file.DeletedAt
        };

    private static ObsidianFileContentSpec NormalizeFileRequest(ObsidianVaultFileUpsertRequest request)
    {
        var path = NormalizePath(request.Path);
        var scope = NormalizeScope(request.Scope, path);
        ValidatePathScope(path, scope);
        var kind = NormalizeKind(request.Kind, scope, path);
        var encoding = NormalizeEncoding(request.Encoding, scope);
        var content = request.Content ?? string.Empty;
        var contentBytes = GetContentBytes(content, encoding);
        var maxBytes = scope switch
        {
            ObsidianSyncScopes.Markdown => MaxMarkdownBytes,
            ObsidianSyncScopes.Settings => MaxSettingsBytes,
            ObsidianSyncScopes.Attachments => MaxAttachmentBytes,
            _ => MaxMarkdownBytes
        };
        if (contentBytes.LongLength > maxBytes)
        {
            throw new InvalidOperationException(scope switch
            {
                ObsidianSyncScopes.Attachments => "obsidianAttachmentTooLarge",
                ObsidianSyncScopes.Settings => "obsidianSettingsFileTooLarge",
                _ => "obsidianMarkdownFileTooLarge"
            });
        }

        var mediaType = NormalizeMediaType(request.MediaType, scope, path);
        var metadataJson = NormalizeMetadataJson(request.MetadataJson);
        return new ObsidianFileContentSpec(
            path,
            NormalizePathKey(path),
            content,
            ComputeSha256Hex(contentBytes),
            mediaType,
            scope,
            kind,
            encoding,
            metadataJson,
            contentBytes.LongLength);
    }

    private static string NormalizeVaultName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("obsidianVaultNameRequired");
        }

        if (normalized.Length > MaxVaultNameLength)
        {
            throw new InvalidOperationException("obsidianVaultNameTooLong");
        }

        if (normalized.Any(char.IsControl))
        {
            throw new InvalidOperationException("obsidianVaultNameInvalid");
        }

        return normalized;
    }

    private static string NormalizeVaultNameKey(string value)
        => NormalizeVaultName(value).ToLowerInvariant();

    private static string NormalizePath(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
        normalized = string.Join('/', normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("obsidianPathRequired");
        }

        if (normalized.Length > MaxPathLength)
        {
            throw new InvalidOperationException("obsidianPathTooLong");
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl)))
        {
            throw new InvalidOperationException("obsidianPathInvalid");
        }

        return normalized;
    }

    private static string NormalizePathKey(string path)
        => NormalizePath(path).ToLowerInvariant();

    private static string NormalizeScope(string? scope, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return path is not null && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? ObsidianSyncScopes.Markdown
                : ObsidianSyncScopes.Markdown;
        }

        var normalized = scope.Trim().ToLowerInvariant();
        if (!ObsidianSyncScopes.All.Contains(normalized, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("obsidianScopeInvalid");
        }

        return normalized;
    }

    private static HashSet<string> ParseScopeFilter(string? scopes)
    {
        if (string.IsNullOrWhiteSpace(scopes))
        {
            return [];
        }

        return scopes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => NormalizeScope(x))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void ValidatePathScope(string path, string scope)
    {
        var isSettingsPath = path.StartsWith(".obsidian/", StringComparison.OrdinalIgnoreCase);
        var isMarkdownPath = path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        if (isSettingsPath && scope != ObsidianSyncScopes.Settings)
        {
            throw new InvalidOperationException("obsidianSettingsScopeRequired");
        }

        if (!isSettingsPath && scope == ObsidianSyncScopes.Settings)
        {
            throw new InvalidOperationException("obsidianSettingsPathRequired");
        }

        if (scope == ObsidianSyncScopes.Markdown && !isMarkdownPath)
        {
            throw new InvalidOperationException("obsidianMarkdownPathRequired");
        }

        if (scope == ObsidianSyncScopes.Attachments && isMarkdownPath)
        {
            throw new InvalidOperationException("obsidianAttachmentPathRequired");
        }
    }

    private static string NormalizeKind(string? kind, string scope, string path)
    {
        var defaultKind = scope switch
        {
            ObsidianSyncScopes.Attachments => ObsidianVaultFileKinds.Attachment,
            ObsidianSyncScopes.Settings => ObsidianVaultFileKinds.Setting,
            _ => ObsidianVaultFileKinds.Markdown
        };
        var normalized = string.IsNullOrWhiteSpace(kind) ? defaultKind : kind.Trim().ToLowerInvariant();
        if (scope == ObsidianSyncScopes.Markdown && normalized != ObsidianVaultFileKinds.Markdown)
        {
            throw new InvalidOperationException("obsidianMarkdownKindRequired");
        }

        if (scope == ObsidianSyncScopes.Attachments && normalized != ObsidianVaultFileKinds.Attachment)
        {
            throw new InvalidOperationException("obsidianAttachmentKindRequired");
        }

        if (scope == ObsidianSyncScopes.Settings && normalized != ObsidianVaultFileKinds.Setting)
        {
            throw new InvalidOperationException("obsidianSettingsKindRequired");
        }

        _ = path;
        return normalized;
    }

    private static string NormalizeEncoding(string? encoding, string scope)
    {
        var normalized = string.IsNullOrWhiteSpace(encoding)
            ? (scope == ObsidianSyncScopes.Attachments ? ObsidianVaultContentEncodings.Base64 : ObsidianVaultContentEncodings.Utf8)
            : encoding.Trim().ToLowerInvariant();
        if (normalized is not ObsidianVaultContentEncodings.Utf8 and not ObsidianVaultContentEncodings.Base64)
        {
            throw new InvalidOperationException("obsidianEncodingInvalid");
        }

        if (scope == ObsidianSyncScopes.Attachments && normalized != ObsidianVaultContentEncodings.Base64)
        {
            throw new InvalidOperationException("obsidianAttachmentBase64Required");
        }

        if (scope != ObsidianSyncScopes.Attachments && normalized != ObsidianVaultContentEncodings.Utf8)
        {
            throw new InvalidOperationException("obsidianTextUtf8Required");
        }

        return normalized;
    }

    private static string NormalizeMediaType(string? mediaType, string scope, string path)
    {
        if (scope == ObsidianSyncScopes.Markdown)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                return MarkdownMediaType;
            }

            var normalizedMarkdown = mediaType.Trim().ToLowerInvariant();
            if (normalizedMarkdown is not MarkdownMediaType and not "text/plain")
            {
                throw new InvalidOperationException("obsidianMarkdownMediaTypeRequired");
            }

            return MarkdownMediaType;
        }

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var normalized = mediaType.Trim().ToLowerInvariant();
            if (normalized.Length > 120 || normalized.Any(char.IsControl))
            {
                throw new InvalidOperationException("obsidianMediaTypeInvalid");
            }

            return normalized;
        }

        if (scope == ObsidianSyncScopes.Settings)
        {
            return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "application/json" : "text/plain";
        }

        return "application/octet-stream";
    }

    private static string NormalizeMetadataJson(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return "{}";
        }

        var trimmed = metadataJson.Trim();
        if (trimmed.Length > MaxMetadataJsonLength)
        {
            throw new InvalidOperationException("obsidianMetadataTooLong");
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("obsidianMetadataObjectRequired");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("obsidianMetadataInvalid", ex);
        }

        return trimmed;
    }

    private static byte[] GetContentBytes(string content, string encoding)
    {
        if (encoding == ObsidianVaultContentEncodings.Utf8)
        {
            return Encoding.UTF8.GetBytes(content);
        }

        try
        {
            return Convert.FromBase64String(content);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("obsidianBase64Invalid", ex);
        }
    }

    private static string ComputeSha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string NormalizeUser(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static int NormalizeLimit(int? value, int defaultValue, int maxValue)
        => Math.Clamp(value.GetValueOrDefault(defaultValue), 0, maxValue);

    private static string NormalizeClientText(string value, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName}Required");
        }

        if (normalized.Length > MaxClientTextLength || normalized.Any(char.IsControl))
        {
            throw new InvalidOperationException($"{fieldName}Invalid");
        }

        return normalized;
    }

    private static string NormalizeOptionalClientId(string? clientId)
        => string.IsNullOrWhiteSpace(clientId)
            ? string.Empty
            : NormalizeClientText(clientId, "clientId");

    private static MarkdownFrontmatter SplitFrontmatter(string markdown)
    {
        var normalized = markdown ?? string.Empty;
        if (!normalized.StartsWith("---", StringComparison.Ordinal))
        {
            return new MarkdownFrontmatter(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), normalized);
        }

        using var reader = new StringReader(normalized);
        var firstLine = reader.ReadLine();
        if (firstLine is not "---")
        {
            return new MarkdownFrontmatter(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), normalized);
        }

        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim() == "---")
            {
                break;
            }

            var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(key))
            {
                frontmatter[key] = value;
            }
        }

        var body = reader.ReadToEnd();
        return new MarkdownFrontmatter(frontmatter, string.IsNullOrWhiteSpace(body) ? normalized : body.TrimStart());
    }

    private static string? GetFrontmatter(IReadOnlyDictionary<string, string> frontmatter, params string[] keys)
        => keys.Select(key => frontmatter.TryGetValue(key, out var value) ? value : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool GetFrontmatterBool(IReadOnlyDictionary<string, string> frontmatter, bool defaultValue, params string[] keys)
    {
        var value = GetFrontmatter(frontmatter, keys);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("1", StringComparison.Ordinal);
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string? ExtractFirstHeading(string markdown)
    {
        using var reader = new StringReader(markdown);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                return trimmed[2..].Trim();
            }
        }

        return null;
    }

    private static string DeriveSummary(string markdown)
    {
        var text = Regex.Replace(markdown, @"[#>*_`\[\]\(\)!-]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Obsidian note imported from Slogs sync.";
        }

        return text.Length > 140 ? text[..140] + "..." : text;
    }

    private sealed record ObsidianFileContentSpec(
        string Path,
        string PathKey,
        string Content,
        string ContentHash,
        string MediaType,
        string Scope,
        string Kind,
        string Encoding,
        string MetadataJson,
        long SizeBytes);

    private sealed record MarkdownFrontmatter(
        IReadOnlyDictionary<string, string> Frontmatter,
        string Body);
}

public sealed record ObsidianVaultFileMutationResult(
    ObsidianVaultFileResponse? File,
    ObsidianVaultFileResponse? RemoteFile,
    long ActiveStorageDeltaBytes = 0)
{
    public bool IsConflict => RemoteFile is not null;

    public static ObsidianVaultFileMutationResult Updated(ObsidianVaultFileResponse file, long activeStorageDeltaBytes = 0)
        => new(file, null, activeStorageDeltaBytes);

    public static ObsidianVaultFileMutationResult Conflict(ObsidianVaultFileResponse remoteFile)
        => new(null, remoteFile);
}
