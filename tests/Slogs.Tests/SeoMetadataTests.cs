using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class SeoMetadataTests
{
    [Fact]
    public void BuildRobotsTxtDoesNotBlockPublicFeedFiles()
    {
        var robots = SeoMetadata.BuildRobotsTxt("https://slogs.dev/");
        var lines = robots.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.DoesNotContain("Disallow: /feed", lines);
        Assert.Contains("# RSS feed: https://slogs.dev/feed.xml", lines);
        Assert.Contains("# JSON feed: https://slogs.dev/feed.json", lines);
    }
}
