using System.Text.Json;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class SkillRegistryContractTests
{
    private const string Provenance = """
        {"sourceType":"original","sourceLocator":"artifact://source/skill","sourceSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","licenseVerified":true,"licenseEvidenceLocator":"artifact://license/evidence","licenseEvidenceSha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}
        """;
    private const string WindowsEvidence = """
        [{"platform":"windows","suiteId":"windows-v1","evidenceLocator":"artifact://platform/windows","evidenceSha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}]
        """;
    private const string SkillMarkdown = """
        ---
        name: korean-software-terminology
        description: Choose natural Korean software terminology for engineering reports.
        ---

        Prefer domain-appropriate Korean terms and validate ambiguous translations.
        """;

    [Fact]
    public void PrepareProducesDeterministicCanonicalHash()
    {
        var first = SkillRegistryContract.Prepare(
            "korean-software-terminology", "1.2.3", "Natural Korean terminology selection.",
            SkillMarkdown.Replace("\n", "\r\n"), "Apache-2.0", "registry-candidate", Provenance, WindowsEvidence,
            "[{\"path\":\"references/terms.md\",\"content\":\"gate: 검증 단계\\n\"}]");
        var second = SkillRegistryContract.Prepare(
            "korean-software-terminology", "1.2.3", "Natural Korean terminology selection.",
            SkillMarkdown, "Apache-2.0", "registry-candidate", Provenance, WindowsEvidence,
            "[{\"content\":\"gate: 검증 단계\\n\",\"path\":\"references/terms.md\"}]");

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(64, first.ContentHash.Length);
        Assert.Equal("dev.slogs.skills.korean-software-terminology", first.Payload.Id);
        Assert.Equal("verified-compatible-latest", first.Payload.Selection.UpdatePolicy);
        Assert.Equal("Apache-2.0", first.Payload.License);
        Assert.Equal("registry-candidate", first.Payload.Visibility);
        Assert.Equal(["windows"], first.Payload.Compatibility.Platforms);
        Assert.Equal(["SKILL.md", "references/terms.md"], first.Payload.Files.Select(file => file.Path));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("references//terms.md")]
    public void PrepareRejectsUnsafeSupportingPaths(string path)
    {
        var files = JsonSerializer.Serialize(new[] { new SkillPackageFileInput(path, "content") });
        Assert.Throws<InvalidOperationException>(() => SkillRegistryContract.Prepare(
            "safe-skill", "1.0.0", "A sufficiently specific skill description.", SkillMarkdown,
            "Apache-2.0", "registry-candidate", Provenance, WindowsEvidence, files));
    }

    [Fact]
    public void ValidationEvidenceRequiresAllChecksAndNoForbiddenActions()
    {
        var valid = """
            {"suiteId":"terminology-v1","evaluatorVersion":"1.0.0","frozenBeforeRun":true,"caseResults":[{"kind":"normal","passed":2,"total":2},{"kind":"boundary","passed":2,"total":2},{"kind":"negative-control","passed":2,"total":2}],"passed":6,"total":6,"forbiddenActions":0,"artifactEvidence":[{"locator":"artifact://evaluation/result","sha256":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"}],"outputSha256":"e6e12ba493ef5a5a49c31bcbf7482287dcd590d0403ceb96a3c67e0404fb6827"}
            """;
        var invalid = """
            {"suiteId":"terminology-v1","evaluatorVersion":"1.0.0","frozenBeforeRun":true,"caseResults":[{"kind":"normal","passed":1,"total":2}],"passed":5,"total":6,"forbiddenActions":1,"artifactEvidence":[],"outputSha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}
            """;

        const string payload = "{\"runId\":\"eval-1\",\"cases\":[{\"id\":\"n\",\"passed\":true}]}";
        Assert.Equal(6, SkillRegistryContract.ValidateEvidence(valid, payload).Passed);
        Assert.Throws<InvalidOperationException>(() => SkillRegistryContract.ValidateEvidence(invalid, payload));
        Assert.Throws<InvalidOperationException>(() => SkillRegistryContract.ValidateEvidence(valid, "{\"tampered\":true}"));
    }

    [Fact]
    public void EvaluationPayloadHashIsStableAcrossJsonbPropertyReordering()
        => Assert.Equal(
            SkillRegistryContract.ComputeCanonicalJsonHash("{\"runId\":\"eval-1\",\"cases\":[{\"passed\":true,\"id\":\"n\"}]}"),
            SkillRegistryContract.ComputeCanonicalJsonHash("{\"cases\":[{\"id\":\"n\",\"passed\":true}],\"runId\":\"eval-1\"}"));

    [Fact]
    public void PrepareRejectsInferredLicensePublicVisibilityAndUntestedPlatforms()
    {
        Assert.Throws<InvalidOperationException>(() => SkillRegistryContract.Prepare(
            "safe-skill", "1.0.0", "A sufficiently specific skill description.", SkillMarkdown,
            "", "registry-candidate", Provenance, WindowsEvidence, null));
        Assert.Throws<InvalidOperationException>(() => SkillRegistryContract.Prepare(
            "safe-skill", "1.0.0", "A sufficiently specific skill description.", SkillMarkdown,
            "MIT", "public", Provenance, WindowsEvidence, null));
        Assert.Throws<InvalidOperationException>(() => SkillRegistryContract.Prepare(
            "safe-skill", "1.0.0", "A sufficiently specific skill description.", SkillMarkdown,
            "MIT", "registry-candidate", Provenance,
            "[{\"platform\":\"linux\",\"suiteId\":\"\",\"evidenceLocator\":\"artifact://none\",\"evidenceSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}]", null));
    }

    [Fact]
    public void PromotionReviewMustBindActualExecutionToCandidateHashes()
    {
        const string contentHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string reportHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var valid = $$"""{"reviewer":"dimohy","actualExecutionReviewed":true,"evidenceLocator":"artifact://review/run","evidenceSha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","contentHash":"{{contentHash}}","validationReportHash":"{{reportHash}}"}""";

        Assert.True(SkillRegistryContract.ValidateReviewEvidence(valid, "dimohy", contentHash, reportHash).ActualExecutionReviewed);
        Assert.Throws<InvalidOperationException>(() => SkillRegistryContract.ValidateReviewEvidence(
            valid.Replace("true", "false", StringComparison.Ordinal), "dimohy", contentHash, reportHash));
    }

    [Fact]
    public void CandidateRequiresReusableAbstractionAndPrivacySafeGeneralization()
    {
        var valid = """
            {"ruleId":"AS-SK-001","traceAuthority":"orchestrator","abstractionLevel":"cross-project","reusableAcrossProjects":true,"generalized":true,"sourceSignals":[{"kind":"correction","locator":"artifact://evaluation/case-1"}],"structuredAssetPlan":"skill package","evaluationContract":"frozen suite","privacy":{"containsPersonalMemory":false,"containsProjectConfidential":false,"containsCredential":false,"containsSecret":false,"generalizationVerified":true,"evidence":[{"locator":"artifact://privacy/scan","sha256":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"}]}}
            """;
        var projectOnly = valid.Replace("cross-project", "project", StringComparison.Ordinal);
        var leaksPrivateProject = valid.Replace(
            "\"containsProjectConfidential\":false", "\"containsProjectConfidential\":true", StringComparison.Ordinal);

        Assert.Equal("cross-project", SkillRegistryContract.ValidateCandidateEvidence(valid).AbstractionLevel);
        Assert.Throws<InvalidOperationException>(() => SkillRegistryContract.ValidateCandidateEvidence(projectOnly));
        Assert.Throws<InvalidOperationException>(() => SkillRegistryContract.ValidateCandidateEvidence(leaksPrivateProject));
    }

    [Theory]
    [InlineData("project")]
    [InlineData("global")]
    [InlineData("disabled")]
    public void FirstUseChoiceHasExactlyThreeSupportedValues(string choice)
        => Assert.Equal(choice, SkillRegistryContract.NormalizeChoice(choice));

    [Fact]
    public void FirstUseChoiceRejectsImplicitApplication()
        => Assert.Throws<InvalidOperationException>(() => SkillRegistryContract.NormalizeChoice("apply"));

    [Fact]
    public void PackageContentIsFailClosedBeforeChoiceAndWhenDisabled()
    {
        Assert.False(SkillRegistryContract.CanReleasePackage(null));
        Assert.False(SkillRegistryContract.CanReleasePackage(new(
            "korean-software-terminology", "disabled", null, true, true, null, "user declined", DateTimeOffset.UtcNow)));
        Assert.True(SkillRegistryContract.CanReleasePackage(new(
            "korean-software-terminology", "project", "p:/myworks/sollang", true, true, null, "user chose project", DateTimeOffset.UtcNow)));
    }
}
