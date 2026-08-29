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
        Assert.Equal("474DC968F5DCB9E45C122FF58966353F217B6D0AD40F34F7888B8085608848EB", result.PackageHash);
        Assert.True(result.VerifyOnly);
        Assert.Equal("bible-reviewed-relations", result.Layer.CollectionId);
        Assert.Equal("public_shared", result.Layer.Visibility);
        Assert.Equal(1, result.Layer.Batches);
        Assert.Equal(1, result.Layer.Documents);
        Assert.Equal(1, result.Layer.Chunks);
        Assert.Equal(0, result.Layer.Entities);
        Assert.Equal(9, result.Layer.Relations);
        Assert.Equal("C19C51DDBE224653B0C30F091ABC3C56A1EE494868194C9256586E8F4B0223FE", result.Layer.PlanHash);
        Assert.Equal("verified", result.Layer.State);
    }
}
