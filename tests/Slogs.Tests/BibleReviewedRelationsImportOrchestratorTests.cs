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
        Assert.Equal("0.2.0", result.PackageVersion);
        Assert.Equal("B0754CFE88CD79C30AF2188F902F0B4C7231768D844B139C9305FDCD8BD56973", result.PackageHash);
        Assert.True(result.VerifyOnly);
        Assert.Equal("bible-reviewed-relations", result.Layer.CollectionId);
        Assert.Equal("public_shared", result.Layer.Visibility);
        Assert.Equal(1, result.Layer.Batches);
        Assert.Equal(1, result.Layer.Documents);
        Assert.Equal(9, result.Layer.Chunks);
        Assert.Equal(0, result.Layer.Entities);
        Assert.Equal(38, result.Layer.Relations);
        Assert.Equal("0C46BE6586C8BFF0658667E1F188BA7DA73C529E711FA2AFC2F4A8CD796945BD", result.Layer.PlanHash);
        Assert.Equal("verified", result.Layer.State);
    }
}
