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
    [InlineData("Agentic Shaping과 Slogs LLM Wiki 시스템 진화까지 지금 요청하며 진행 중 durable 신호에 따라 계속 진화해줘", true)]
    [InlineData("Slogs LLM Wiki 시스템을 계속 진화시켜줘", true)]
    [InlineData("Continue evolving the Slogs LLM Wiki system during this active goal", true)]
    [InlineData("Slogs LLM Wiki 정책이 어떻게 동작해?", false)]
    [InlineData("Slogs LLM Wiki 시스템이 어떻게 동작해?", false)]
    [InlineData("앞으로 이 정정을 기억해줘", false)]
    [InlineData("LLM Wiki 구현을 수정해줘", false)]
    [InlineData("아니... 문제가 있고 정형으로 해결이 안될것 같으면 너가 직접 읽어서 문제를 확인해야지... 내말이 맞다면 에이전틱셰이핑 및 Slogs LLM 정책에도 반영해줘", true)]
    [InlineData("Slogs LLM 정책을 원문 직접 확인 원칙으로 수정해줘", true)]
    [InlineData("Update the Slogs LLM policy to inspect original sources", true)]
    [InlineData("Slogs LLM 정책이 뭐야?", false)]
    [InlineData("Slogs LLM 정책을 기억해줘", false)]
    [InlineData("Slogs LLM 기능을 수정해줘", false)]
    [InlineData("Slogs LLM 시스템을 진화시켜줘", false)]
    [InlineData("Other LLM policy를 수정해줘", false)]
    public void ExplicitRequestMustNamePolicyOrPromptAndChange(string request, bool expected)
        => Assert.Equal(expected, SlogsMcpPolicyPromptService.IsExplicitPromptUpdateRequest(request));

    [Theory]
    [InlineData("2026.07.13.1", "2026-07-13T03:00:00Z", "2026.07.13.2")]
    [InlineData("2026.07.12.9", "2026-07-13T03:00:00Z", "2026.07.13.1")]
    public void VersionIsServerAssignedAndIncrements(string current, string now, string expected)
        => Assert.Equal(expected, SlogsMcpPolicyPromptService.NextVersion(current, DateTimeOffset.Parse(now)));
}
