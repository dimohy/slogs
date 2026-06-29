using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Slogs.Data;

namespace Slogs.Obsidian.Drive;

internal sealed class SlogsObsidianRemoteClient(HttpClient httpClient, string token)
{
    public async Task<ObsidianVaultResponse> GetOrCreateVaultAsync(string vaultName, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/obsidian/vaults");
        request.Content = JsonContent.Create(
            new ObsidianVaultCreateRequest(vaultName),
            SlogsJsonSerializerContext.Default.ObsidianVaultCreateRequest);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadRequiredJsonAsync(
            response,
            SlogsJsonSerializerContext.Default.ObsidianVaultResponse,
            cancellationToken);
    }

    public async Task<ObsidianVaultFileListResponse> GetFilesAsync(
        Guid vaultId,
        long? sinceVersion,
        bool includeDeleted,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken = default)
    {
        var scopeQuery = scopes.Count == 0 ? string.Empty : $"&scopes={Uri.EscapeDataString(string.Join(',', scopes))}";
        var query = sinceVersion is null
            ? $"includeDeleted={includeDeleted.ToString().ToLowerInvariant()}"
            : $"sinceVersion={sinceVersion.Value}&includeDeleted={includeDeleted.ToString().ToLowerInvariant()}";
        query += scopeQuery;
        using var request = CreateRequest(HttpMethod.Get, $"api/obsidian/vaults/{vaultId:D}/files?{query}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadRequiredJsonAsync(
            response,
            SlogsJsonSerializerContext.Default.ObsidianVaultFileListResponse,
            cancellationToken);
    }

    public async Task<ObsidianVaultStatusResponse> GetVaultStatusAsync(
        Guid vaultId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/obsidian/vaults/{vaultId:D}/status");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadRequiredJsonAsync(
            response,
            SlogsJsonSerializerContext.Default.ObsidianVaultStatusResponse,
            cancellationToken);
    }

    public async Task<SlogsObsidianRemoteMutationResult> UpsertFileAsync(
        Guid vaultId,
        string path,
        string content,
        string mediaType,
        string scope,
        string kind,
        string encoding,
        long baseVersion,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Put, $"api/obsidian/vaults/{vaultId:D}/files");
        request.Content = JsonContent.Create(
            new ObsidianVaultFileUpsertRequest(path, content, baseVersion, mediaType, scope, kind, encoding),
            SlogsJsonSerializerContext.Default.ObsidianVaultFileUpsertRequest);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadMutationResultAsync(response, cancellationToken);
    }

    public async Task<SlogsObsidianRemoteMutationResult> DeleteFileAsync(
        Guid vaultId,
        string path,
        long baseVersion,
        string scope,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"api/obsidian/vaults/{vaultId:D}/files/delete");
        request.Content = JsonContent.Create(
            new ObsidianVaultFileDeleteRequest(path, baseVersion, scope),
            SlogsJsonSerializerContext.Default.ObsidianVaultFileDeleteRequest);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadMutationResultAsync(response, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<SlogsObsidianRemoteMutationResult> ReadMutationResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflict = await ReadJsonAsync(
                    response,
                    SlogsJsonSerializerContext.Default.ObsidianVaultConflictResponse,
                    cancellationToken)
                ?? throw new InvalidOperationException("Slogs API returned an empty conflict response.");
            return SlogsObsidianRemoteMutationResult.Conflict(conflict.RemoteFile);
        }

        var file = await ReadRequiredJsonAsync(
            response,
            SlogsJsonSerializerContext.Default.ObsidianVaultFileResponse,
            cancellationToken);
        return SlogsObsidianRemoteMutationResult.Updated(file);
    }

    private static async Task<T> ReadRequiredJsonAsync<T>(
        HttpResponseMessage response,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadApiErrorCodeAsync(response, cancellationToken);
            throw new InvalidOperationException(error ?? $"Slogs API returned HTTP {(int)response.StatusCode}.");
        }

        var value = await response.Content.ReadFromJsonAsync(jsonTypeInfo, cancellationToken);
        return value ?? throw new InvalidOperationException("Slogs API returned an empty response.");
    }

    private static async Task<T?> ReadJsonAsync<T>(
        HttpResponseMessage response,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
        => await response.Content.ReadFromJsonAsync(jsonTypeInfo, cancellationToken);

    private static async Task<string?> ReadApiErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync(
                SlogsJsonSerializerContext.Default.ApiErrorResponse,
                cancellationToken);
            return error?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record SlogsObsidianRemoteMutationResult(
    ObsidianVaultFileResponse? File,
    ObsidianVaultFileResponse? RemoteFile)
{
    public bool IsConflict => RemoteFile is not null;

    public static SlogsObsidianRemoteMutationResult Updated(ObsidianVaultFileResponse file)
        => new(file, null);

    public static SlogsObsidianRemoteMutationResult Conflict(ObsidianVaultFileResponse remoteFile)
        => new(null, remoteFile);
}
