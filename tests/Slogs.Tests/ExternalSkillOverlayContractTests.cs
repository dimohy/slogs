using System.Text.Json;
using Xunit;

namespace Slogs.Tests;

public sealed class ExternalSkillOverlayContractTests
{
    [Fact]
    public void FrozenBehaviorContractCoversPositiveBoundaryAndNegativeCases()
    {
        var fixture = File.ReadAllText(FindRepoFile("tests", "Fixtures", "external-skill-overlay-contract.v1.json"));
        using var document = JsonDocument.Parse(fixture);
        var root = document.RootElement;
        Assert.True(root.GetProperty("frozenBeforeImplementation").GetBoolean());
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(5, cases.Length);
        Assert.Contains(cases, item => item.GetProperty("kind").GetString() == "boundary");
        Assert.Equal(2, cases.Count(item => item.GetProperty("kind").GetString() == "negative-control"));
    }

    [Fact]
    public void ExternalResolveUsesLiveUpstreamAndValidatedConventionOverlayWithoutBodyCache()
    {
        var initializer = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "SlogsDbInitializer.cs"));
        var service = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "ExternalSkillRegistryService.cs"));
        var tools = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "SkillRegistryMcpTools.cs"));

        Assert.Contains("DROP COLUMN IF EXISTS \"LastResolvedContent\"", initializer, StringComparison.Ordinal);
        Assert.DoesNotContain("@content, @contentHash", service, StringComparison.Ordinal);
        Assert.DoesNotContain("cache-current-after-upstream-check", service, StringComparison.Ordinal);
        Assert.Contains("sourceClient.ReadFileAsync", service, StringComparison.Ordinal);
        Assert.Contains("$\"{externalSlug}-slogs-overlay\"", service, StringComparison.Ordinal);
        Assert.Contains("AND \"Status\" = 'validated'", service, StringComparison.Ordinal);
        Assert.Contains("applicationOrder: upstream then Slogs overlay", tools, StringComparison.Ordinal);
        Assert.Contains("contentPersistence: none", tools, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. pathParts]);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException(string.Join('/', pathParts));
    }
}
