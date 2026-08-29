using System.Text.Json;
using System.Reflection;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BgeM3OnlineRerankContractTests
{
    [Fact]
    public void RerankDocumentPrioritizesSummaryAndBoundsLongSourceText()
    {
        var entry = new LlmWikiEntryRecord
        {
            Title = "BGE 온라인 검색",
            Summary = "핵심 요약 근거",
            CategoryPath = "slogs/llm-wiki/performance",
            TagsJson = JsonSerializer.Serialize(new[] { "bge-m3", "rerank" }),
            SourcePrompt = new string('가', 5_000),
            Content = new string('나', 5_000)
        };

        var method = typeof(LlmWikiService).GetMethod(
            "BuildBgeM3RerankDocument",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BGE-M3 rerank document builder is missing.");
        var document = (string)(method.Invoke(null, [entry])
            ?? throw new InvalidOperationException("BGE-M3 rerank document builder returned null."));

        Assert.InRange(document.Length, 1, 6_000);
        Assert.Contains("title: BGE 온라인 검색", document, StringComparison.Ordinal);
        Assert.Contains("category: slogs/llm-wiki/performance", document, StringComparison.Ordinal);
        Assert.Contains("summary: 핵심 요약 근거", document, StringComparison.Ordinal);
        Assert.True(
            document.IndexOf("summary: 핵심 요약 근거", StringComparison.Ordinal)
            < document.IndexOf(new string('가', 100), StringComparison.Ordinal));
        Assert.DoesNotContain(new string('나', 1_000), document, StringComparison.Ordinal);
    }
}
