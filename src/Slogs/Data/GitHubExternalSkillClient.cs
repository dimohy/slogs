using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Slogs.Data;

public sealed class GitHubExternalSkillClient(HttpClient httpClient)
{
    public async Task<string> GetLatestRevisionAsync(
        ExternalSkillSourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(source.RepositoryOwner)}/{Uri.EscapeDataString(source.RepositoryName)}/commits/{Uri.EscapeDataString(source.TrackingRef)}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"외부 스킬 원본의 최신 revision 확인에 실패했습니다: {(int)response.StatusCode} {source.SourceUrl}");
        }
        var payload = await response.Content.ReadFromJsonAsync<GitHubCommitResponse>(cancellationToken)
            ?? throw new InvalidOperationException("GitHub 최신 commit 응답이 비어 있습니다.");
        ExternalSkillSourceContract.ValidateRevision(payload.Sha);
        return payload.Sha.ToLowerInvariant();
    }

    public async Task<string> ReadFileAsync(
        ExternalSkillSourceDescriptor source,
        string revision,
        string path,
        CancellationToken cancellationToken = default)
    {
        ExternalSkillSourceContract.ValidateRevision(revision);
        var normalizedPath = ExternalSkillSourceContract.NormalizeRepositoryPath(path);
        var url = $"https://raw.githubusercontent.com/{Uri.EscapeDataString(source.RepositoryOwner)}/{Uri.EscapeDataString(source.RepositoryName)}/{revision}/{string.Join('/', normalizedPath.Split('/').Select(Uri.EscapeDataString))}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"외부 스킬 파일을 읽지 못했습니다: {(int)response.StatusCode} {normalizedPath}@{revision}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > ExternalSkillSourceContract.MaxEntrypointBytes)
        {
            throw new InvalidOperationException(
                $"외부 스킬 파일은 {ExternalSkillSourceContract.MaxEntrypointBytes:N0}바이트 이하여야 합니다: {normalizedPath}");
        }
        try
        {
            return new System.Text.UTF8Encoding(false, true).GetString(bytes);
        }
        catch (System.Text.DecoderFallbackException exception)
        {
            throw new InvalidOperationException($"외부 스킬 파일은 유효한 UTF-8 텍스트여야 합니다: {normalizedPath}", exception);
        }
    }

    private sealed record GitHubCommitResponse([property: JsonPropertyName("sha")] string Sha);
}
