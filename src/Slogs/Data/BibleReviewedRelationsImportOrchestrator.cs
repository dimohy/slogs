using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Slogs.Data;

public sealed record BibleReviewedRelationsImportOptions(
    string PackageRoot,
    string CheckpointRoot,
    string OwnerUserName,
    bool VerifyOnly);

public sealed record BibleReviewedRelationsImportSummary(
    string PackageId,
    string PackageVersion,
    string PackageHash,
    bool VerifyOnly,
    BibleCorpusLayerImportResult Layer);

internal sealed record ReviewedRelationDecisionRecord(
    int SchemaVersion,
    string Id,
    string From,
    string Relation,
    string To,
    string Verdict,
    string ClaimClass,
    double Confidence,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> DecisionBasis,
    string Rationale,
    string ReviewedBy,
    DateTimeOffset ReviewedAt,
    IReadOnlyList<string> ProhibitedGroundsUsed);

public sealed class BibleReviewedRelationsImportOrchestrator(BibleCorpusImportRunner importRunner)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<BibleReviewedRelationsImportSummary> RunAsync(
        BibleReviewedRelationsImportOptions options,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(options.PackageRoot);
        var checkpointRoot = Path.GetFullPath(options.CheckpointRoot);
        var owner = Normalize(options.OwnerUserName, 80, "ownerUserName");
        var verified = await VerifyAsync(root, cancellationToken);
        var plan = CreatePlan(verified.Manifest, verified.PackageHash, verified.Decisions, verified.Relations);
        var planHash = BibleCorpusImportRunner.ComputePlanHash(plan);
        var state = "verified";
        if (!options.VerifyOnly)
        {
            Directory.CreateDirectory(checkpointRoot);
            var checkpoint = await importRunner.RunAsync(
                KnowledgeCorpusActor.User(owner, isAdmin: true),
                plan,
                verified.PackageHash,
                Path.Combine(checkpointRoot,
                    $"{plan.Collection.CollectionId}-{plan.Collection.Version}-{verified.PackageHash[..12]}.json"),
                cancellationToken);
            state = checkpoint.State;
        }

        var layer = new BibleCorpusLayerImportResult(
            plan.Collection.CollectionId,
            plan.Collection.Version,
            plan.Collection.Visibility,
            plan.Batches.Count,
            plan.Batches.Sum(value => value.Documents.Count),
            plan.Batches.Sum(value => value.Chunks.Count),
            plan.Batches.Sum(value => value.Entities.Count),
            plan.Batches.Sum(value => value.Relations.Count),
            planHash,
            state);
        return new BibleReviewedRelationsImportSummary(
            verified.Manifest.PackageId,
            verified.Manifest.PackageVersion,
            verified.PackageHash,
            options.VerifyOnly,
            layer);
    }

    private static async Task<VerifiedReviewedRelationsPackage> VerifyAsync(
        string root,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }
        var manifestPath = Path.Combine(root, "manifest.json");
        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<BibleCorpusPackageManifest>(
            manifestStream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("검토 관계 manifest.json을 읽을 수 없습니다.");
        if (manifest.SchemaVersion != 1
            || manifest.PackageId != "slogs-bible-agent-reviewed-relations"
            || manifest.Visibility != "public_shared"
            || manifest.CoordinateSystem != "OSIS"
            || manifest.Files.Count != 2)
        {
            throw new InvalidDataException("검토 관계 manifest 계약이 올바르지 않습니다.");
        }

        var expectedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "review-decisions.ndjson",
            "relations.ndjson"
        };
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = file.RelativePath.Replace('\\', '/');
            if (!expectedFiles.Remove(relative) || relative.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"검토 관계 패키지 파일 경로가 올바르지 않습니다: {relative}");
            }
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path)
                || new FileInfo(path).Length != file.Bytes)
            {
                throw new InvalidDataException($"검토 관계 패키지 파일 크기 또는 경로가 다릅니다: {relative}");
            }
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)
                || File.ReadLines(path).LongCount() != file.Records)
            {
                throw new InvalidDataException($"검토 관계 패키지 파일 무결성이 다릅니다: {relative}");
            }
        }
        if (expectedFiles.Count > 0)
        {
            throw new InvalidDataException($"검토 관계 패키지 파일이 누락됐습니다: {string.Join(',', expectedFiles)}");
        }

        var decisions = ReadNdjson<ReviewedRelationDecisionRecord>(Path.Combine(root, "review-decisions.ndjson"));
        var relations = ReadNdjson<BibleGraphEdgeCorpusInput>(Path.Combine(root, "relations.ndjson"));
        ValidateReview(decisions, relations);
        var identityLines = manifest.Files.OrderBy(value => value.RelativePath, StringComparer.Ordinal)
            .Select(value => $"{value.RelativePath}|{value.Records}|{value.Bytes}|{value.Sha256.ToUpperInvariant()}");
        var identity = string.Join('\n', new[]
        {
            $"manifest|{manifest.PackageId}|{manifest.PackageVersion}|{manifest.Visibility}|{manifest.CoordinateSystem}"
        }.Concat(identityLines));
        var packageHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new VerifiedReviewedRelationsPackage(manifest, packageHash, decisions, relations);
    }

    private static void ValidateReview(
        IReadOnlyList<ReviewedRelationDecisionRecord> decisions,
        IReadOnlyList<BibleGraphEdgeCorpusInput> relations)
    {
        if (decisions.Count == 0
            || decisions.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != decisions.Count
            || decisions.Any(value => value.SchemaVersion != 1
                || value.Verdict is not ("approved" or "rejected")
                || value.ProhibitedGroundsUsed.Count != 0
                || value.EvidenceReferences.Count == 0
                || value.DecisionBasis.Count == 0
                || value.Rationale.Trim().Length < 20
                || string.IsNullOrWhiteSpace(value.ReviewedBy)))
        {
            throw new InvalidDataException("검토 관계 결정 계약이 올바르지 않습니다.");
        }
        var approved = decisions.Where(value => value.Verdict == "approved")
            .ToDictionary(value => $"edge:reviewed:{value.Id["review:".Length..]}", StringComparer.Ordinal);
        var reviewedRelations = relations.Where(value => value.Id.StartsWith("edge:reviewed:", StringComparison.Ordinal)).ToArray();
        var rangeBridges = relations.Where(value => value.Id.StartsWith("edge:reviewed-range:", StringComparison.Ordinal)).ToArray();
        if (reviewedRelations.Length + rangeBridges.Length != relations.Count
            || approved.Count != reviewedRelations.Length
            || reviewedRelations.Any(relation => !approved.TryGetValue(relation.Id, out var decision)
                || relation.From != decision.From
                || relation.Relation != decision.Relation
                || relation.To != decision.To
                || relation.ClaimClass != decision.ClaimClass
                || relation.ReviewStatus != "approved"
                || relation.Visibility != "public_shared"
                || !relation.CreatedBy.StartsWith("agent_biblical_review:", StringComparison.Ordinal)
                || relation.Evidence.All(value => value.EvidenceType != "review_decision")))
        {
            throw new InvalidDataException("승인 결정과 게시 관계가 일대일로 일치하지 않습니다.");
        }
        var approvedRangeEndpoints = reviewedRelations
            .SelectMany(value => new[] { value.From, value.To })
            .Where(IsPassageRange)
            .ToHashSet(StringComparer.Ordinal);
        if (approvedRangeEndpoints.Count > 0
            && approvedRangeEndpoints.Any(endpoint => !rangeBridges.Any(value => value.From == endpoint)))
        {
            throw new InvalidDataException("승인 범위 관계에 개별 절 연결이 누락됐습니다.");
        }
        if (rangeBridges.Any(value => !approvedRangeEndpoints.Contains(value.From)
            || !value.To.StartsWith("passage:", StringComparison.Ordinal)
            || IsPassageRange(value.To)
            || value.Relation != "contains_passage"
            || value.ClaimClass != "source_explicit"
            || value.ReviewStatus != "published"
            || value.Visibility != "public_shared"
            || value.Confidence != 1
            || value.CreatedBy != "deterministic:osis-range-expansion-v1"
            || !value.Evidence.Any(evidence => evidence.SourceId == "scripture-coordinate"
                && evidence.Locator == value.To["passage:".Length..]
                && evidence.EvidenceType == "verse")
            || !value.Evidence.Any(evidence => evidence.SourceId == "agent-reviewed-range"
                && evidence.Locator == value.From
                && evidence.EvidenceType == "derived_range")))
        {
            throw new InvalidDataException("승인 범위의 결정론적 개별 절 연결 계약이 올바르지 않습니다.");
        }
    }

    private static bool IsPassageRange(string value)
        => value.StartsWith("passage:", StringComparison.Ordinal)
            && value["passage:".Length..].Contains("-", StringComparison.Ordinal);

    private static BibleCorpusPlan CreatePlan(
        BibleCorpusPackageManifest manifest,
        string packageHash,
        IReadOnlyList<ReviewedRelationDecisionRecord> decisions,
        IReadOnlyList<BibleGraphEdgeCorpusInput> relations)
    {
        const string documentId = "document:bible-reviewed-relations";
        const string rootNodeId = "reviewed-relations:root";
        const string chunkId = "chunk:bible-reviewed-relations:review-decisions";
        var endpoints = relations.SelectMany(value => new[] { value.From, value.To })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var structures = new List<KnowledgeStructureInput>
        {
            new(rootNodeId, documentId, null, "review_collection", "Agent 검토 성경 관계", 0, "reviewed-relations")
        };
        var entities = new List<KnowledgeEntityInput>();
        var ordinal = 1;
        foreach (var endpoint in endpoints)
        {
            if (endpoint.StartsWith("passage:", StringComparison.Ordinal))
            {
                var locator = endpoint["passage:".Length..];
                structures.Add(new KnowledgeStructureInput(
                    endpoint, documentId, rootNodeId, "passage_review_endpoint", locator, ordinal++, locator));
            }
            else
            {
                entities.Add(new KnowledgeEntityInput(endpoint, "review_endpoint", endpoint, [endpoint]));
            }
        }

        var text = string.Join('\n', relations.OrderBy(value => value.Id, StringComparer.Ordinal)
            .Select(value => $"{value.From} {value.Relation} {value.To} | {value.ClaimClass} | review={value.Id}"));
        var chunk = new KnowledgeChunkInput(
            chunkId,
            documentId,
            rootNodeId,
            0,
            text,
            decisions.Min(value => value.ReviewedAt).ToString("O"),
            decisions.Max(value => value.ReviewedAt).ToString("O"),
            null,
            null,
            0,
            KnowledgeChunkingService.CountTokens(text),
            "unicode-word-estimate-v1",
            endpoints.Concat(relations.Select(value => value.Relation)).Distinct(StringComparer.Ordinal).ToArray(),
            new Dictionary<string, string>
            {
                ["reviewedBy"] = string.Join(',', decisions.Select(value => value.ReviewedBy).Distinct(StringComparer.Ordinal)),
                ["packageHash"] = packageHash,
                ["containsRestrictedTranslationText"] = "false"
            });
        var mappedRelations = relations.Select(value => new KnowledgeRelationInput(
            value.Id,
            value.From,
            value.Relation,
            value.To,
            value.ClaimClass,
            value.ReviewStatus,
            value.Confidence,
            value.Evidence.Select(evidence => new KnowledgeEvidenceInput(
                evidence.SourceId, evidence.Locator, evidence.EvidenceType, null)).ToArray(),
            value.CreatedBy,
            value.Metadata)).ToArray();
        var collection = new KnowledgeCollectionInput(
            "bible-reviewed-relations",
            manifest.PackageVersion,
            "Agent가 성경 본문으로 검토한 관계",
            "bible-relation-review",
            "mul",
            "CC BY 4.0 review metadata; underlying source references retain their licenses",
            $"urn:slogs:bible-reviewed-relations:{packageHash.ToLowerInvariant()}",
            "system",
            "slogs",
            "public_shared",
            null,
            true,
            1);
        var batch = new KnowledgeCorpusIngestRequest(
            collection,
            [new KnowledgeDocumentInput(documentId, "성경 본문 기반 Agent 관계 검토", "review_ledger", 0,
                "reviews/bible-relation-reviews.v1.ndjson")],
            structures,
            [chunk],
            entities,
            mappedRelations,
            null,
            Activate: true);
        return new BibleCorpusPlan(collection, [batch], new Dictionary<string, string>());
    }

    private static IReadOnlyList<T> ReadNdjson<T>(string path)
    {
        var result = new List<T>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            try
            {
                result.Add(JsonSerializer.Deserialize<T>(line, JsonOptions)
                    ?? throw new InvalidDataException($"검토 관계 NDJSON null 레코드: {path}:{lineNumber}"));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"검토 관계 NDJSON 파싱 오류: {path}:{lineNumber}", exception);
            }
        }
        return result;
    }

    private static string Normalize(string value, int maximumLength, string field)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new InvalidDataException($"{field} 길이가 유효하지 않습니다: {normalized.Length}");
        }
        return normalized;
    }

    private sealed record VerifiedReviewedRelationsPackage(
        BibleCorpusPackageManifest Manifest,
        string PackageHash,
        IReadOnlyList<ReviewedRelationDecisionRecord> Decisions,
        IReadOnlyList<BibleGraphEdgeCorpusInput> Relations);
}
