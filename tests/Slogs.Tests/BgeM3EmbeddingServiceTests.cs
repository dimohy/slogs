using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BgeM3EmbeddingServiceTests
{
    [Fact]
    public void HttpClientTimeoutRequiresAnExplicitBoundedContract()
    {
        using var httpClient = new HttpClient();
        var missing = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var tooShort = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BgeM3:RequestTimeoutSeconds"] = "100"
        }).Build();
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BgeM3:RequestTimeoutSeconds"] = "600"
        }).Build();

        Assert.Throws<InvalidOperationException>(() => BgeM3EmbeddingService.ConfigureHttpClient(httpClient, missing));
        Assert.Throws<InvalidOperationException>(() => BgeM3EmbeddingService.ConfigureHttpClient(httpClient, tooShort));

        BgeM3EmbeddingService.ConfigureHttpClient(httpClient, configured);

        Assert.Equal(TimeSpan.FromMinutes(10), httpClient.Timeout);
    }

    [Fact]
    public async Task FullFunctionContractReturnsDenseAndThreeModePairScores()
    {
        var handler = new ContractHandler();
        using var httpClient = new HttpClient(handler);
        var service = new BgeM3EmbeddingService(
            httpClient,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BgeM3:BaseUrl"] = "http://bge.test",
                ["BgeM3:RerankMaxPassageTokens"] = "640"
            }).Build());

        await service.VerifyRuntimeAsync(CancellationToken.None);
        var embedding = await service.EmbedDocumentAsync("document", CancellationToken.None);
        var embeddings = await service.EmbedDocumentsAsync(["first", "second"], CancellationToken.None);
        var scores = await service.ScorePairsAsync("query", ["relevant", "irrelevant"], CancellationToken.None);

        Assert.True(service.SupportsFullFunctionReranking);
        Assert.Equal("bge-m3", service.Model);
        Assert.Equal(1024, embedding.Count);
        Assert.Equal(2, embeddings.Count);
        Assert.All(embeddings, value => Assert.Equal(1024, value.Count));
        Assert.Equal(2, scores.Count);
        Assert.Equal(0.91f, scores[0].Combined);
        Assert.Equal(0.22f, scores[1].Combined);
        Assert.All(scores, score => Assert.True(score.Dense >= 0 && score.Sparse >= 0 && score.MultiVector >= 0));
        Assert.Contains("\"max_passage_length\":640", handler.LastScoreRequest, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("255")]
    [InlineData("8193")]
    public async Task PairScoringRejectsMissingOrUnboundedPassageTokenContract(string? configuredTokens)
    {
        using var httpClient = new HttpClient(new ContractHandler());
        var settings = new Dictionary<string, string?> { ["BgeM3:BaseUrl"] = "http://bge.test" };
        if (configuredTokens is not null)
        {
            settings["BgeM3:RerankMaxPassageTokens"] = configuredTokens;
        }
        var service = new BgeM3EmbeddingService(
            httpClient,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ScorePairsAsync("query", ["passage"], CancellationToken.None));
    }

    private sealed class ContractHandler : HttpMessageHandler
    {
        public string LastScoreRequest { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var encodeCount = 1;
            if (request.RequestUri?.AbsolutePath == "/encode")
            {
                var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException("BGE-M3 encode request body is missing.");
                using var document = JsonDocument.Parse(body);
                encodeCount = document.RootElement.GetProperty("inputs").GetArrayLength();
            }
            if (request.RequestUri?.AbsolutePath == "/score")
            {
                LastScoreRequest = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult()
                    ?? string.Empty;
            }
            var json = request.RequestUri?.AbsolutePath switch
            {
                "/info" =>
                    """
                    {"modelId":"BAAI/bge-m3","modelRevision":"5617a9f61b028005a4858fdac845db406aefb181","dimensions":1024,"encodeBatchSize":1,"scoreBatchSize":8,"concurrentGpuRequests":1,"functions":["dense","sparse","multi-vector","pair-score"]}
                    """,
                "/encode" => $"{{\"dense\":[{string.Join(',', Enumerable.Repeat($"[{string.Join(',', Enumerable.Repeat("0.25", 1024))}]", encodeCount))}]}}",
                "/score" =>
                    """
                    {"dense":[0.8,0.2],"sparse":[0.7,0.1],"colbert":[0.9,0.3],"colbert+sparse+dense":[0.91,0.22]}
                    """,
                _ => throw new InvalidOperationException($"Unexpected BGE-M3 request: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
