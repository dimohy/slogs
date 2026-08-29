using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BibleCorpusEvaluationRunnerTests
{
    [Fact]
    public void InterpretationSafetyDefaultsToAnswerEvaluationLayer()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """{"class":"interpretation_safety"}""");

        var layer = BibleCorpusEvaluationRunner.ReadEvaluationLayer(document.RootElement);

        Assert.Equal("answer", layer);
    }

    [Fact]
    public void ExplicitRetrievalLayerOverridesClassification()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """{"class":"interpretation_safety","evaluationLayer":"retrieval"}""");

        var layer = BibleCorpusEvaluationRunner.ReadEvaluationLayer(document.RootElement);

        Assert.Equal("retrieval", layer);
    }

    [Fact]
    public void ScoreAcceptsTraceableEvidenceAndRequiredRelation()
    {
        var evaluationCase = new BibleCorpusEvaluationCase(
            "identity-paul-saul",
            "사도 바울과 다소 사람 사울은 같은 사람인가?",
            ["Acts.13.9", "G3972G", "G4569G"],
            ["H7586G"],
            [],
            "same_as",
            "text_explicit");
        var recalled = new[]
        {
            Recall(new KnowledgeRelationRecall(
                "bible-reviewed-relations",
                "0.1.0",
                "same_as",
                "entity:step:G4569G",
                "entity:step:G3972G",
                "text_explicit",
                1,
                [new("source:Acts", "Acts.13.9", "verse")]))
        };

        var result = BibleCorpusEvaluationRunner.Score(evaluationCase, recalled);

        Assert.True(result.Passed);
        Assert.Empty(result.MissingEvidence);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void ScoreRejectsKingSaulMergedWithPaul()
    {
        var evaluationCase = new BibleCorpusEvaluationCase(
            "identity-king-saul-negative",
            "사울 왕과 사도 바울은 같은 사람인가?",
            ["H7586G", "G3972G"],
            [],
            ["same_entity"],
            null,
            "text_explicit");
        var recalled = new[]
        {
            Recall(new KnowledgeRelationRecall(
                "bible-reviewed-relations",
                "0.1.0",
                "same_as",
                "entity:step:H7586G",
                "entity:step:G3972G",
                "text_explicit",
                1,
                [new("source:invalid", "Acts.13.9", "verse")]))
        };

        var result = BibleCorpusEvaluationRunner.Score(evaluationCase, recalled);

        Assert.False(result.Passed);
        Assert.Contains("forbidden claim emitted: same_entity", result.Violations);
    }

    [Fact]
    public void ScoreAndMcpRecallExposeEndToEndProvenance()
    {
        var relation = new KnowledgeRelationRecall(
            "bible-original-step",
            "0.1.0",
            "contains_passage",
            "chunk:Acts.13.9",
            "passage:Acts.13.9",
            "source_explicit",
            1,
            [new("step-tagnt", "Acts.13.9", "original_tokens")]);
        var recalled = Recall(relation);
        var evaluationCase = new BibleCorpusEvaluationCase(
            "original-provenance",
            "사도행전 13장 9절 원문 근거",
            ["CC BY", "urn:slogs:bible-package:", "#Acts", "step-tagnt", "Acts.13.9"],
            [],
            [],
            "contains_passage",
            "source_explicit");

        var result = BibleCorpusEvaluationRunner.Score(evaluationCase, [recalled]);
        var markdown = KnowledgeCorpusMcpTools.FormatRecall([recalled]);

        Assert.True(result.Passed);
        Assert.Contains("- license: CC BY 4.0", markdown);
        Assert.Contains("- collectionSource: urn:slogs:bible-package:test:scholarly", markdown);
        Assert.Contains("- documentSource: urn:slogs:bible-package:test:scholarly#Acts", markdown);
        Assert.Contains("- evidence: step-tagnt @ Acts.13.9 (original_tokens)", markdown);
    }

    [Fact]
    public void StructuredRelationRequirementCannotCombineEvidenceFromDifferentRelations()
    {
        var requirement = new BibleCorpusEvaluationRelationRequirement(
            "mentions",
            "bible-original-step",
            "passage:Acts.13.9",
            "entity:step:G3972G",
            null,
            null,
            "text_explicit",
            ["step-tagnt"],
            ["Acts.13.9"]);
        var evaluationCase = new BibleCorpusEvaluationCase(
            "coupled-relation-evidence",
            "사울과 바울 관계",
            [],
            [],
            [],
            null,
            null,
            RequiredRelations: [requirement]);
        var wrongSource = new KnowledgeRelationRecall(
            "bible-original-step",
            "0.1.0",
            "mentions",
            "passage:Acts.13.9",
            "entity:step:G3972G",
            "text_explicit",
            1,
            [new("unrelated-source", "Acts.13.9", "original_token")]);
        var sourceOnDifferentRelation = new KnowledgeRelationRecall(
            "bible-original-step",
            "0.1.0",
            "contains_passage",
            "chunk:Acts.13.9",
            "passage:Acts.13.9",
            "source_explicit",
            1,
            [new("step-tagnt", "Acts.13.9", "original_tokens")]);

        var result = BibleCorpusEvaluationRunner.Score(
            evaluationCase,
            [Recall(wrongSource, sourceOnDifferentRelation)]);

        Assert.False(result.Passed);
        Assert.Contains(result.Violations, value => value.StartsWith(
            "missing required relation evidence:", StringComparison.Ordinal));
    }

    [Fact]
    public void StructuredRelationRequirementsParseFromFrozenFixtureShape()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """
            {
              "requiredRelations": [{
                "collectionId": "bible-original-step",
                "relationType": "mentions",
                "fromNodeId": "passage:Acts.13.9",
                "toNodeId": "entity:step:G3972G",
                "claimClass": "text_explicit",
                "evidenceSourceIds": ["step-tagnt"],
                "evidenceLocators": ["Acts.13.9"]
              }]
            }
            """);

        var requirement = Assert.Single(
            BibleCorpusEvaluationRunner.ReadRelationRequirements(document.RootElement));

        Assert.Equal("bible-original-step", requirement.CollectionId);
        Assert.Equal("mentions", requirement.RelationType);
        Assert.Equal("passage:Acts.13.9", requirement.FromNodeId);
        Assert.Equal("entity:step:G3972G", requirement.ToNodeId);
        Assert.Equal("text_explicit", requirement.ClaimClass);
        Assert.Equal(["step-tagnt"], requirement.EvidenceSourceIds);
        Assert.Equal(["Acts.13.9"], requirement.EvidenceLocators);
    }

    [Fact]
    public void ForbiddenStructuredRelationRejectsWrongNamesakeMention()
    {
        var forbidden = new BibleCorpusEvaluationRelationRequirement(
            "mentions",
            "bible-original-step",
            "passage:Acts.9.19",
            "entity:step:H7586G",
            null,
            null,
            "text_explicit",
            ["step-tagnt"],
            ["Acts.9.19"]);
        var evaluationCase = new BibleCorpusEvaluationCase(
            "apostle-saul-not-king-saul",
            "사도행전 9장 19절의 사울은 누구인가?",
            [],
            [],
            [],
            null,
            null,
            ForbiddenRelations: [forbidden]);
        var wrongMention = new KnowledgeRelationRecall(
            "bible-original-step",
            "0.1.0",
            "mentions",
            "passage:Acts.9.19",
            "entity:step:H7586G",
            "text_explicit",
            1,
            [new("step-tagnt", "Acts.9.19", "original_token")]);

        var result = BibleCorpusEvaluationRunner.Score(evaluationCase, [Recall(wrongMention)]);

        Assert.False(result.Passed);
        Assert.Contains(result.Violations, value => value.StartsWith(
            "forbidden relation evidence:", StringComparison.Ordinal));
    }

    [Fact]
    public void ForbiddenRelationRequirementsParseFromFrozenFixtureShape()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """
            {
              "forbiddenRelations": [{
                "collectionId": "bible-original-step",
                "relationType": "mentions",
                "fromNodeId": "passage:Acts.9.26",
                "toNodeId": "entity:step:H7586G",
                "claimClass": "text_explicit",
                "evidenceSourceIds": ["step-tagnt"],
                "evidenceLocators": ["Acts.9.26"]
              }]
            }
            """);

        var requirement = Assert.Single(
            BibleCorpusEvaluationRunner.ReadRelationRequirements(
                document.RootElement,
                "forbiddenRelations"));

        Assert.Equal("passage:Acts.9.26", requirement.FromNodeId);
        Assert.Equal("entity:step:H7586G", requirement.ToNodeId);
    }

    private static KnowledgeChunkRecall Recall(params KnowledgeRelationRecall[] relations)
        => new(
            "bible-original",
            "0.1.0",
            "bible",
            "document:Acts",
            "Acts",
            "chunk:Acts.13.9",
            "Acts.13.9 G4569G G3972G H7586G",
            "Acts.13.9",
            "Acts.13.9",
            100,
            relations,
            "CC BY 4.0",
            "urn:slogs:bible-package:test:scholarly",
            "urn:slogs:bible-package:test:scholarly#Acts");
}
