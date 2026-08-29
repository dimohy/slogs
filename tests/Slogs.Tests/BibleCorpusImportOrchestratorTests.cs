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
                "6A9A4A8BFB1D4DCF0C83602907CBD588483777733F7EAFAFC27468938AFC09FE"),
            layer => AssertLayer(layer, "bible-ko-tkv", "private", 111, 66, 2_203, 0, 31_097,
                "6892EB4E5E03747215A3468C7D6EAB8145921B7998D626937D814E0C458953AD"),
            layer => AssertLayer(layer, "bible-original-step", "public_shared", 2_426, 66, 48_515, 4_259, 456_058,
                "6F00F97A1A5D50B2853C6F556D9E0033F978B2EBE6C05BE3B0E486C5AC88D137"));
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
