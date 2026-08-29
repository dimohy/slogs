using Slogs.Data;
using System.Text.Json;
using Xunit;

namespace Slogs.Tests;

public sealed class LlmWikiSemanticGraphValidatorTests
{
    [Fact]
    [Trait("Category", "SemanticCorpus")]
    public void Generated_manifest_matches_the_frozen_private_corpus()
    {
        var manifestPath = Environment.GetEnvironmentVariable("SLOGS_SEMANTIC_MANIFEST");
        var corpusDirectory = Environment.GetEnvironmentVariable("SLOGS_SEMANTIC_CORPUS");
        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(corpusDirectory))
        {
            return;
        }

        var manifest = JsonSerializer.Deserialize<LlmWikiSemanticGraphManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(manifest);
        using var corpusManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(corpusDirectory, "corpus-manifest.json")));
        var corpusSha256 = corpusManifest.RootElement.GetProperty("corpusSha256").GetString();
        Assert.False(string.IsNullOrWhiteSpace(corpusSha256));

        var entries = File.ReadLines(Path.Combine(corpusDirectory, "entries.jsonl"))
            .Select(ParseEntry)
            .ToDictionary(x => x.Id);
        var sources = File.ReadLines(Path.Combine(corpusDirectory, "sources.jsonl"))
            .Select(ParseSource)
            .ToDictionary(x => x.Id);

        var errors = LlmWikiSemanticGraphValidator.Validate(manifest, entries, sources, corpusSha256!);

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Take(20)));
    }

    [Fact]
    public void Evidence_backed_relation_and_split_pass()
    {
        var entryId = Guid.NewGuid();
        var secondEntryId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var entries = new Dictionary<Guid, LlmWikiSemanticCorpusEntry>
        {
            [entryId] = new(entryId, "owner", "Saul and Paul", "Alias", "faith/bible", "Saul was also called Paul.", "Paul continued the mission."),
            [secondEntryId] = new(secondEntryId, "owner", "Paul's letters", "Letters", "faith/bible", "Paul wrote letters.", "Letters document the mission.")
        };
        var sources = new Dictionary<Guid, LlmWikiSemanticCorpusSource>
        {
            [sourceId] = new(sourceId, entryId, "owner", "Original prompt about Saul and Paul.", null)
        };
        var manifest = new LlmWikiSemanticGraphManifest(
            1,
            "abc",
            "owner",
            "codex",
            "1",
            DateTimeOffset.UtcNow,
            [
                new("person:saul", "Saul", "person", "Earlier name used for Paul."),
                new("person:paul", "Paul", "person", "Apostle Paul.")
            ],
            [
                new("person:saul", entryId, null, "source-prompt", "Saul", 1.0),
                new("person:paul", entryId, null, "source-prompt", "Paul", 1.0),
                new("person:paul", secondEntryId, null, "source-prompt", "Paul", 1.0)
            ],
            [
                new("person:saul", "person:paul", "alias-of", 0.99,
                    [new(entryId, null, "source-prompt", "Saul was also called Paul")])
            ],
            [
                new(entryId, "Paul's mission", "faith/bible/paul", "Paul continued the mission.", "",
                    "The mission is independently recallable from the alias fact.",
                    [new(entryId, null, "content", "Paul continued the mission")])
            ]);

        var errors = LlmWikiSemanticGraphValidator.Validate(manifest, entries, sources, "abc");

        Assert.Empty(errors);
    }

    [Fact]
    public void Unverified_or_cross_owner_semantics_fail_closed()
    {
        var entryId = Guid.NewGuid();
        var entries = new Dictionary<Guid, LlmWikiSemanticCorpusEntry>
        {
            [entryId] = new(entryId, "other-owner", "Paul", "", "faith/bible", "Paul", "")
        };
        var manifest = new LlmWikiSemanticGraphManifest(
            1,
            "wrong",
            "owner",
            "codex",
            "1",
            DateTimeOffset.UtcNow,
            [new("person:paul", "Paul", "unknown-type", "")],
            [new("missing", entryId, null, "source-prompt", "not present", 1.2)],
            [new("person:paul", "person:paul", "invented-relation", 0.8, [])],
            [new(entryId, "", "faith/bible", "", "", "", [])]);

        var errors = LlmWikiSemanticGraphValidator.Validate(manifest, entries, new Dictionary<Guid, LlmWikiSemanticCorpusSource>(), "abc");

        Assert.Contains(errors, x => x.Contains("SHA-256", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("Unknown entity type", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("unknown entity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains("Confidence", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("same entity", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("requires evidence", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("cross-owner", StringComparison.Ordinal));
    }

    private static LlmWikiSemanticCorpusEntry ParseEntry(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        return new(
            root.GetProperty("id").GetGuid(),
            root.GetProperty("ownerUserName").GetString()!,
            root.GetProperty("title").GetString()!,
            root.GetProperty("summary").GetString()!,
            root.GetProperty("categoryPath").GetString()!,
            root.GetProperty("sourcePrompt").GetString()!,
            root.GetProperty("content").GetString()!);
    }

    private static LlmWikiSemanticCorpusSource ParseSource(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        return new(
            root.GetProperty("id").GetGuid(),
            root.GetProperty("entryId").GetGuid(),
            root.GetProperty("ownerUserName").GetString()!,
            root.GetProperty("prompt").GetString()!,
            root.GetProperty("content").ValueKind == JsonValueKind.Null
                ? null
                : root.GetProperty("content").GetString());
    }
}
