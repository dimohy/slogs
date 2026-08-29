using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class SlogsMcpPolicyPromptServiceTests
{
    [Theory]
    [InlineData("slogs llm wiki 프롬프트를 회상 전에 검색하도록 수정해줘", true)]
    [InlineData("Slogs LLM Wiki의 정책 프롬프트에 이 동작을 반영해줘", true)]
    [InlineData("Agentic Shaping 경험을 Slogs LLM Wiki 프롬프트 정책에 적용해줘", true)]
    [InlineData("Slogs LLM Wiki 정책에 그게 반영되었는지 확인하고 MCP도 그렇게 반응하게 해줘", true)]
    [InlineData("Update the Slogs LLM Wiki prompt to search first", true)]
    [InlineData("Apply this interlock rule to the Slogs LLM Wiki prompt", true)]
    [InlineData("Update the Slogs LLM Wiki policy to choose graph depth", true)]
    [InlineData("Slogs LLM Wiki 정책이 어떻게 동작해?", false)]
    [InlineData("앞으로 이 정정을 기억해줘", false)]
    [InlineData("LLM Wiki 구현을 수정해줘", false)]
    public void ExplicitRequestMustNamePolicyOrPromptAndChange(string request, bool expected)
        => Assert.Equal(expected, SlogsMcpPolicyPromptService.IsExplicitPromptUpdateRequest(request));

    [Theory]
    [InlineData("2026.07.13.1", "2026-07-13T03:00:00Z", "2026.07.13.2")]
    [InlineData("2026.07.12.9", "2026-07-13T03:00:00Z", "2026.07.13.1")]
    public void VersionIsServerAssignedAndIncrements(string current, string now, string expected)
        => Assert.Equal(expected, SlogsMcpPolicyPromptService.NextVersion(current, DateTimeOffset.Parse(now)));
}
