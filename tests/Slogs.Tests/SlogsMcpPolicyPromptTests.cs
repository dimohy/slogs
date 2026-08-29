using Slogs.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Slogs.Tests;

public sealed class SlogsMcpPolicyPromptTests
{
    [Fact]
    public void VersionTextMatchesPromptVersion()
    {
        Assert.Equal("2026.08.30.2\n", SlogsMcpPolicyPrompt.BuildVersionText());
        Assert.Contains("Prompt Version: 2026.08.30.2", SlogsMcpPolicyPrompt.BuildKoreanMarkdown());
        Assert.Contains("Prompt Version: 2026.08.30.2", SlogsMcpPolicyPrompt.BuildEnglishMarkdown());
    }

    [Fact]
    public void AgentPromptsDefineEvidenceBackedIncrementalGrowingGraphBehavior()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("성장형 그래프", koreanPrompt);
        Assert.Contains("`relationsJson`", koreanPrompt);
        Assert.Contains("같은 트랜잭션", koreanPrompt);
        Assert.Contains("근거가 사라진 관계는 retired 처리", koreanPrompt);
        Assert.Contains("edge 수나 호출 횟수가 아니라", koreanPrompt);
        Assert.Contains("growing graph", englishPrompt);
        Assert.Contains("evidence quotes that occur in both sources", englishPrompt);
        Assert.Contains("same transaction", englishPrompt);
        Assert.Contains("Retire relations whose evidence disappears", englishPrompt);
        Assert.Contains("not edge count or call count", englishPrompt);
    }

    [Fact]
    public void GrowingGraphBehaviorEvaluationContractIsFrozen()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "slogs-growing-graph-policy.v1.json");
        var lockPath = Path.Combine(
            Path.GetDirectoryName(fixturePath)!,
            "slogs-growing-graph-policy.v1.sha256");
        var bytes = File.ReadAllBytes(fixturePath);
        var expectedHash = File.ReadAllText(lockPath).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)));
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.Equal(5, root.GetProperty("positiveCases").GetArrayLength());
        Assert.Equal(5, root.GetProperty("negativeCases").GetArrayLength());
        Assert.Equal(0, root.GetProperty("metrics").GetProperty("allowedFalsePositiveTypedEdges").GetInt32());
        Assert.Equal(0, root.GetProperty("metrics").GetProperty("allowedPermissionLeaks").GetInt32());
        Assert.Equal(0, root.GetProperty("metrics").GetProperty("allowedPartialCommits").GetInt32());
    }

    [Fact]
    public void AgentPromptsDefineGenericKnowledgeCorpusEvidenceAndAuthorityBoundaries()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("범용 Knowledge Corpus", koreanPrompt);
        Assert.Contains("후보·비승인 관계를 정답으로 승격하지 않는다", koreanPrompt);
        Assert.Contains("`public_shared`는 접근 가능한 읽기 근거일 뿐 공개 수정을 허용하지 않는다", koreanPrompt);
        Assert.Contains("`pairScoreCalls`", koreanPrompt);
        Assert.Contains("`general-bge-m3-dense`", koreanPrompt);
        Assert.Contains("`relational-bge-m3-full`", koreanPrompt);
        Assert.Contains("generic Knowledge Corpus", englishPrompt);
        Assert.Contains("candidate or unapproved relations", englishPrompt);
        Assert.Contains("permits accessible reading, not public editing", englishPrompt);
        Assert.Contains("`pairScoreCalls`", englishPrompt);
        Assert.Contains("`general-bge-m3-dense`", englishPrompt);
        Assert.Contains("`relational-bge-m3-full`", englishPrompt);
    }

    [Fact]
    public void KnowledgeCorpusPolicyAndLatencyEvaluationContractIsFrozenAndComplete()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "slogs-knowledge-corpus-policy-latency.v1.json");
        var lockPath = Path.Combine(
            Path.GetDirectoryName(fixturePath)!,
            "slogs-knowledge-corpus-policy-latency.v1.sha256");
        var bytes = File.ReadAllBytes(fixturePath);
        var expectedHash = File.ReadAllText(lockPath).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)));
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.Equal(4, root.GetProperty("policyCases").GetArrayLength());
        Assert.Equal(2, root.GetProperty("runtimeCases").GetArrayLength());
        Assert.Equal(0, root.GetProperty("passThresholds").GetProperty("policyForbiddenActions").GetInt32());
        Assert.Equal(0, root.GetProperty("passThresholds").GetProperty("regressionFailures").GetInt32());
    }

    [Fact]
    public void AgentPromptsRequireExplicitDimohyPolicyPromptUpdate()
    {
        Assert.Contains("`dimohy`", SlogsMcpPolicyPrompt.BuildKoreanMarkdown());
        Assert.Contains("명시적으로 요청한 경우에만 `llm_wiki_update_policy_prompt`", SlogsMcpPolicyPrompt.BuildKoreanMarkdown());
        Assert.Contains("Never infer prompt-editing intent", SlogsMcpPolicyPrompt.BuildEnglishMarkdown());
    }

    [Fact]
    public void AgentPromptsIncludeCorrectionPromptPolicy()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("의도 보정 신호", koreanPrompt);
        Assert.Contains("원치 않았던 전개", koreanPrompt);
        Assert.Contains("intent-correction signal", englishPrompt);
        Assert.Contains("unwanted development", englishPrompt);
    }

    [Fact]
    public void AgentPromptsRequireDirectSessionStartManagedBlockUpdate()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("같은 지침 위치의 기존 `SLOGS_MCP_PROMPT` 지침 블록을 즉시 교체", koreanPrompt);
        Assert.Contains("보고만 하고 멈추지 않는다", koreanPrompt);
        Assert.Contains("별도 동기화 스크립트", koreanPrompt);
        Assert.Contains("immediately replace the previous `SLOGS_MCP_PROMPT` instruction block", englishPrompt);
        Assert.Contains("Do not stop after merely reporting", englishPrompt);
        Assert.Contains("separate sync script", englishPrompt);
        Assert.DoesNotContain("관리 블록", koreanPrompt);
        Assert.DoesNotContain("managed block", englishPrompt);
    }

    [Fact]
    public void AgentPromptsUseKnowledgeLogWordingForSlogsPostTools()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("공개 지식 로그", koreanPrompt);
        Assert.Contains("공개 공유", koreanPrompt);
        Assert.Contains("public knowledge-log", englishPrompt);
        Assert.Contains("public sharing", englishPrompt);
        Assert.DoesNotContain("블로그 글", koreanPrompt);
        Assert.DoesNotContain("post (blog)", englishPrompt);
    }

    [Fact]
    public void AgentPromptsFramePublicLlmWikiVisibilityAsPublicMemory()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("공개 기억", koreanPrompt);
        Assert.Contains("공개 기억 회상", koreanPrompt);
        Assert.Contains("공개된 기억이 없다고 답한다", koreanPrompt);
        Assert.Contains("public memory", englishPrompt);
        Assert.Contains("public-memory recall questions", englishPrompt);
        Assert.Contains("LLM Wiki memories are private by default", englishPrompt);

        Assert.DoesNotContain("공개 기억 조회", koreanPrompt);
        Assert.DoesNotContain("공개 Wiki", koreanPrompt);
        Assert.DoesNotContain("private 조회", koreanPrompt);
        Assert.DoesNotContain("public Wiki", englishPrompt);
        Assert.DoesNotContain("their own wiki", englishPrompt);
        Assert.DoesNotContain("private lookup results", englishPrompt);
    }
}
