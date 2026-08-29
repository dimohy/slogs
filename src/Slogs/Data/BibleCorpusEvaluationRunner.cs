using System.Text;
using System.Text.Json;

namespace Slogs.Data;

public sealed record BibleCorpusEvaluationOptions(
    string EvaluationPath,
    string OutputPath,
    string OwnerUserName,
    int Limit = 10,
    int MaxGraphHops = 3);

public sealed record BibleCorpusEvaluationCase(
    string Id,
    string Query,
    IReadOnlyList<string> MustFind,
    IReadOnlyList<string> MustNotMerge,
    IReadOnlyList<string> MustNotClaim,
    string? RequiredRelation,
    string? RequiredClaimClass,
    string EvaluationLayer = "retrieval");

public sealed record BibleCorpusEvaluationCaseResult(
    string Id,
    string Query,
    bool Passed,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> Violations,
    IReadOnlyList<string> MatchedLocators,
    IReadOnlyList<string> MatchedRelations,
    int ResultCount);

public sealed class BibleCorpusEvaluationRunner(KnowledgeCorpusService corpusService)
{
    public async Task<IReadOnlyList<BibleCorpusEvaluationCaseResult>> RunAsync(
        BibleCorpusEvaluationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.EvaluationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OwnerUserName);
        if (options.Limit is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Evaluation limit must be between 1 and 10.");
        }
        if (options.MaxGraphHops is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Evaluation graph depth must be between 0 and 3.");
        }

        var cases = ReadCases(options.EvaluationPath);
        var results = new List<BibleCorpusEvaluationCaseResult>(cases.Count);
        var excludedAnswerCaseIds = new List<string>();
        var actor = KnowledgeCorpusActor.User(options.OwnerUserName, isAdmin: true);
        foreach (var evaluationCase in cases)
        {
            if (evaluationCase.EvaluationLayer.Equals("answer", StringComparison.Ordinal))
            {
                excludedAnswerCaseIds.Add(evaluationCase.Id);
                continue;
            }
            var recalled = await corpusService.RecallAsync(
                actor,
                evaluationCase.Query,
                options.Limit,
                options.MaxGraphHops,
                cancellationToken: cancellationToken);
            results.Add(Score(evaluationCase, recalled));
        }

