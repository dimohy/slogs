using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class LlmWikiCombinedRecallTests
{
    [Fact]
    public void CombinedRecallAppliesOneGlobalLimitAcrossMemoryAndCorpus()
    {
        var memories = new[]
        {
            Memory("unrelated-memory", 43),
            Memory("strong-memory", 70)
        };
        var corpus = new[]
        {
            Corpus("Acts.13.1", 54),
            Corpus("Acts.13.9", 53)
        };

        var selected = LlmWikiMcpTools.SelectCombinedRecallCandidates(memories, corpus, 2);

        Assert.Collection(
            selected,
            candidate =>
            {
                Assert.False(candidate.IsCorpus);
                Assert.Equal(1, candidate.SourceIndex);
                Assert.Equal(70, candidate.RelevancePercent);
            },
            candidate =>
            {
                Assert.True(candidate.IsCorpus);
                Assert.Equal(0, candidate.SourceIndex);
                Assert.Equal(54, candidate.RelevancePercent);
            });
    }

    [Fact]
    public void ExactCorpusEvidenceDisplacesLowerRelevancePersonalMemory()
    {
        var selected = LlmWikiMcpTools.SelectCombinedRecallCandidates(
            [Memory("unrelated-memory", 43)],
            [Corpus("Acts.13.9-nkrv", 54), Corpus("Acts.13.9-tkv", 53)],
            2);

        Assert.Equal(2, selected.Count);
        Assert.All(selected, candidate => Assert.True(candidate.IsCorpus));
        Assert.Equal([0, 1], selected.Select(candidate => candidate.SourceIndex));
    }

    [Fact]
    public void EqualRelevancePreservesPersonalMemoryCompatibilityBeforeCorpus()
    {
        var selected = LlmWikiMcpTools.SelectCombinedRecallCandidates(
            [Memory("memory", 60)],
            [Corpus("corpus", 60)],
            1);

        var candidate = Assert.Single(selected);
        Assert.False(candidate.IsCorpus);
    }

    [Fact]
    public void RelationalRecallKeepsSubstantiveGraphEvidenceInsideTheResponseLimit()
    {
        var selected = LlmWikiMcpTools.SelectCombinedRecallCandidates(
            [],
            [
                Corpus("semantic-1", 70),
                Corpus("semantic-2", 69),
                Corpus("semantic-3", 68),
                Corpus("semantic-4", 67),
                Corpus("semantic-5", 66),
                Corpus("reviewed-relation", 50, "direct_quote")
            ],
            5,
            preferSubstantiveGraphRelations: true);

        Assert.Contains(selected, candidate => candidate.SourceIndex == 5 && candidate.HasSubstantiveGraphRelation);
        Assert.Equal(5, selected.Count);
    }

    [Fact]
    public void CombinedRerankingReservesFourCorpusCandidatesWhenReviewedRelationEvidenceExists()
    {
        var memories = Enumerable.Range(1, 6)
            .Select(index => Memory($"memory-{index}", 100 - index))
            .ToArray();
        var corpus = new[]
        {
            Corpus("ordinary-1", 80),
            Corpus("reviewed-relation", 50, "direct_quote"),
            Corpus("ordinary-2", 70),
            Corpus("ordinary-3", 60),
            Corpus("ordinary-4", 40)
        };

        var selected = LlmWikiMcpTools.SelectCombinedRerankCandidates(memories, corpus);

        Assert.Equal(5, selected.Count);
        Assert.Single(selected, candidate => !candidate.IsCorpus);
        Assert.Equal(4, selected.Count(candidate => candidate.IsCorpus));
        Assert.Contains(selected, candidate => candidate.IsCorpus && candidate.SourceIndex == 1);
    }

    [Fact]
    public void CombinedRerankingFillsUnusedSourceQuotaWithoutExceedingFiveCandidates()
    {
        var selected = LlmWikiMcpTools.SelectCombinedRerankCandidates(
            [],
            Enumerable.Range(1, 10).Select(index => Corpus($"corpus-{index}", 50 - index)).ToArray());

        Assert.Equal(5, selected.Count);
        Assert.All(selected, candidate => Assert.True(candidate.IsCorpus));
    }

    [Fact]
    public void ReviewedSubstantiveRelationCannotBeDemotedBelowItsHybridRetrievalScore()
    {
        var relation = new LlmWikiMcpTools.CombinedRecallCandidate(
            IsCorpus: true,
            SourceIndex: 0,
            RelevancePercent: 50,
            HasSubstantiveGraphRelation: true);
        var ordinary = relation with { HasSubstantiveGraphRelation = false };

        Assert.Equal(50, LlmWikiMcpTools.CalculateCombinedRerankRelevance(relation, 0.1f));
        Assert.Equal(18, LlmWikiMcpTools.CalculateCombinedRerankRelevance(ordinary, 0.1f));
    }

    [Fact]
    public void ReviewedSubstantiveRelationSurvivesTheSemanticThresholdGate()
    {
        var reviewed = Corpus("reviewed-relation", 20, "direct_quote");
        var ordinary = Corpus("ordinary", 20);

        Assert.True(LlmWikiMcpTools.ShouldKeepRerankedCorpusCandidate(reviewed, 45));
        Assert.False(LlmWikiMcpTools.ShouldKeepRerankedCorpusCandidate(ordinary, 45));
    }

    [Theory]
    [InlineData("Acts.13.9 본문", true)]
    [InlineData("사도행전 13장 9절 본문", true)]
    [InlineData("사도행전 13:9 본문", true)]
    [InlineData("Acts.13.9와 1Sam.9.2 비교", false)]
    [InlineData("사도행전 13장 9절과 사무엘상 9장 2절 비교", false)]
    [InlineData("사울과 바울의 관계", false)]
    public void SingleExplicitLocatorDetectionProtectsMultiLocatorAndSemanticQueries(
        string query,
        bool expected)
    {
        Assert.Equal(expected, KnowledgeCorpusService.HasSingleExplicitLocatorQuery(query));
    }

    [Fact]
    public void LocatorCoordinateExtractionKeepsEveryExplicitReferenceForCrossCollectionExpansion()
    {
        Assert.Equal(
            [".9.19", ".9.26"],
            KnowledgeCorpusService.ExtractLocatorCoordinateSuffixes("사도행전 9장 19절과 26절"));
        Assert.Equal(
            [".4.3", ".3.6", ".15.6"],
            KnowledgeCorpusService.ExtractLocatorCoordinateSuffixes("Rom.4.3 Gal.3.6 Gen.15.6"));
    }

    [Theory]
    [InlineData(5, 1, 5)]
    [InlineData(3, 2, 10)]
    [InlineData(5, 3, 10)]
    public void RelationalRecallSearchesADeepCandidatePoolWithoutExpandingTheResponseLimit(
        int responseLimit,
        int maxGraphHops,
        int expectedCorpusLimit)
    {
        Assert.Equal(expectedCorpusLimit, LlmWikiMcpTools.CalculateCorpusRecallLimit(responseLimit, maxGraphHops));
    }

    private static LlmWikiSearchResult Memory(string title, int relevance)
        => new(
            Guid.NewGuid(),
            title,
            title,
            title,
            [],
            "test/combined-recall",
            2,
            DateTime.UtcNow,
            0,
            false,
            null,
            relevance);

    private static KnowledgeChunkRecall Corpus(string chunkId, int relevance, string? relationType = null)
        => new(
            "test-corpus",
            "1.0.0",
            "test",
            "document:test",
            "Test document",
            chunkId,
            "evidence",
            chunkId,
            chunkId,
            relevance,
            relationType is null
                ? []
                : [new KnowledgeRelationRecall(
                    "test-corpus",
                    "1.0.0",
                    relationType,
                    "passage:from",
                    "passage:to",
                    "source_asserted",
                    1,
                    [])]);
}
