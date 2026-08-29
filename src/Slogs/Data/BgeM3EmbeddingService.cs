using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slogs.Data;

public sealed class BgeM3EmbeddingService(HttpClient httpClient, IConfiguration configuration)
    : IKnowledgeEmbeddingService
{
    private const string RequestTimeoutSecondsKey = "BgeM3:RequestTimeoutSeconds";
    private const string DefaultBaseUrl = "http://localhost:8082";
    private const string RequiredModel = "BAAI/bge-m3";
    private const string RequiredRevision = "5617a9f61b028005a4858fdac845db406aefb181";

    public string Model => "bge-m3";
    public int Dimensions => 1024;
    public bool SupportsFullFunctionReranking => true;

    public static void ConfigureHttpClient(HttpClient client, IConfiguration configuration)
    {
        var configured = configuration[RequestTimeoutSecondsKey];
        if (!int.TryParse(configured, out var seconds) || seconds is < 120 or > 3600)
        {
            throw new InvalidOperationException(
                $"{RequestTimeoutSecondsKey} must be explicitly configured between 120 and 3600 seconds.");
        }
        client.Timeout = TimeSpan.FromSeconds(seconds);
    }

    public Task<IReadOnlyList<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken)
        => EmbedAsync(query, cancellationToken);

    public Task<IReadOnlyList<float>> EmbedDocumentAsync(string document, CancellationToken cancellationToken)
        => EmbedAsync(document, cancellationToken);

    public async Task<IReadOnlyList<IReadOnlyList<float>>> EmbedDocumentsAsync(
        IReadOnlyList<string> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(documents), "BGE-M3 encoding requires 1..256 documents.");
        }
        var result = await PostAsync(
            "encode",
            new BgeM3EncodeRequest(documents, true, false, false, 8192),
            BgeM3JsonSerializerContext.Default.BgeM3EncodeRequest,
            BgeM3JsonSerializerContext.Default.BgeM3EncodeResponse,
            cancellationToken);
        if (result.Dense.Count != documents.Count || result.Dense.Any(values => values.Count != Dimensions))
        {
            throw new InvalidOperationException(
                $"BGE-M3 response must contain {documents.Count} embeddings with exactly {Dimensions} dense values each.");
        }
        return result.Dense;
    }

    public async Task VerifyRuntimeAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(BuildUrl("info"), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"BGE-M3 runtime check failed with HTTP {(int)response.StatusCode}: {json}");
        }
        var info = JsonSerializer.Deserialize(json, BgeM3JsonSerializerContext.Default.BgeM3InfoResponse);
        var requiredFunctions = new[] { "dense", "sparse", "multi-vector", "pair-score" };
        if (info is null || info.ModelId != RequiredModel || info.ModelRevision != RequiredRevision ||
            info.Dimensions != Dimensions || info.MaxBatchSize != 8 || info.ConcurrentGpuRequests != 1 ||
            requiredFunctions.Except(info.Functions).Any())
        {
            throw new InvalidOperationException($"BGE-M3 runtime contract drift: {json}");
        }
    }

    public async Task<IReadOnlyList<KnowledgeRerankScore>> ScorePairsAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken)
    {
        if (passages.Count is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(passages), "BGE-M3 pair scoring requires 1..256 passages.");
        }
        var request = new BgeM3ScoreRequest(
            passages.Select(passage => new[] { query, passage }).ToArray(),
            [0.4f, 0.2f, 0.4f],
            512,
            8192);
        var result = await PostAsync(
            "score",
            request,
            BgeM3JsonSerializerContext.Default.BgeM3ScoreRequest,
            BgeM3JsonSerializerContext.Default.BgeM3ScoreResponse,
            cancellationToken);
        if (result.Dense.Count != passages.Count || result.Sparse.Count != passages.Count ||
            result.Colbert.Count != passages.Count || result.Combined.Count != passages.Count)
        {
            throw new InvalidOperationException("BGE-M3 pair score response count mismatch.");
        }
        return Enumerable.Range(0, passages.Count)
            .Select(index => new KnowledgeRerankScore(
                result.Dense[index], result.Sparse[index], result.Colbert[index], result.Combined[index]))
            .ToArray();
    }

    private async Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var result = await EmbedDocumentsAsync([text], cancellationToken);
        return result[0];
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> requestType,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, requestType);
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
        message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(message, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"BGE-M3 {path} failed with HTTP {(int)response.StatusCode}: {responseJson}");
        }
        return JsonSerializer.Deserialize(responseJson, responseType)
            ?? throw new InvalidOperationException($"BGE-M3 {path} returned an empty response.");
    }

    private string BuildUrl(string path)
    {
        var configured = configuration["BgeM3:BaseUrl"];
        var baseUrl = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.TrimEnd('/');
        return $"{baseUrl}/{path}";
    }
}

internal sealed record BgeM3EncodeRequest(
    IReadOnlyList<string> Inputs,
    [property: JsonPropertyName("return_dense")] bool ReturnDense,
    [property: JsonPropertyName("return_sparse")] bool ReturnSparse,
    [property: JsonPropertyName("return_multi_vector")] bool ReturnMultiVector,
    [property: JsonPropertyName("max_length")] int MaxLength);

internal sealed record BgeM3EncodeResponse(IReadOnlyList<IReadOnlyList<float>> Dense);
internal sealed record BgeM3ScoreRequest(
    IReadOnlyList<IReadOnlyList<string>> Pairs,
    IReadOnlyList<float> Weights,
    [property: JsonPropertyName("max_query_length")] int MaxQueryLength,
    [property: JsonPropertyName("max_passage_length")] int MaxPassageLength);
internal sealed record BgeM3ScoreResponse(
    IReadOnlyList<float> Dense,
    IReadOnlyList<float> Sparse,
    IReadOnlyList<float> Colbert,
    [property: JsonPropertyName("colbert+sparse+dense")] IReadOnlyList<float> Combined);
internal sealed record BgeM3InfoResponse(
    string ModelId,
    string ModelRevision,
    int Dimensions,
    int MaxBatchSize,
    int ConcurrentGpuRequests,
    IReadOnlyList<string> Functions);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(BgeM3EncodeRequest))]
[JsonSerializable(typeof(BgeM3EncodeResponse))]
[JsonSerializable(typeof(BgeM3ScoreRequest))]
[JsonSerializable(typeof(BgeM3ScoreResponse))]
[JsonSerializable(typeof(BgeM3InfoResponse))]
internal sealed partial class BgeM3JsonSerializerContext : JsonSerializerContext;
