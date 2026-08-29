using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BibleKnowledgeCorpusAdapterTests
{
    [Fact]
    public void PlanPreservesBookChapterVerseEvidenceAndDeterministicBatches()
    {
        var adapter = new BibleKnowledgeCorpusAdapter(new KnowledgeChunkingService());
        var verses = new[]
        {
            Verse("Acts.9.1", 9, 1, "사울이 주의 제자들에 대하여 여전히 위협과 살기가 등등하여"),
            Verse("Acts.13.9", 13, 9, "바울이라고 하는 사울이 성령이 충만하여 그를 주목하고")
        };
        var entities = new[]
        {
            new BibleEntityCorpusInput("entity:paul", "person", "바울", ["Paul", "Saul", "사울"], null, ["G3972", "G4569"], "fixture")
        };
        var relations = new[]
        {
            new BibleRelationCorpusInput(
                "edge:acts-13-9:mentions-paul", "passage:Acts.13.9", "mentions", "entity:paul",
                "source_explicit", "approved", 1.0,
                [new BibleRelationEvidenceInput("fixture", "Acts.13.9", "source_verse", "Acts.13.9")],
                "fixture")
        };

        var first = adapter.CreatePlan(Options(), verses, entities, relations);
        var second = adapter.CreatePlan(Options(), verses, entities, relations);

        Assert.Equal(first.Collection, second.Collection);
        Assert.Equal(first.Batches.SelectMany(value => value.Chunks).Select(value => value.ChunkId),
            second.Batches.SelectMany(value => value.Chunks).Select(value => value.ChunkId));
        Assert.Contains(first.Batches.SelectMany(value => value.Documents), value => value.DocumentId == "document:book:Acts");
        Assert.Contains(first.Batches.SelectMany(value => value.StructureNodes), value =>
            value.NodeId == "passage:Acts.13.9" && value.ParentNodeId == "chapter:Acts.13");
        Assert.Equal(first.PassageChunkIds["Acts.13.9"],
            Assert.Single(first.Batches.SelectMany(value => value.Relations), value => value.RelationId == "edge:acts-13-9:mentions-paul")
                .Evidence.Single().ChunkIds!.Single());
        Assert.All(first.Batches.Take(first.Batches.Count - 1), value => Assert.False(value.Activate));
        Assert.True(first.Batches[^1].Activate);
        Assert.All(first.Batches, value =>
        {
            Assert.True(value.Documents.Count <= KnowledgeCorpusBatchLimits.Documents);
            Assert.True(value.StructureNodes.Count <= KnowledgeCorpusBatchLimits.StructureNodes);
            Assert.True(value.Chunks.Count <= KnowledgeCorpusBatchLimits.Chunks);
            Assert.True(value.Entities.Count <= KnowledgeCorpusBatchLimits.Entities);
            Assert.True(value.Relations.Count <= KnowledgeCorpusBatchLimits.Relations);
        });
    }

    [Fact]
    public void RestrictedTranslationFailsBeforePublicOrRedistributablePlan()
    {
        var adapter = new BibleKnowledgeCorpusAdapter(new KnowledgeChunkingService());

        var exception = Assert.Throws<InvalidDataException>(() => adapter.CreatePlan(
            Options() with { Visibility = "public_shared", RedistributionAllowed = true },
            [Verse("Acts.13.9", 13, 9, "바울이라고 하는 사울") ]));

        Assert.Contains("공개 재배포가 제한", exception.Message);
    }

    [Fact]
    public void CoordinateAndContentHashCorruptionFailBeforeChunking()
    {
        var adapter = new BibleKnowledgeCorpusAdapter(new KnowledgeChunkingService());
        var coordinate = Verse("Acts.13.9", 12, 9, "바울이라고 하는 사울");
        var corruptHash = Verse("Acts.13.9", 13, 9, "바울이라고 하는 사울") with { ContentHash = new string('0', 64) };

        Assert.Contains("좌표", Assert.Throws<InvalidDataException>(() =>
            adapter.CreatePlan(Options(), [coordinate])).Message);
        Assert.Contains("contentHash", Assert.Throws<InvalidDataException>(() =>
            adapter.CreatePlan(Options(), [corruptHash])).Message);
    }

    [Fact]
    public void FullVerifiedVersePackageProducesStrictPlansWhenExplicitlyEnabled()
    {
        var root = Environment.GetEnvironmentVariable("SLOGS_BIBLE_CORPUS_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var path = Path.Combine(root, "verses.ndjson");
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var verses = File.ReadLines(path)
            .Select((line, index) => JsonSerializer.Deserialize<BibleVerseCorpusInput>(line, jsonOptions)
                ?? throw new InvalidDataException($"verses.ndjson {index + 1}행을 읽을 수 없습니다."))
            .ToArray();
        var adapter = new BibleKnowledgeCorpusAdapter(new KnowledgeChunkingService());
        var plans = verses.GroupBy(value => value.TranslationId, StringComparer.Ordinal)
            .Select(group => adapter.CreatePlan(
                Options() with
                {
                    CollectionId = $"bible-{group.Key}",
                    Title = group.Key,
                    RequireContiguousVerses = true,
                    Chunking = new KnowledgeChunkingOptions(),
                    DeclaredOmissions = group.Key == "ko-nkrv"
                        ? [new BibleDeclaredOmission(
                            "ko-nkrv", "Acts.24.7",
                            "대한성서공회 개역개정 본문에서 6절 다음 8절로 이어지고 6절에 생략 표기가 있습니다.",
                            "https://www.bskorea.or.kr/bible/korbibReadpage.php?version=GAE&book=act&chap=24")]
                        : []
                },
                group.ToArray()))
            .ToArray();

        Assert.Equal(62_198, verses.Length);
        Assert.Equal(2, plans.Length);
        Assert.All(plans, plan =>
        {
            Assert.Equal(66, plan.Batches.SelectMany(value => value.Documents).Count());
            Assert.Equal(plan.Collection.ExpectedChunkCount, plan.Batches.SelectMany(value => value.Chunks).Count());
            Assert.Equal(plan.PassageChunkIds.Count,
                plan.Batches.SelectMany(value => value.Relations).Count(value => value.RelationType == "contains_passage"));
            Assert.True(plan.Batches[^1].Activate);
        });
    }

    private static BibleCorpusOptions Options() => new(
        "bible-ko-nkrv-fixture", "1.0.0", "개역개정 fixture", "restricted", "urn:test:bible",
        "user", "bible-owner", "private", null, false,
        Chunking: new KnowledgeChunkingOptions(TargetTokens: 80, MaxTokens: 120, MinTokens: 1, OverlapUnits: 0));

    internal static BibleVerseCorpusInput Verse(string reference, int chapter, int verse, string text)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return new BibleVerseCorpusInput(
            $"verse:ko-nkrv:{reference}", reference, "ko-nkrv", "ko", 44, "사도행전", chapter, verse,
            text, null, "fixture", "restricted_no_public_redistribution", hash);
    }
}
