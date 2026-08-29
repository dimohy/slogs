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
            relations);
}
