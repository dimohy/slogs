using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slogs.Data;

public sealed class EmbeddingGemmaService(HttpClient httpClient, IConfiguration configuration)
{
    private const string DefaultEndpoint = "http://localhost:11434/api/embed";
    private const string DefaultModel = "embeddinggemma";
    private const int DefaultDimensions = 768;
    private const string DefaultKeepAlive = "30m";

    public string Model => DefaultModel;

    public int Dimensions => DefaultDimensions;

    private string KeepAlive => string.IsNullOrWhiteSpace(configuration["EmbeddingGemma:KeepAlive"])
        ? DefaultKeepAlive
        : configuration["EmbeddingGemma:KeepAlive"]!.Trim();

    public Task<IReadOnlyList<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken)
        => EmbedAsync($"task: search result | query: {query}", cancellationToken);

    public Task<IReadOnlyList<float>> EmbedDocumentAsync(string document, CancellationToken cancellationToken)
        => EmbedAsync(document, cancellationToken);

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
