using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class SkillRegistryPostgresIntegrationTests
{
    [Fact]
    public async Task CandidatePromotionFirstUseAndAutomaticLatestResolutionAreFailClosed()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SLOGS_SKILL_REGISTRY_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("SLOGS_SKILL_REGISTRY_CONNECTION")
            ?? throw new InvalidOperationException("SLOGS_SKILL_REGISTRY_CONNECTION is required.");
        var services = new ServiceCollection();
        services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(connectionString));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
        var registry = new SkillRegistryService(factory);
        var slug = $"integration-skill-{Guid.NewGuid():N}";
        var owner = $"skill-integration-{Guid.NewGuid():N}";
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new UserRecord
            {
                UserName = owner,
                DisplayName = "Skill integration",
                Email = $"{owner}@example.invalid",
                Password = "not-a-login-account"
            });
            await db.SaveChangesAsync();
        }
        const string markdown = "---\nname: integration-skill\ndescription: Integration test skill package.\n---\n\nTest.\n";
        const string provenance = """
            {"sourceType":"original","sourceLocator":"artifact://integration/source","sourceSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","licenseVerified":true,"licenseEvidenceLocator":"artifact://integration/license","licenseEvidenceSha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}
            """;
        const string platforms = """
            [{"platform":"windows","suiteId":"registry-integration","evidenceLocator":"artifact://integration/windows","evidenceSha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}]
            """;
        const string candidateEvidence = """
            {"ruleId":"AS-SK-001","traceAuthority":"orchestrator","abstractionLevel":"general-method","reusableAcrossProjects":true,"generalized":true,"sourceSignals":[{"kind":"repeated-failure","locator":"artifact://integration/case"}],"structuredAssetPlan":"shared skill","evaluationContract":"frozen integration suite","privacy":{"containsPersonalMemory":false,"containsProjectConfidential":false,"containsCredential":false,"containsSecret":false,"generalizationVerified":true,"evidence":[{"locator":"artifact://integration/privacy","sha256":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"}]}}
            """;
        const string validation = """
            {"suiteId":"registry-integration","evaluatorVersion":"1.0.0","frozenBeforeRun":true,"caseResults":[{"kind":"normal","passed":1,"total":1},{"kind":"boundary","passed":1,"total":1},{"kind":"negative-control","passed":1,"total":1}],"passed":3,"total":3,"forbiddenActions":0,"artifactEvidence":[{"locator":"artifact://integration/result","sha256":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"}],"outputSha256":"e6e12ba493ef5a5a49c31bcbf7482287dcd590d0403ceb96a3c67e0404fb6827"}
            """;
        const string evaluationPayload = "{\"runId\":\"eval-1\",\"cases\":[{\"id\":\"n\",\"passed\":true}]}";

        var firstPrepared = await registry.PrepareAsync(
            slug, "1.0.0", "Integration registry behavior skill.", markdown,
            "Apache-2.0", "registry-candidate", provenance, platforms, null);
        var candidate = await registry.SubmitCandidateAsync(
            "dimohy", slug, "1.0.0", "Integration registry behavior skill.", markdown,
            "Apache-2.0", "registry-candidate", provenance, platforms, null,
            candidateEvidence, validation, evaluationPayload, firstPrepared.ContentHash);
        Assert.Equal("validated-candidate", candidate.Status);
        Assert.Empty(await registry.SearchAsync(slug, 5));
        Assert.True((await registry.ResolveAsync(owner, slug, "project/integration")).FirstUseDecisionRequired);

        var validated = await registry.ValidateCandidateAsync(
            "dimohy", candidate.Id, candidate.ContentHash, candidate.ValidationReportHash,
            ReviewEvidence(candidate));
        Assert.Equal("validated", validated.Status);
        Assert.Single(await registry.SearchAsync(slug, 5));

        await registry.ChooseAsync(
            owner, slug, "project", "project/integration", true,
            "explicit project choice in integration fixture", autoUpdate: true, pinnedVersion: null);
        var firstResolution = await registry.ResolveAsync(owner, slug, "project/integration");
        Assert.Equal("1.0.0", firstResolution.Package?.Version);

        var secondPrepared = await registry.PrepareAsync(
            slug, "1.1.0", "Integration registry behavior skill updated.", markdown + "Updated.\n",
            "Apache-2.0", "registry-candidate", provenance, platforms, null);
        var secondCandidate = await registry.SubmitCandidateAsync(
            "dimohy", slug, "1.1.0", "Integration registry behavior skill updated.", markdown + "Updated.\n",
            "Apache-2.0", "registry-candidate", provenance, platforms, null,
            candidateEvidence, validation, evaluationPayload, secondPrepared.ContentHash);
        await registry.ValidateCandidateAsync(
            "dimohy", secondCandidate.Id, secondCandidate.ContentHash, secondCandidate.ValidationReportHash,
            ReviewEvidence(secondCandidate));
        var latestResolution = await registry.ResolveAsync(owner, slug, "project/integration");
        Assert.Equal("1.1.0", latestResolution.LatestValidatedVersion);
        Assert.Equal("1.1.0", latestResolution.Package?.Version);

        await registry.ChooseAsync(
            owner, slug, "disabled", "project/integration", true,
            "explicit disabled choice in integration fixture", autoUpdate: true, pinnedVersion: null);
        var disabled = await registry.ResolveAsync(owner, slug, "project/integration");
        Assert.False(disabled.FirstUseDecisionRequired);
        Assert.Null(disabled.Package);
    }

    private static string ReviewEvidence(RegisteredSkillVersion candidate)
        => $$"""{"reviewer":"dimohy","actualExecutionReviewed":true,"evidenceLocator":"artifact://integration/review","evidenceSha256":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","contentHash":"{{candidate.ContentHash}}","validationReportHash":"{{candidate.ValidationReportHash}}"}""";
}
