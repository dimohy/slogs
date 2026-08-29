using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class KnowledgeChunkingServiceTests
{
    [Fact]
    public void CreateChunksUsesCanonicalLfBetweenUnits()
    {
        var chunks = new KnowledgeChunkingService().CreateChunks(
            "corpus",
            "1.0.0",
            "document",
            null,
            [
                new KnowledgeTextUnit("u1", "1", "first line"),
                new KnowledgeTextUnit("u2", "2", "second line")
            ],
            new KnowledgeChunkingOptions(TargetTokens: 20, MaxTokens: 30, MinTokens: 1, OverlapUnits: 0));

        Assert.Equal("first line\nsecond line", Assert.Single(chunks).Text);
        Assert.DoesNotContain('\r', chunks[0].Text);
    }

    [Fact]
    public void CreateChunksPreservesNaturalLocatorsNeighborsAndOverlap()
    {
        var service = new KnowledgeChunkingService();
        var units = new[]
        {
            new KnowledgeTextUnit("p1", "manual/1/p1", "센서의 기준값을 설정한다."),
            new KnowledgeTextUnit("p2", "manual/1/p2", "측정값이 기준값보다 크면 경고를 발생시킨다."),
            new KnowledgeTextUnit("p3", "manual/1/p3", "경고가 반복되면 배선을 점검한다."),
            new KnowledgeTextUnit("p4", "manual/1/p4", "배선이 정상이면 센서를 교체한다.")
        };

        var chunks = service.CreateChunks(
            "equipment-manual",
            "1.0.0",
            "manual-a",
            "section:diagnostics",
            units,
            new KnowledgeChunkingOptions(TargetTokens: 14, MaxTokens: 20, MinTokens: 3, OverlapUnits: 1));

        Assert.True(chunks.Count >= 2);
        Assert.Equal("manual/1/p1", chunks[0].StartLocator);
        Assert.NotNull(chunks[0].NextChunkId);
        Assert.Equal(chunks[0].NextChunkId, chunks[1].ChunkId);
        Assert.Equal(chunks[0].ChunkId, chunks[1].PreviousChunkId);
        Assert.Equal(1, chunks[1].OverlapUnits);
        Assert.NotEmpty(chunks[0].SearchAliases!.Intersect(chunks[1].SearchAliases!, StringComparer.Ordinal));
    }

    [Fact]
    public void StableChunkIdsDependOnSourceUnitsNotDisplayText()
    {
        var service = new KnowledgeChunkingService();
        var first = service.CreateChunks(
            "book",
            "v2",
            "chapter-1",
            null,
            [new KnowledgeTextUnit("u1", "1.1", "첫 번째 본문")],
            new KnowledgeChunkingOptions(TargetTokens: 20, MaxTokens: 30, MinTokens: 1));
        var edited = service.CreateChunks(
            "book",
            "v1",
            "chapter-1",
            null,
            [new KnowledgeTextUnit("u1", "1.1", "첫 번째 본문의 교정본")],
            new KnowledgeChunkingOptions(TargetTokens: 20, MaxTokens: 30, MinTokens: 1));

        Assert.Equal(first.Single().ChunkId, edited.Single().ChunkId);
        Assert.NotEqual(first.Single().Text, edited.Single().Text);
    }

    [Fact]
    public void OversizedNaturalUnitFailsBeforeEmbedding()
    {
        var service = new KnowledgeChunkingService();
        var oversized = string.Join(' ', Enumerable.Repeat("token", 30));

        var exception = Assert.Throws<InvalidDataException>(() => service.CreateChunks(
            "book",
            "v1",
            "chapter-1",
            null,
            [new KnowledgeTextUnit("u1", "1.1", oversized)],
            new KnowledgeChunkingOptions(TargetTokens: 10, MaxTokens: 20, MinTokens: 1)));

        Assert.Contains("도메인 어댑터가 먼저 분할", exception.Message);
    }

    [Fact]
    public void HardBoundaryStartsANewChunkEvenBelowTarget()
    {
        var service = new KnowledgeChunkingService();
        var chunks = service.CreateChunks(
            "book",
            "v1",
            "doc",
            null,
            [
                new KnowledgeTextUnit("a", "a", "첫 문단"),
                new KnowledgeTextUnit("b", "b", "새 장의 첫 문단", HardBoundary: true)
            ],
            new KnowledgeChunkingOptions(TargetTokens: 100, MaxTokens: 120, MinTokens: 1, OverlapUnits: 0));

        Assert.Equal(2, chunks.Count);
        Assert.Equal("a", chunks[0].StartLocator);
        Assert.Equal("b", chunks[1].StartLocator);
    }
}
