using System.Security.Cryptography;
using System.Text;
using Slogs.Data;

namespace Slogs.Obsidian.Drive;

internal sealed class SlogsObsidianSyncService(
    SlogsObsidianRemoteClient remoteClient,
    SlogsObsidianDriveStateStore stateStore,
    string filesRoot,
    string vaultName,
    bool syncAttachments = false,
    bool syncSettings = false)
{
    private readonly SemaphoreSlim syncLock = new(1, 1);
    private SlogsObsidianDriveState state = new();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await syncLock.WaitAsync(cancellationToken);
        try
        {
            state = await stateStore.LoadAsync(cancellationToken);
            var vault = await remoteClient.GetOrCreateVaultAsync(vaultName, cancellationToken);
            EnsureCacheMatchesVault(vault);

            state.VaultId = vault.Id;
            state.VaultName = vault.Name;
            state.LastRemoteVersion = Math.Max(state.LastRemoteVersion, vault.CurrentVersion == 0 ? 0 : state.LastRemoteVersion);

            await PullRemoteChangesCoreAsync(cancellationToken);
            await PushLocalChangesCoreAsync(cancellationToken);
            await stateStore.SaveAsync(state, cancellationToken);
        }
        finally
        {
            syncLock.Release();
        }
    }

    public async Task RunRemotePollingAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);
                await PullRemoteChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Slogs remote pull failed: {ex.Message}");
            }
        }
    }

    public async Task PullRemoteChangesAsync(CancellationToken cancellationToken = default)
    {
        await syncLock.WaitAsync(cancellationToken);
        try
        {
            await PullRemoteChangesCoreAsync(cancellationToken);
            await stateStore.SaveAsync(state, cancellationToken);
        }
        finally
        {
            syncLock.Release();
        }
    }

    public async Task PushLocalChangesAsync(CancellationToken cancellationToken = default)
    {
        await syncLock.WaitAsync(cancellationToken);
        try
        {
            await PushLocalChangesCoreAsync(cancellationToken);
            await stateStore.SaveAsync(state, cancellationToken);
        }
        finally
        {
            syncLock.Release();
        }
    }

    public async Task FlushLocalFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var path = NormalizeRemotePath(relativePath);
        if (!IsSyncedPath(path))
        {
            return;
        }

        await syncLock.WaitAsync(cancellationToken);
        try
        {
            await PushSingleFileCoreAsync(path, cancellationToken);
            await stateStore.SaveAsync(state, cancellationToken);
        }
        finally
        {
            syncLock.Release();
        }
    }

    public async Task DeleteLocalPathAsync(string relativePath, bool isDirectory, CancellationToken cancellationToken = default)
    {
        await syncLock.WaitAsync(cancellationToken);
        try
        {
            if (isDirectory)
            {
                await PushLocalChangesCoreAsync(cancellationToken);
            }
            else
            {
                await DeleteSingleFileCoreAsync(NormalizeRemotePath(relativePath), cancellationToken);
            }

            await stateStore.SaveAsync(state, cancellationToken);
        }
        finally
        {
            syncLock.Release();
        }
    }

    public async Task RenameLocalPathAsync(CancellationToken cancellationToken = default)
    {
        await PushLocalChangesAsync(cancellationToken);
    }

    private async Task PullRemoteChangesCoreAsync(CancellationToken cancellationToken)
    {
        long? sinceVersion = state.LastRemoteVersion == 0 ? null : state.LastRemoteVersion;
        var response = await remoteClient.GetFilesAsync(
            state.VaultId,
            sinceVersion,
            includeDeleted: true,
            GetEnabledScopes(),
            cancellationToken);

        foreach (var remoteFile in response.Files.OrderBy(file => file.Version))
        {
            await ApplyRemoteFileAsync(remoteFile, cancellationToken);
            RememberRemoteFile(remoteFile);
        }

        state.LastRemoteVersion = Math.Max(state.LastRemoteVersion, response.CurrentVersion);
    }

    private async Task PushLocalChangesCoreAsync(CancellationToken cancellationToken)
    {
        var localPaths = EnumerateSyncedFiles()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in localPaths.Order(StringComparer.OrdinalIgnoreCase))
        {
            await PushSingleFileCoreAsync(path, cancellationToken);
        }

        foreach (var deletedPath in state.Files
            .Where(pair => !pair.Value.Deleted && !localPaths.Contains(pair.Key))
            .Select(pair => pair.Key)
            .ToArray())
        {
            await DeleteSingleFileCoreAsync(deletedPath, cancellationToken);
        }
    }

    private async Task PushSingleFileCoreAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsSyncedPath(path))
        {
            return;
        }

        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            await DeleteSingleFileCoreAsync(path, cancellationToken);
            return;
        }

        var content = await ReadApiContentAsync(fullPath, path, cancellationToken);
        var hash = await ComputeLocalHashAsync(fullPath, path, cancellationToken);
        state.Files.TryGetValue(path, out var fileState);
        if (fileState is not null && !fileState.Deleted && fileState.ContentHash == hash)
        {
            return;
        }

        var baseVersion = fileState?.Version ?? 0;
        var result = await remoteClient.UpsertFileAsync(
            state.VaultId,
            path,
            content,
            GuessMediaType(path),
            GetScope(path),
            GetKind(path),
            GetEncoding(path),
            baseVersion,
            cancellationToken);
        if (result.IsConflict)
        {
            throw SlogsObsidianSyncConflictException.ForRemoteFile(path, result.RemoteFile!);
        }

        RememberRemoteFile(result.File!);
    }

    private async Task DeleteSingleFileCoreAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsSyncedPath(path))
        {
            return;
        }

        if (!state.Files.TryGetValue(path, out var fileState) || fileState.Deleted)
        {
            return;
        }

        var result = await remoteClient.DeleteFileAsync(
            state.VaultId,
            path,
            fileState.Version,
            fileState.Scope,
            cancellationToken);
        if (result.IsConflict)
        {
            throw SlogsObsidianSyncConflictException.ForRemoteFile(path, result.RemoteFile!);
        }

        RememberRemoteFile(result.File!);
    }

    private async Task ApplyRemoteFileAsync(ObsidianVaultFileResponse remoteFile, CancellationToken cancellationToken)
    {
        var path = NormalizeRemotePath(remoteFile.Path);
        if (!IsSyncedPath(path))
        {
            return;
        }

        state.Files.TryGetValue(path, out var fileState);
        var fullPath = GetFullPath(path);
        var localExists = File.Exists(fullPath);

        if (remoteFile.IsDeleted)
        {
            if (localExists && await IsLocalDirtyAsync(path, fileState, cancellationToken))
            {
                throw SlogsObsidianSyncConflictException.ForRemoteFile(path, remoteFile);
            }

            if (localExists)
            {
                File.Delete(fullPath);
            }

            return;
        }

        if (localExists)
        {
            var localHash = await ComputeLocalHashAsync(fullPath, path, cancellationToken);
            if (localHash != remoteFile.ContentHash
                && (fileState is null || fileState.ContentHash != localHash || fileState.Version != remoteFile.Version))
            {
                throw SlogsObsidianSyncConflictException.ForRemoteFile(path, remoteFile);
            }

            if (localHash == remoteFile.ContentHash)
            {
                return;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Invalid remote path: {path}"));
        await WriteApiContentAsync(fullPath, remoteFile, cancellationToken);
    }

    private async Task<bool> IsLocalDirtyAsync(
        string path,
        SlogsObsidianDriveFileState? fileState,
        CancellationToken cancellationToken)
    {
        if (fileState is null || fileState.Deleted)
        {
            return File.Exists(GetFullPath(path));
        }

        return await ComputeLocalHashAsync(GetFullPath(path), path, cancellationToken) != fileState.ContentHash;
    }

    private IEnumerable<string> EnumerateSyncedFiles()
    {
        if (!Directory.Exists(filesRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories))
        {
            var path = ToRemotePath(file);
            if (IsSyncedPath(path))
            {
                yield return path;
            }
        }
    }

    private void RememberRemoteFile(ObsidianVaultFileResponse remoteFile)
    {
        var path = NormalizeRemotePath(remoteFile.Path);
        state.Files[path] = new SlogsObsidianDriveFileState
        {
            Version = remoteFile.Version,
            ContentHash = remoteFile.ContentHash,
            Deleted = remoteFile.IsDeleted,
            Scope = remoteFile.Scope,
            Kind = remoteFile.Kind
        };
        state.LastRemoteVersion = Math.Max(state.LastRemoteVersion, remoteFile.Version);
    }

    private void EnsureCacheMatchesVault(ObsidianVaultResponse vault)
    {
        if (state.VaultId != Guid.Empty && state.VaultId != vault.Id)
        {
            throw new InvalidOperationException(
                $"The cache belongs to a different Slogs vault ({state.VaultId}). Use another --cache path or clear the existing cache deliberately.");
        }
    }

    private string GetFullPath(string remotePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(filesRoot, remotePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(filesRoot);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid remote path escapes cache root: {remotePath}");
        }

        return fullPath;
    }

    private string ToRemotePath(string fullPath)
        => Path.GetRelativePath(filesRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private IReadOnlyList<string> GetEnabledScopes()
    {
        var scopes = new List<string> { ObsidianSyncScopes.Markdown };
        if (syncAttachments)
        {
            scopes.Add(ObsidianSyncScopes.Attachments);
        }

        if (syncSettings)
        {
            scopes.Add(ObsidianSyncScopes.Settings);
        }

        return scopes;
    }

    private bool IsSyncedPath(string path)
    {
        var normalized = NormalizeRemotePath(path);
        return IsSyncedMarkdownPath(normalized)
            || (syncSettings && IsSettingsPath(normalized))
            || (syncAttachments && IsAttachmentPath(normalized));
    }

    public static bool IsSyncedMarkdownPath(string path)
    {
        var normalized = NormalizeRemotePath(path);
        return normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith(".obsidian/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSettingsPath(string path)
        => NormalizeRemotePath(path).StartsWith(".obsidian/", StringComparison.OrdinalIgnoreCase);

    private static bool IsAttachmentPath(string path)
    {
        var normalized = NormalizeRemotePath(path);
        return !string.IsNullOrWhiteSpace(normalized)
            && !IsSyncedMarkdownPath(normalized)
            && !IsSettingsPath(normalized);
    }

    private static string GetScope(string path)
    {
        var normalized = NormalizeRemotePath(path);
        if (IsSyncedMarkdownPath(normalized))
        {
            return ObsidianSyncScopes.Markdown;
        }

        return IsSettingsPath(normalized) ? ObsidianSyncScopes.Settings : ObsidianSyncScopes.Attachments;
    }

    private static string GetKind(string path)
        => GetScope(path) switch
        {
            ObsidianSyncScopes.Attachments => ObsidianVaultFileKinds.Attachment,
            ObsidianSyncScopes.Settings => ObsidianVaultFileKinds.Setting,
            _ => ObsidianVaultFileKinds.Markdown
        };

    private static string GetEncoding(string path)
        => GetScope(path) == ObsidianSyncScopes.Attachments
            ? ObsidianVaultContentEncodings.Base64
            : ObsidianVaultContentEncodings.Utf8;

    private static string GuessMediaType(string path)
    {
        var normalized = NormalizeRemotePath(path).ToLowerInvariant();
        if (normalized.EndsWith(".md", StringComparison.Ordinal))
        {
            return "text/markdown";
        }

        if (normalized.EndsWith(".json", StringComparison.Ordinal))
        {
            return "application/json";
        }

        if (normalized.EndsWith(".css", StringComparison.Ordinal))
        {
            return "text/css";
        }

        if (normalized.EndsWith(".png", StringComparison.Ordinal))
        {
            return "image/png";
        }

        if (normalized.EndsWith(".jpg", StringComparison.Ordinal) || normalized.EndsWith(".jpeg", StringComparison.Ordinal))
        {
            return "image/jpeg";
        }

        if (normalized.EndsWith(".gif", StringComparison.Ordinal))
        {
            return "image/gif";
        }

        if (normalized.EndsWith(".webp", StringComparison.Ordinal))
        {
            return "image/webp";
        }

        if (normalized.EndsWith(".svg", StringComparison.Ordinal))
        {
            return "image/svg+xml";
        }

        if (normalized.EndsWith(".pdf", StringComparison.Ordinal))
        {
            return "application/pdf";
        }

        return "application/octet-stream";
    }

    private static async Task<string> ReadApiContentAsync(string fullPath, string remotePath, CancellationToken cancellationToken)
    {
        if (GetEncoding(remotePath) == ObsidianVaultContentEncodings.Base64)
        {
            return Convert.ToBase64String(await ReadAllBytesSharedAsync(fullPath, cancellationToken));
        }

        return await ReadAllTextSharedAsync(fullPath, cancellationToken);
    }

    private static async Task WriteApiContentAsync(string fullPath, ObsidianVaultFileResponse remoteFile, CancellationToken cancellationToken)
    {
        if (remoteFile.Encoding == ObsidianVaultContentEncodings.Base64)
        {
            await File.WriteAllBytesAsync(fullPath, Convert.FromBase64String(remoteFile.Content), cancellationToken);
            return;
        }

        await File.WriteAllTextAsync(fullPath, remoteFile.Content, Encoding.UTF8, cancellationToken);
    }

    private static async Task<string> ComputeLocalHashAsync(string fullPath, string remotePath, CancellationToken cancellationToken)
    {
        var bytes = GetEncoding(remotePath) == ObsidianVaultContentEncodings.Base64
            ? await ReadAllBytesSharedAsync(fullPath, cancellationToken)
            : Encoding.UTF8.GetBytes(await ReadAllTextSharedAsync(fullPath, cancellationToken));
        return ComputeSha256Hex(bytes);
    }

    private static async Task<byte[]> ReadAllBytesSharedAsync(string fullPath, CancellationToken cancellationToken)
    {
        await using var stream = OpenSharedRead(fullPath);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private static async Task<string> ReadAllTextSharedAsync(string fullPath, CancellationToken cancellationToken)
    {
        await using var stream = OpenSharedRead(fullPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static FileStream OpenSharedRead(string fullPath)
        => new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    public static string NormalizeRemotePath(string path)
        => string.Join(
            '/',
            path.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string ComputeSha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed class SlogsObsidianSyncConflictException(string message) : InvalidOperationException(message)
{
    public static SlogsObsidianSyncConflictException ForRemoteFile(string path, ObsidianVaultFileResponse remoteFile)
        => new($"Slogs Obsidian conflict at '{path}'. Remote version {remoteFile.Version} was not overwritten.");
}
