using System.ComponentModel;
using System.Reflection;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class LlmWikiAdaptiveGraphHopContractTests
{
    [Fact]
    public void SearchAndRecallToolsExposeOneSharedSmallestSufficientHopContract()
    {
        var descriptions = new[]
        {
            ParameterDescription(nameof(LlmWikiMcpTools.SearchAsync)),
            ParameterDescription(nameof(LlmWikiMcpTools.RecallAsync)),
            ParameterDescription(nameof(LlmWikiMcpTools.PublicSearchAsync)),
            ParameterDescription(nameof(LlmWikiMcpTools.PublicRecallAsync))
        };

        Assert.Single(descriptions.Distinct(StringComparer.Ordinal));
        var description = descriptions[0];
        Assert.Contains("smallest sufficient depth", description, StringComparison.Ordinal);
        Assert.Contains("use 1 for a direct memory", description, StringComparison.Ordinal);
        Assert.Contains("use 2 when one relationship bridge", description, StringComparison.Ordinal);
        Assert.Contains("use 3 for a multi-stage causal, provenance, dependency, or chronological chain", description, StringComparison.Ordinal);
        Assert.Contains("Do not use 3 for every query", description, StringComparison.Ordinal);
        Assert.Contains("Agents should still pass 1 explicitly", description, StringComparison.Ordinal);
        Assert.Contains("Start progressive refinement at 1", description, StringComparison.Ordinal);
        Assert.Contains("raise to 2 or 3 only when returned relationship evidence requires another stage", description, StringComparison.Ordinal);
        Assert.Contains("inspect Retrieval Diagnostics", description, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchAndRecallToolsKeepOneHopAsTheCompatibilityDefault()
    {
        foreach (var methodName in new[]
                 {
                     nameof(LlmWikiMcpTools.SearchAsync),
                     nameof(LlmWikiMcpTools.RecallAsync),
                     nameof(LlmWikiMcpTools.PublicSearchAsync),
                     nameof(LlmWikiMcpTools.PublicRecallAsync)
                 })
        {
            var parameter = HopParameter(methodName);
            Assert.True(parameter.HasDefaultValue);
            Assert.Equal(1, parameter.DefaultValue);
        }
    }

    private static string ParameterDescription(string methodName)
    {
        var description = HopParameter(methodName).GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        return description.Description;
    }

    private static ParameterInfo HopParameter(string methodName)
    {
        var method = typeof(LlmWikiMcpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        return Assert.Single(method.GetParameters(), parameter => parameter.Name == "maxGraphHops");
    }
}
