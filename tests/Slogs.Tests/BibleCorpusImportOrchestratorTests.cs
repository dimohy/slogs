using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BibleCorpusImportOrchestratorTests
{
    [Fact]
    public async Task FullVerifiedPackageProducesThreeDeterministicDeploymentLayers()
    {
        var root = Environment.GetEnvironmentVariable("SLOGS_BIBLE_CORPUS_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var chunker = new KnowledgeChunkingService();
        var orchestrator = new BibleCorpusImportOrchestrator(
            new BibleCorpusPackageReader(),
            new BibleKnowledgeCorpusAdapter(chunker),
            new BibleOriginalKnowledgeCorpusAdapter(chunker),
            new BibleCorpusImportRunner(null!));
        var result = await orchestrator.RunAsync(new BibleCorpusImportOptions(
            root,
            Path.Combine(Path.GetTempPath(), "unused-bible-checkpoints"),
            "dimohy",
            "all",
            VerifyOnly: true));

        Assert.Equal("F0D5A93AC9E53701DB80186D3E269D46882E120AAB350CB542AA7409DF84C429", result.PackageHash);
        Assert.True(result.VerifyOnly);
        Assert.Collection(
            result.Layers,
            layer => AssertLayer(layer, "bible-ko-nkrv", "private", 85, 66, 1_693, 0, 31_101,
                "EB10C98E6EBF3E4D0A8107A0F3E8D67A0534AE81B1EE4FEED07BE0A25DEBF47D"),
            layer => AssertLayer(layer, "bible-ko-tkv", "private", 111, 66, 2_203, 0, 31_097,
                "767DF60B8804BB48821629FDFDBC039BC284C6930D90444F3E91D39D129AD7D2"),
            layer => AssertLayer(layer, "bible-original-step", "public_shared", 2_426, 66, 48_515, 4_259, 461_355,
                "90FBF073C6A0DC7E677279B8A6694A559D5C310540EA8939B2EF15468B332B30"));
    }

    private static void AssertLayer(
        BibleCorpusLayerImportResult layer,
        string collectionId,
        string visibility,
        int batches,
        int documents,
        int chunks,
        int entities,
        int relations,
        string planHash)
    {
        Assert.Equal(collectionId, layer.CollectionId);
        Assert.Equal("0.1.0", layer.Version);
        Assert.Equal(visibility, layer.Visibility);
        Assert.Equal(batches, layer.Batches);
        Assert.Equal(documents, layer.Documents);
        Assert.Equal(chunks, layer.Chunks);
        Assert.Equal(entities, layer.Entities);
        Assert.Equal(relations, layer.Relations);
        Assert.Equal(planHash, layer.PlanHash);
        Assert.Equal("verified", layer.State);
    }
}
