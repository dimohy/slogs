using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BibleReviewedRelationsImportOrchestratorTests
{
    [Fact]
    public async Task FullReviewPackageProducesDeterministicPublicOverlay()
    {
        var root = Environment.GetEnvironmentVariable("SLOGS_BIBLE_REVIEW_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var result = await new BibleReviewedRelationsImportOrchestrator(
            new BibleCorpusImportRunner(null!)).RunAsync(
            new BibleReviewedRelationsImportOptions(
                root,
                Path.Combine(Path.GetTempPath(), "unused-bible-review-checkpoints"),
                "dimohy",
                VerifyOnly: true));

        Assert.Equal("slogs-bible-agent-reviewed-relations", result.PackageId);
        Assert.Equal("0.1.0", result.PackageVersion);
        Assert.Equal("60D0BA3E971FD0282F8F168F4FE2227348E4D8A994585704ABDC61EB73D7B6AF", result.PackageHash);
        Assert.True(result.VerifyOnly);
        Assert.Equal("bible-reviewed-relations", result.Layer.CollectionId);
        Assert.Equal("public_shared", result.Layer.Visibility);
        Assert.Equal(1, result.Layer.Batches);
        Assert.Equal(1, result.Layer.Documents);
        Assert.Equal(1, result.Layer.Chunks);
        Assert.Equal(0, result.Layer.Entities);
        Assert.Equal(38, result.Layer.Relations);
        Assert.Equal("BF3A46BC30D445CF396138D325EFA6F915FEDEB5D518B16A61AC8416E7A9E03F", result.Layer.PlanHash);
        Assert.Equal("verified", result.Layer.State);
    }
}