        WriteResult(options, results, cases.Count, excludedAnswerCaseIds);
        return results;
    }

    public static BibleCorpusEvaluationCaseResult Score(
        BibleCorpusEvaluationCase evaluationCase,
        IReadOnlyList<KnowledgeChunkRecall> recalled)
    {
        var searchable = new StringBuilder();
        var locators = new SortedSet<string>(StringComparer.Ordinal);
        var relationTriples = new SortedSet<string>(StringComparer.Ordinal);
        var relations = recalled.SelectMany(result => result.Relations).ToArray();

        foreach (var result in recalled)
        {
            Append(searchable, result.CollectionId, result.Version, result.DocumentId, result.DocumentTitle,
                result.ChunkId, result.Text, result.StartLocator, result.EndLocator);
            locators.Add(result.StartLocator);
            locators.Add(result.EndLocator);
            foreach (var relation in result.Relations)
            {
                Append(searchable, relation.CollectionId, relation.Version, relation.RelationType,
                    relation.FromNodeId, relation.ToNodeId, relation.ClaimClass,
                    relation.FromLabel, relation.ToLabel);
                if (relation.FromAliases is not null)
                {
                    Append(searchable, [.. relation.FromAliases]);
                }
                if (relation.ToAliases is not null)
                {
                    Append(searchable, [.. relation.ToAliases]);
                }
                foreach (var evidence in relation.Evidence)
                {
                    Append(searchable, evidence.SourceId, evidence.Locator, evidence.EvidenceType);
                    locators.Add(evidence.Locator);
                    if (evidence.ChunkIds is not null)
                    {
                        Append(searchable, [.. evidence.ChunkIds]);
                    }
                }
                relationTriples.Add($"{relation.FromNodeId} --{relation.RelationType}--> {relation.ToNodeId} [{relation.ClaimClass}]");
            }
        }

        var searchableText = searchable.ToString();
        var missing = evaluationCase.MustFind
            .Where(expected => !searchableText.Contains(expected, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var violations = new List<string>();

        if (evaluationCase.RequiredRelation is not null
            && !relations.Any(value => value.RelationType.Equals(
                evaluationCase.RequiredRelation,
                StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add($"missing required relation: {evaluationCase.RequiredRelation}");
        }
        if (evaluationCase.RequiredClaimClass is not null
            && !relations.Any(value => value.ClaimClass.Equals(
                evaluationCase.RequiredClaimClass,
                StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add($"missing required claim class: {evaluationCase.RequiredClaimClass}");
        }
        foreach (var forbidden in evaluationCase.MustNotMerge)
        {
            if (relations.Any(value => IsSameEntity(value.RelationType)
                    && (Contains(value.FromNodeId, forbidden) || Contains(value.ToNodeId, forbidden))))
            {
                violations.Add($"forbidden entity merged: {forbidden}");
            }
        }
        if (evaluationCase.MustNotClaim.Any(IsSameEntity))
        {
            var namedEvidence = evaluationCase.MustFind;
            if (relations.Any(value => IsSameEntity(value.RelationType)
                    && namedEvidence.Any(left => Contains(value.FromNodeId, left) || Contains(value.ToNodeId, left))
                    && namedEvidence.Any(right => !right.Equals(namedEvidence.FirstOrDefault(), StringComparison.OrdinalIgnoreCase)
                        && (Contains(value.FromNodeId, right) || Contains(value.ToNodeId, right)))))
            {
                violations.Add("forbidden claim emitted: same_entity");
            }
        }

        return new(
            evaluationCase.Id,
            evaluationCase.Query,
            missing.Length == 0 && violations.Count == 0,
            missing,
            violations,
            [.. locators],
            [.. relationTriples],
            recalled.Count);
    }

    private static IReadOnlyList<BibleCorpusEvaluationCase> ReadCases(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var cases = new List<BibleCorpusEvaluationCase>();
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            cases.Add(new(
                item.GetProperty("id").GetString() ?? throw new InvalidDataException("Evaluation case id is missing."),
                item.GetProperty("query").GetString() ?? throw new InvalidDataException("Evaluation case query is missing."),
                ReadStrings(item, "mustFind", "mustFindEvidence"),
                ReadStrings(item, "mustNotMerge"),
                ReadStrings(item, "mustNotClaim", "answerMustNotClaim"),
                ReadOptionalString(item, "requiredRelation"),
                ReadOptionalString(item, "requiredClaimClass"),
                ReadEvaluationLayer(item)));
        }
        if (cases.Count == 0)
        {
            throw new InvalidDataException("Evaluation file has no cases.");
        }
        return cases;
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                return value.EnumerateArray()
                    .Select(item => item.GetString() ?? throw new InvalidDataException($"{name} contains a null value."))
                    .ToArray();
            }
        }
        return [];
    }

    private static string? ReadOptionalString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.GetString() : null;

    public static string ReadEvaluationLayer(JsonElement element)
    {
        if (element.TryGetProperty("evaluationLayer", out var configured))
        {
            var value = configured.GetString();
            if (value is not ("retrieval" or "answer"))
            {
                throw new InvalidDataException("evaluationLayer must be retrieval or answer.");
            }
            return value;
        }

        return element.TryGetProperty("class", out var classification)
            && classification.GetString() == "interpretation_safety"
                ? "answer"
                : "retrieval";
    }

    private static void WriteResult(
        BibleCorpusEvaluationOptions options,
        IReadOnlyList<BibleCorpusEvaluationCaseResult> results,
        int sourceCaseCount,
        IReadOnlyList<string> excludedAnswerCaseIds)
    {
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        Directory.CreateDirectory(outputDirectory!);
        using var stream = File.Create(options.OutputPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 2);
        writer.WriteString("evaluatedAtUtc", DateTimeOffset.UtcNow);
        writer.WriteString("evaluationPath", Path.GetFileName(options.EvaluationPath));
        writer.WriteString("ownerUserName", options.OwnerUserName);
        writer.WriteNumber("limit", options.Limit);
        writer.WriteNumber("maxGraphHops", options.MaxGraphHops);
        writer.WriteNumber("sourceCaseCount", sourceCaseCount);
        writer.WriteNumber("retrievalCaseCount", results.Count);
        writer.WriteNumber("answerCaseCount", excludedAnswerCaseIds.Count);
        writer.WriteNumber("passedCases", results.Count(value => value.Passed));
        writer.WriteNumber("totalCases", results.Count);
        writer.WriteBoolean("passed", results.All(value => value.Passed));
        WriteStrings(writer, "excludedAnswerCaseIds", excludedAnswerCaseIds);
        writer.WriteStartArray("cases");
        foreach (var result in results)
        {
            writer.WriteStartObject();
            writer.WriteString("id", result.Id);
            writer.WriteString("query", result.Query);
            writer.WriteBoolean("passed", result.Passed);
            writer.WriteNumber("resultCount", result.ResultCount);
            WriteStrings(writer, "missingEvidence", result.MissingEvidence);
            WriteStrings(writer, "violations", result.Violations);
            WriteStrings(writer, "matchedLocators", result.MatchedLocators);
            WriteStrings(writer, "matchedRelations", result.MatchedRelations);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string propertyName, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }

    private static void Append(StringBuilder builder, params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.Append(value).Append('\n');
            }
        }
    }

    private static bool Contains(string value, string expected)
        => value.Contains(expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameEntity(string value)
        => value.Equals("same_as", StringComparison.OrdinalIgnoreCase)
            || value.Equals("same_entity", StringComparison.OrdinalIgnoreCase);
}
