using System.Text.Json;
using System.Reflection;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BgeM3OnlineRerankContractTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 5)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    public void CorpusCandidateWindowBoundsExpensiveOnlinePairScoring(int requestedLimit, int expected)
    {
        var method = typeof(KnowledgeCorpusService).GetMethod(
            "CalculateBgeM3CandidateLimit",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Corpus BGE-M3 candidate limit calculator is missing.");

        Assert.Equal(expected, (int)(method.Invoke(null, [requestedLimit])
            ?? throw new InvalidOperationException("Corpus BGE-M3 candidate limit calculator returned null.")));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(5, 2)]
    [InlineData(10, 4)]
    public void CorpusHybridCandidateWindowReservesBothRetrievalChannels(int candidateLimit, int expected)
    {
        var method = typeof(KnowledgeCorpusService).GetMethod(
            "CalculateHybridChannelQuota",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Corpus hybrid channel quota calculator is missing.");

        Assert.Equal(expected, (int)(method.Invoke(null, [candidateLimit])
            ?? throw new InvalidOperationException("Corpus hybrid channel quota calculator returned null.")));
    }

    [Theory]
    [InlineData("오순절 설교에서 베드로가 요엘서의 어느 말씀을 인용했나?", "설교", "베드로", "요엘")]
    [InlineData("선한 사마리아인 비유의 문맥상 핵심 요구는 무엇인가?", "사마리아", "비유", "요구")]
    public void CorpusLexicalQueryExpandsKoreanParticlesAndDerivedNouns(
        string query,
        string expectedFirst,
        string expectedSecond,
        string expectedThird)
    {
        var method = typeof(KnowledgeCorpusService).GetMethod(
            "BuildLexicalTsQuery",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Corpus lexical query builder is missing.");

        var result = (string)(method.Invoke(null, [query])
            ?? throw new InvalidOperationException("Corpus lexical query builder returned null."));

        Assert.Contains(expectedFirst, result, StringComparison.Ordinal);
        Assert.Contains(expectedSecond, result, StringComparison.Ordinal);
        Assert.Contains(expectedThird, result, StringComparison.Ordinal);
        Assert.DoesNotContain("어느", result, StringComparison.Ordinal);
        Assert.DoesNotContain("무엇인가", result, StringComparison.Ordinal);
        Assert.Contains(" | ", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("사도행전 13장 9절에서 사울과 바울을 어떻게 표현하는가", 13, 9)]
    [InlineData("사도행전 13:9을 두 번역으로 비교해줘", 13, 9)]
    public void CorpusQueryExtractsExplicitHierarchicalReference(
        string query,
        int expectedChapter,
        int expectedVerse)
    {
        var method = typeof(KnowledgeCorpusService).GetMethod(
            "TryExtractHierarchicalReference",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Corpus hierarchical reference parser is missing.");

        var result = method.Invoke(null, [query])
            ?? throw new InvalidOperationException("Corpus hierarchical reference parser returned null.");

        Assert.Equal(expectedChapter, result.GetType().GetProperty("Chapter")!.GetValue(result));
        Assert.Equal(expectedVerse, result.GetType().GetProperty("Verse")!.GetValue(result));
    }

    [Fact]
    public void CorpusQueryDoesNotInventHierarchicalReferenceWithoutChapterVerseSyntax()
    {
        var method = typeof(KnowledgeCorpusService).GetMethod(
            "TryExtractHierarchicalReference",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Corpus hierarchical reference parser is missing.");

        Assert.Null(method.Invoke(null, ["사도 바울과 사울의 관계"]));
    }

    [Fact]
    public void CorpusQueryExtractsCanonicalOsisLocatorAliases()
    {
        var method = typeof(KnowledgeCorpusService).GetMethod(
            "ExtractCanonicalLocatorAliases",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Corpus canonical locator extractor is missing.");

        var result = (string[])(method.Invoke(null, ["Acts.13.9와 1Sam.9.2를 비교해줘"])
            ?? throw new InvalidOperationException("Corpus canonical locator extractor returned null."));

        Assert.Equal(["passage:Acts.13.9", "passage:1Sam.9.2"], result);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 5)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    public void CandidateWindowBoundsExpensiveOnlinePairScoring(int requestedWindow, int expected)
    {
        var method = typeof(LlmWikiService).GetMethod(
            "CalculateBgeM3CandidateLimit",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BGE-M3 candidate limit calculator is missing.");

        Assert.Equal(expected, (int)(method.Invoke(null, [requestedWindow])
            ?? throw new InvalidOperationException("BGE-M3 candidate limit calculator returned null.")));
    }

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
