using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Slogs.Tests;

public sealed class SummaryRecallQualityContractTests
{
    [Fact]
    public void FrozenSummaryRecallContractHasRequiredCasesAndStableHash()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "slogs-summary-recall-quality.v1.json");
        var expectedHashPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "slogs-summary-recall-quality.v1.sha256");
        var bytes = File.ReadAllBytes(fixturePath);
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var expectedHash = File.ReadAllText(expectedHashPath).Trim();

        Assert.Equal(expectedHash, actualHash);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.True(root.GetProperty("frozenBeforeImplementation").GetBoolean());
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(4, cases.Length);
        Assert.Contains(cases, value => value.GetProperty("id").GetString() == "summary-style-personal-memory-only");
        Assert.Contains(cases, value => value.GetProperty("id").GetString() == "exact-passage-minimal-evidence");
        Assert.Contains(cases, value => value.GetProperty("id").GetString() == "one-relation-bridge");
        Assert.Contains(cases, value => value.GetProperty("id").GetString() == "negative-control-ordinary-memory");
        Assert.True(root.GetProperty("acceptance").GetProperty("allCasesMustPass").GetBoolean());
        Assert.Equal(0, root.GetProperty("acceptance").GetProperty("forbiddenBehaviorCount").GetInt32());
    }
}
