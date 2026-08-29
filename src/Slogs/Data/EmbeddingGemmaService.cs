using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slogs.Data;

public sealed class EmbeddingGemmaService(HttpClient httpClient, IConfiguration configuration)
    : IKnowledgeEmbeddingService
{
    private const string DefaultEndpoint = "http://localhost:11434/api/embed";
    private const string DefaultModel = "embeddinggemma";
    private const int DefaultDimensions = 768;
    private const string DefaultKeepAlive = "30m";

    public string Model => DefaultModel;

    public int Dimensions => DefaultDimensions;

    public bool SupportsFullFunctionReranking => false;

    public Task<IReadOnlyList<KnowledgeRerankScore>> ScorePairsAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("EmbeddingGemma does not provide BGE-M3 full-function pair scoring.");

    private string KeepAlive => string.IsNullOrWhiteSpace(configuration["EmbeddingGemma:KeepAlive"])
        ? DefaultKeepAlive
        : configuration["EmbeddingGemma:KeepAlive"]!.Trim();

    public Task<IReadOnlyList<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken)
        => EmbedAsync($"task: search result | query: {query}", cancellationToken);

    public Task<IReadOnlyList<float>> EmbedDocumentAsync(string document, CancellationToken cancellationToken)
        => EmbedAsync(document, cancellationToken);

    public async Task<IReadOnlyList<IReadOnlyList<float>>> EmbedDocumentsAsync(
        IReadOnlyList<string> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count is < 1 or > KnowledgeCorpusBatchLimits.Chunks)
        {
            throw new ArgumentOutOfRangeException(nameof(documents),
                $"EmbeddingGemma corpus encoding requires 1..{KnowledgeCorpusBatchLimits.Chunks} documents.");
        }

        var results = new List<IReadOnlyList<float>>(documents.Count);
        foreach (var document in documents)
        {
            results.Add(await EmbedDocumentAsync(document, cancellationToken));
        }
        return results;
    }

    private async Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var endpoint = configuration["EmbeddingGemma:Endpoint"] ?? DefaultEndpoint;
        var request = new EmbeddingGemmaRequest(Model, text, KeepAlive);
        var requestJson = JsonSerializer.Serialize(
            request,
            EmbeddingGemmaJsonSerializerContext.Default.EmbeddingGemmaRequest);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"EmbeddingGemma local model request failed with HTTP {(int)response.StatusCode}: {responseJson}");
        }

        var result = JsonSerializer.Deserialize(
            responseJson,
            EmbeddingGemmaJsonSerializerContext.Default.EmbeddingGemmaResponse);
        var values = result?.Embeddings.FirstOrDefault();
        if (values is null || values.Count != Dimensions)
        {
            throw new InvalidOperationException(
                $"EmbeddingGemma local model response must contain exactly {Dimensions} values.");
        }

        return values;
    }
}

internal sealed record EmbeddingGemmaRequest(
    string Model,
    string Input,
    [property: JsonPropertyName("keep_alive")] string KeepAlive);

internal sealed record EmbeddingGemmaResponse(IReadOnlyList<IReadOnlyList<float>> Embeddings);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(EmbeddingGemmaRequest))]
[JsonSerializable(typeof(EmbeddingGemmaResponse))]
internal sealed partial class EmbeddingGemmaJsonSerializerContext : JsonSerializerContext;
