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

    private static KnowledgeChunkRecall Corpus(string chunkId, int relevance)
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
            []);
}
