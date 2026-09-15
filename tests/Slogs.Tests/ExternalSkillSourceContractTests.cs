using System.Net;
using System.Text;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class ExternalSkillSourceContractTests
{
    private const string SkillMarkdown = """
        ---
        name: diagram-design
        description: Create clear diagrams from structured relationships.
        ---

        Build the smallest diagram that materially improves understanding.
        """;

    [Fact]
    public void NormalizePreservesCanonicalRepositoryInsteadOfSnapshotPackage()
    {
        var source = ExternalSkillSourceContract.Normalize(
            "diagram-design",
            "https://github.com/cathrynlavery/diagram-design.git",
            "main",
            "skills/diagram-design/SKILL.md",
            "Create architecture, process, relationship, and explanatory diagrams when visuals improve understanding.",
            "MIT",
            "[\"diagram architecture visualization\",\"다이어그램 도식 시각화\"]");

        Assert.Equal("https://github.com/cathrynlavery/diagram-design", source.SourceUrl);
        Assert.Equal("cathrynlavery", source.RepositoryOwner);
        Assert.Equal("diagram-design", source.RepositoryName);
        Assert.Equal("main", source.TrackingRef);
        Assert.Contains("다이어그램 도식 시각화",
            System.Text.Json.JsonSerializer.Deserialize<string[]>(source.SearchAliasesJson)!);
    }

    [Theory]
    [InlineData("http://github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo/tree/main")]
    [InlineData("https://example.com/owner/repo")]
    public void NormalizeRejectsNonCanonicalSources(string sourceUrl)
        => Assert.Throws<InvalidOperationException>(() => ExternalSkillSourceContract.Normalize(
            "diagram-design", sourceUrl, "main", "skills/diagram-design/SKILL.md",
            "Create useful architecture and process diagrams.", "MIT",
            "[\"diagram architecture visualization\"]"));

    [Theory]
    [InlineData("../SKILL.md")]
    [InlineData("skills//SKILL.md")]
    [InlineData("/skills/SKILL.md")]
    public void RepositoryPathRejectsTraversalAndAmbiguity(string path)
        => Assert.Throws<InvalidOperationException>(() => ExternalSkillSourceContract.NormalizeRepositoryPath(path));

    [Fact]
    public void EntrypointCompatibilityBindsExpectedSlugAndHash()
    {
        var hash = ExternalSkillSourceContract.ValidateEntrypoint("diagram-design", SkillMarkdown);
        Assert.Equal(64, hash.Length);
        Assert.Throws<InvalidOperationException>(() =>
            ExternalSkillSourceContract.ValidateEntrypoint("another-skill", SkillMarkdown));
    }

    [Fact]
    public async Task GitHubClientChecksLatestCommitBeforeReadingPinnedContent()
    {
        const string revision = "9874ad73813715fc36875e45afd9cd94c68f3c3f";
        var requested = new List<Uri>();
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requested.Add(request.RequestUri!);
            return request.RequestUri!.Host == "api.github.com"
                ? Json(HttpStatusCode.OK, $$"""{"sha":"{{revision}}"}""")
                : Text(HttpStatusCode.OK, SkillMarkdown);
        }));
        var client = new GitHubExternalSkillClient(httpClient);
        var source = ExternalSkillSourceContract.Normalize(
            "diagram-design", "https://github.com/cathrynlavery/diagram-design", "main",
            "skills/diagram-design/SKILL.md", "Create useful architecture and process diagrams.", "MIT",
            "[\"diagram architecture visualization\"]");

        var resolvedRevision = await client.GetLatestRevisionAsync(source);
        var content = await client.ReadFileAsync(source, resolvedRevision, source.EntrypointPath);

        Assert.Equal(revision, resolvedRevision);
        Assert.Equal(SkillMarkdown, content);
        Assert.Equal("api.github.com", requested[0].Host);
        Assert.Contains(revision, requested[1].AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHubClientDoesNotSilentlyUseCacheWhenUpstreamCheckFails()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => Text(HttpStatusCode.ServiceUnavailable, "offline")));
        var client = new GitHubExternalSkillClient(httpClient);
        var source = ExternalSkillSourceContract.Normalize(
            "diagram-design", "https://github.com/cathrynlavery/diagram-design", "main",
            "skills/diagram-design/SKILL.md", "Create useful architecture and process diagrams.", "MIT",
            "[\"diagram architecture visualization\"]");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetLatestRevisionAsync(source));
    }

    [Fact]
    public void SearchFormattingExposesCanonicalSourceWithoutInstructionBody()
    {
        var external = new ExternalSkillSearchResult(
            "diagram-design", "Create explanatory diagrams.",
            "https://github.com/cathrynlavery/diagram-design", "main", "skills/diagram-design/SKILL.md", "MIT");

        var output = SkillRegistryMcpTools.FormatSearchResults([], [external]);

        Assert.Contains("diagram-design (external canonical source)", output, StringComparison.Ordinal);
        Assert.Contains(external.SourceUrl, output, StringComparison.Ordinal);
        Assert.DoesNotContain("Build the smallest diagram", output, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Text(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
