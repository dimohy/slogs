using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class SlogsMcpPolicyPromptTests
{
    [Fact]
    public void VersionTextMatchesPromptVersion()
    {
        Assert.Equal("2026.07.13.1\n", SlogsMcpPolicyPrompt.BuildVersionText());
        Assert.Contains("Prompt Version: 2026.07.13.1", SlogsMcpPolicyPrompt.BuildKoreanMarkdown());
        Assert.Contains("Prompt Version: 2026.07.13.1", SlogsMcpPolicyPrompt.BuildEnglishMarkdown());
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
