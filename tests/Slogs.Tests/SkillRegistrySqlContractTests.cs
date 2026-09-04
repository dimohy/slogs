using Xunit;

namespace Slogs.Tests;

public sealed class SkillRegistrySqlContractTests
{
    [Fact]
    public void ResolutionUsesActualProjectKeyAndDoesNotLeakProjectDisabledChoice()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "SkillRegistryService.cs"));

        Assert.DoesNotContain("ProjectKeyKey", source, StringComparison.Ordinal);
        Assert.Contains("\"ProjectKey\" = CAST(@projectKey AS text)", source, StringComparison.Ordinal);
        Assert.Contains("\"ProjectKey\" IS NULL AND \"ScopeKind\" IN ('global', 'disabled')", source, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonbCompatibilityDefaultEscapesEfRawSqlFormatBraces()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "SlogsDbInitializer.cs"));

        Assert.Contains("DEFAULT '{{}}'::jsonb", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT '{}'::jsonb", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchUsesParameterizedPreciseAliasSubsetMatchingAndLatestValidatedVersion()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "SkillRegistryService.cs"));

        Assert.Contains("unnest(CAST(@terms AS text[]))", source, StringComparison.Ordinal);
        Assert.Contains("NOT LIKE '% ' || requested.\"Term\" || ' %'", source, StringComparison.Ordinal);
        Assert.Contains("\"PackageJson\" -> 'searchAliases'", source, StringComparison.Ordinal);
        Assert.Contains("jsonb_array_elements_text", source, StringComparison.Ordinal);
        Assert.Contains("aliasTerm.\"Term\" <> ALL(CAST(@terms AS text[]))", source, StringComparison.Ordinal);
        Assert.Contains("lower(\"Slug\") = @normalizedQuery", source, StringComparison.Ordinal);
        Assert.Contains("\"Status\" = 'validated'", source, StringComparison.Ordinal);
        Assert.Contains("\"VersionMajor\" DESC, \"VersionMinor\" DESC, \"VersionPatch\" DESC", source, StringComparison.Ordinal);
        Assert.Contains("ORDER BY \"MatchRank\" DESC, \"AliasSpecificity\" DESC, \"Slug\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ILIKE '%' || @query", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"PackageJson\"::text LIKE", source, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Slogs.slnx")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException("Slogs repository root was not found.")
            : Path.Combine([current.FullName, .. pathParts]);
    }
}
