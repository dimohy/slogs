using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BibleOriginalKnowledgeCorpusAdapterTests
{
    [Fact]
    public void OriginalPlanKeepsTokenMorphologyEntityAliasesAndPassageGraphSeparateFromTranslationText()
    {
        var adapter = new BibleOriginalKnowledgeCorpusAdapter(new KnowledgeChunkingService());
        var coordinate = BibleKnowledgeCorpusAdapterTests.Verse(
            "Acts.13.9", 13, 9, "바울이라고 하는 사울") with
        {
            TranslationId = "ko-tkv",
            Id = "verse:ko-tkv:Acts.13.9"
        };
        var token = new BibleOriginalTokenCorpusInput(
            "token:Acts.13.9:001:K", "Acts.13.9", 1, "grc", "Σαῦλος", "Saulos", "Saul",
            "G4569", "N-NSM-P", "Σαῦλος", "Saul", false, "K", "step-tagnt", ["Paul@Acts.7.58"]);
        var entity = new BibleEntityCorpusInput(
            "entity:step:G3972G", "Male", "Paul", ["Paul", "Saul", "Παῦλος", "Σαῦλος"],
            "Apostle", ["G3972", "G4569"], "step-tipnr");
        var mention = new BibleGraphEdgeCorpusInput(
            "edge:mention:Acts.13.9:entity:step:G3972G", "passage:Acts.13.9", "mentions",
            "entity:step:G3972G", "text_explicit", "published", 1.0, "public_shared",
            [new BiblePackageEvidence("step-tagnt", "Acts.13.9", "original_token", [token.Id])],
            "deterministic_import");

        var plan = adapter.CreatePlan(Options(), [coordinate], [token], [entity], [mention]);

        Assert.Equal("bible-original", plan.Collection.Domain);
        Assert.Equal("CC BY 4.0", plan.Collection.License);
        var chunk = Assert.Single(plan.Batches.SelectMany(value => value.Chunks));
        Assert.Contains("lemma=Σαῦλος", chunk.Text);
        Assert.Contains("morphology=N-NSM-P", chunk.Text);
        Assert.DoesNotContain(coordinate.Text, chunk.Text);
        var mappedEntity = Assert.Single(plan.Batches.SelectMany(value => value.Entities));
        Assert.Contains("Saul", mappedEntity.Aliases!);
        var mappedMention = Assert.Single(plan.Batches.SelectMany(value => value.Relations), value => value.RelationId == mention.Id);
        Assert.Contains(chunk.ChunkId, mappedMention.Evidence.Single().ChunkIds!);
        Assert.Contains(plan.Batches.SelectMany(value => value.Relations), value =>
            value.RelationType == "contains_passage" && value.FromNodeId == chunk.ChunkId && value.ToNodeId == "passage:Acts.13.9");
    }

    [Fact]
    public void PublicOriginalPlanRequiresRedistributionAndRejectsOverlap()
    {
        var adapter = new BibleOriginalKnowledgeCorpusAdapter(new KnowledgeChunkingService());
        var coordinate = BibleKnowledgeCorpusAdapterTests.Verse("Acts.13.9", 13, 9, "fixture");
        var token = new BibleOriginalTokenCorpusInput(
            "token:Acts.13.9:001:K", "Acts.13.9", 1, "grc", "Σαῦλος", "Saulos", "Saul",
            "G4569", "N-NSM-P", "Σαῦλος", "Saul", false, "K", "step-tagnt", []);

        Assert.Contains("재배포", Assert.Throws<InvalidDataException>(() => adapter.CreatePlan(
            Options() with { RedistributionAllowed = false }, [coordinate], [token], [], [])).Message);
        Assert.Contains("overlapUnits", Assert.Throws<InvalidDataException>(() => adapter.CreatePlan(
            Options() with { Chunking = new KnowledgeChunkingOptions(OverlapUnits: 1) },
            [coordinate], [token], [], [])).Message);
    }

    [Fact]
    public async Task FullVerifiedOriginalTokensEntitiesAndMentionsProduceAPlanWhenExplicitlyEnabled()
    {
        var root = Environment.GetEnvironmentVariable("SLOGS_BIBLE_CORPUS_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var reader = new BibleCorpusPackageReader();
        var package = await reader.VerifyAsync(root);
        var coordinates = reader.ReadVerses(package, "ko-tkv");
        var tokens = reader.ReadOriginalTokens(package);
        var entities = reader.ReadEntities(package);
        var mentions = reader.ReadEntityMentions(package);
        var crossReferences = reader.ReadCrossReferences(package);
        var candidates = reader.ReadRelationCandidates(package);
        var adapter = new BibleOriginalKnowledgeCorpusAdapter(new KnowledgeChunkingService());
        var plan = adapter.CreatePlan(
            new BibleOriginalCorpusOptions(
                "bible-original-full-plan", package.Manifest.PackageVersion, "STEP 원문 전체 계획", "CC BY 4.0",
                "https://github.com/STEPBible/STEPBible-Data", "system", "slogs", "public_shared", null, true,
                Chunking: new KnowledgeChunkingOptions(OverlapUnits: 0)),
            coordinates, tokens, entities, mentions.Concat(crossReferences).Concat(candidates).ToArray());

        Assert.Equal(425_454, tokens.Count);
        Assert.Equal(4_259, plan.Batches.SelectMany(value => value.Entities).Count());
        Assert.Equal(38_343, plan.Batches.SelectMany(value => value.Relations).Count(value => value.RelationType == "mentions"));
        Assert.Equal(344_799, plan.Batches.SelectMany(value => value.Relations).Count(value => value.RelationType == "cross_reference"));
        Assert.Equal(5_297, plan.Batches.SelectMany(value => value.Relations).Count(value => value.ReviewStatus == "candidate"));
        Assert.All(plan.Batches.SelectMany(value => value.Relations).Where(value => value.ReviewStatus == "candidate"), value =>
        {
            Assert.Equal("true", value.Metadata!["requiresBiblicalValidation"]);
            Assert.Contains("denominational_doctrine", value.Metadata["prohibitedGrounds"]);
        });
        Assert.Equal(66, plan.Batches.SelectMany(value => value.Documents).Count());
        Assert.Equal(plan.Collection.ExpectedChunkCount, plan.Batches.SelectMany(value => value.Chunks).Count());
        Assert.True(plan.Batches[^1].Activate);
    }

    private static BibleOriginalCorpusOptions Options() => new(
        "bible-original-fixture", "1.0.0", "STEP 원문 fixture", "CC BY 4.0", "urn:test:step",
        "system", "slogs", "public_shared", null, true, RequireAllBooks: false,
        Chunking: new KnowledgeChunkingOptions(TargetTokens: 80, MaxTokens: 120, MinTokens: 1, OverlapUnits: 0));
}
