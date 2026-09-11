using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Slogs.Tests;

public sealed class PublishedMcpContractTests
{
    [Fact]
    public void RequiredOrganizationCorpusToolsAreDeclaredAndRegistered()
    {
        var root = FindRepositoryRoot();
        var contractPath = Path.Combine(root, "tests", "Fixtures", "slogs-required-mcp-tools.v1.json");
        using var contract = JsonDocument.Parse(File.ReadAllText(contractPath));
        var program = File.ReadAllText(Path.Combine(root, "src", "Slogs", "Program.cs"));

        foreach (var toolType in contract.RootElement.GetProperty("requiredToolTypes").EnumerateArray())
        {
            var typeName = toolType.GetProperty("typeName").GetString()!;
            var sourcePath = Path.Combine(root, "src", "Slogs", "Data", $"{typeName}.cs");
            Assert.True(File.Exists(sourcePath), $"Required MCP tool source is missing: {sourcePath}");
            var source = File.ReadAllText(sourcePath);
            Assert.Matches($@"\.WithTools\s*<\s*{Regex.Escape(typeName)}\s*>\s*\(\s*\)", program);

            foreach (var tool in toolType.GetProperty("tools").EnumerateArray())
            {
                var toolName = tool.GetString()!;
                Assert.Matches($@"McpServerTool\s*\(\s*Name\s*=\s*""{Regex.Escape(toolName)}""\s*\)", source);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Slogs", "Program.cs")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Slogs repository root.");
    }
}
