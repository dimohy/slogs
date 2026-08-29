using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Slogs.Data;

public sealed record BibleVerseCorpusInput(
    string Id,
    string Reference,
    string TranslationId,
    string Language,
    int BookNumber,
    string BookName,
    int Chapter,
    int Verse,
    string Text,
    string? ContinuationOf,
    string SourceId,
    string DistributionPolicy,
    string ContentHash);

public sealed record BibleEntityCorpusInput(
    string EntityId,
    string EntityType,
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    string? Description,
    IReadOnlyList<string> StrongIds,
    string SourceId);

public sealed record BibleRelationEvidenceInput(
    string SourceId,
    string Locator,
    string EvidenceType,
    string? Reference = null);

public sealed record BibleRelationCorpusInput(
    string RelationId,
    string FromNodeId,
    string RelationType,
    string ToNodeId,
    string ClaimClass,
    string ReviewStatus,
    double Confidence,
    IReadOnlyList<BibleRelationEvidenceInput> Evidence,
    string CreatedBy,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record BibleCorpusOptions(
    string CollectionId,
    string Version,
    string Title,
    string License,
    string SourceUri,
    string OwnerKind,
    string OwnerKey,
    string Visibility,
    string? ScopeKey,
    bool RedistributionAllowed,
    bool RequireContiguousVerses = false,
    IReadOnlyList<BibleDeclaredOmission>? DeclaredOmissions = null,
    KnowledgeChunkingOptions? Chunking = null,
    IReadOnlyList<KnowledgeAclGrantInput>? Acl = null);

public sealed record BibleDeclaredOmission(
    string TranslationId,
    string Reference,
    string Reason,
    string SourceUri);

public sealed record BibleCorpusPlan(
    KnowledgeCollectionInput Collection,
    IReadOnlyList<KnowledgeCorpusIngestRequest> Batches,
    IReadOnlyDictionary<string, string> PassageChunkIds);

public sealed partial class BibleKnowledgeCorpusAdapter(KnowledgeChunkingService chunker)
{
    private const string RestrictedDistribution = "restricted_no_public_redistribution";

    [GeneratedRegex(@"^(?<book>[1-3]?[A-Za-z]+)\.(?<chapter>[1-9][0-9]*)\.(?<verse>[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    public BibleCorpusPlan CreatePlan(
        BibleCorpusOptions options,
        IReadOnlyList<BibleVerseCorpusInput> verses,
        IReadOnlyList<BibleEntityCorpusInput>? entities = null,
        IReadOnlyList<BibleRelationCorpusInput>? relations = null)
    {
        ValidateOptions(options);
        var orderedVerses = ValidateVerses(verses, options.RequireContiguousVerses, options.DeclaredOmissions ?? []);
        ValidateDistribution(options, orderedVerses);

        var documents = CreateDocuments(orderedVerses, options.SourceUri);
        var structures = CreateStructures(orderedVerses);
        var chunks = CreateChunks(options, orderedVerses);
        var passageChunkIds = BuildPassageChunkIndex(chunks, orderedVerses);
        var mappedEntities = MapEntities(entities ?? []);
        var mappedRelations = MapRelations(relations ?? [], structures, mappedEntities, passageChunkIds);
        var passageRelations = CreatePassageContainmentRelations(orderedVerses, passageChunkIds);
        var allRelations = passageRelations.Concat(mappedRelations).ToArray();

        var first = orderedVerses[0];
        var collection = new KnowledgeCollectionInput(
            options.CollectionId,
            options.Version,
            options.Title,
            "bible",
            first.Language,
            options.License,
            options.SourceUri,
            options.OwnerKind,
            options.OwnerKey,
            options.Visibility,
            options.ScopeKey,
            options.RedistributionAllowed,
            chunks.Count);
        var batches = CreateBatches(collection, documents, structures, chunks, mappedEntities, allRelations, options.Acl);
        return new BibleCorpusPlan(collection, batches, passageChunkIds);
    }

    private static void ValidateOptions(BibleCorpusOptions options)
    {
        var required = new[]
        {
            options.CollectionId, options.Version, options.Title, options.License, options.SourceUri,
            options.OwnerKind, options.OwnerKey, options.Visibility
        };
        if (required.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("성경 코퍼스 옵션의 필수 값이 비어 있습니다.");
        }
    }

    private static BibleVerseCorpusInput[] ValidateVerses(
        IReadOnlyList<BibleVerseCorpusInput> verses,
        bool requireContiguousVerses,
        IReadOnlyList<BibleDeclaredOmission> declaredOmissions)
    {
        if (verses.Count == 0)
        {
            throw new InvalidDataException("성경 코퍼스에는 한 절 이상의 본문이 필요합니다.");
        }

        var translationIds = verses.Select(value => value.TranslationId).Distinct(StringComparer.Ordinal).ToArray();
        var languages = verses.Select(value => value.Language).Distinct(StringComparer.Ordinal).ToArray();
        if (translationIds.Length != 1 || languages.Length != 1)
        {
            throw new InvalidDataException("한 컬렉션 계획에는 하나의 번역본과 언어만 포함할 수 있습니다.");
        }

        if (verses.Select(value => value.Reference).Distinct(StringComparer.Ordinal).Count() != verses.Count
            || verses.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != verses.Count)
        {
            throw new InvalidDataException("구절 reference와 id는 컬렉션 안에서 고유해야 합니다.");
        }

        foreach (var value in verses)
        {
            var match = ReferencePattern().Match(value.Reference);
            if (!match.Success
                || int.Parse(match.Groups["chapter"].Value, System.Globalization.CultureInfo.InvariantCulture) != value.Chapter
                || int.Parse(match.Groups["verse"].Value, System.Globalization.CultureInfo.InvariantCulture) != value.Verse)
            {
                throw new InvalidDataException($"책/장/절 좌표가 reference와 일치하지 않습니다: {value.Reference}");
            }

            if (value.Id != $"verse:{value.TranslationId}:{value.Reference}")
            {
                throw new InvalidDataException($"구절 id가 번역본/reference 계약과 일치하지 않습니다: {value.Id}");
            }

            if (value.BookNumber is < 1 or > 66 || string.IsNullOrWhiteSpace(value.BookName)
                || string.IsNullOrWhiteSpace(value.Text) || string.IsNullOrWhiteSpace(value.SourceId)
                || string.IsNullOrWhiteSpace(value.DistributionPolicy))
            {
                throw new InvalidDataException($"구절 필수 필드가 유효하지 않습니다: {value.Reference}");
            }

            var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Text)));
            if (!actualHash.Equals(value.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"구절 contentHash가 본문과 일치하지 않습니다: {value.Reference}");
            }
        }

        var bookContracts = verses
            .GroupBy(value => ReferencePattern().Match(value.Reference).Groups["book"].Value, StringComparer.Ordinal)
            .Select(group => new
            {
                Code = group.Key,
                Numbers = group.Select(value => value.BookNumber).Distinct().ToArray(),
                Names = group.Select(value => value.BookName).Distinct(StringComparer.Ordinal).ToArray()
            });
        if (bookContracts.Any(book => book.Numbers.Length != 1 || book.Names.Length != 1))
        {
            throw new InvalidDataException("같은 책 코드는 하나의 bookNumber와 bookName에만 대응해야 합니다.");
        }

        var numberContracts = verses.GroupBy(value => value.BookNumber)
            .Select(group => group.Select(value => ReferencePattern().Match(value.Reference).Groups["book"].Value)
                .Distinct(StringComparer.Ordinal).Count());
        if (numberContracts.Any(count => count != 1))
        {
            throw new InvalidDataException("하나의 bookNumber가 여러 책 코드에 대응할 수 없습니다.");
        }

        var ordered = verses.OrderBy(value => value.BookNumber).ThenBy(value => value.Chapter).ThenBy(value => value.Verse).ToArray();
        if (requireContiguousVerses)
        {
            var usedOmissions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chapter in ordered.GroupBy(value => (value.BookNumber, value.Chapter)))
            {
                var actual = chapter.Select(value => value.Verse).ToArray();
                var first = chapter.First();
                var bookCode = GetBookCode(first.Reference);
                var missing = Enumerable.Range(1, actual[^1]).Except(actual).Select(number => $"{bookCode}.{first.Chapter}.{number}").ToArray();
                foreach (var reference in missing)
                {
                    var declaration = declaredOmissions.SingleOrDefault(item =>
                        item.TranslationId == first.TranslationId && item.Reference == reference);
                    if (declaration is null || string.IsNullOrWhiteSpace(declaration.Reason) || string.IsNullOrWhiteSpace(declaration.SourceUri))
                    {
                        throw new InvalidDataException($"장 안의 누락 절에 권위 근거가 없습니다: translation={first.TranslationId}, reference={reference}");
                    }

                    usedOmissions.Add($"{declaration.TranslationId}|{declaration.Reference}");
                }
            }

            var unused = declaredOmissions.Where(item => item.TranslationId == translationIds[0])
                .Where(item => !usedOmissions.Contains($"{item.TranslationId}|{item.Reference}"))
                .Select(item => item.Reference).ToArray();
            if (unused.Length > 0)
            {
                throw new InvalidDataException($"실제 누락과 일치하지 않는 선언된 절 예외가 있습니다: {string.Join(',', unused)}");
            }
        }

        return ordered;
    }

    private static void ValidateDistribution(BibleCorpusOptions options, IReadOnlyList<BibleVerseCorpusInput> verses)
    {
        var restricted = verses.Any(value => value.DistributionPolicy.Equals(RestrictedDistribution, StringComparison.OrdinalIgnoreCase));
        if (restricted && (options.RedistributionAllowed || options.Visibility.Equals("public_shared", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("공개 재배포가 제한된 번역 본문은 public_shared 또는 redistributionAllowed로 적재할 수 없습니다.");
        }
    }

    private static KnowledgeDocumentInput[] CreateDocuments(
        IReadOnlyList<BibleVerseCorpusInput> verses,
        string sourceUri)
        => verses.GroupBy(value => GetBookCode(value.Reference), StringComparer.Ordinal)
            .OrderBy(group => group.First().BookNumber)
            .Select(group =>
            {
                var first = group.First();
                return new KnowledgeDocumentInput(
                    $"document:book:{group.Key}",
                    first.BookName,
                    "bible_book",
                    first.BookNumber,
                    $"{sourceUri}#{group.Key}",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["bookCode"] = group.Key,
                        ["bookNumber"] = first.BookNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["translationId"] = first.TranslationId,
                        ["sourceId"] = first.SourceId
                    });
            }).ToArray();

    private static KnowledgeStructureInput[] CreateStructures(IReadOnlyList<BibleVerseCorpusInput> verses)
    {
        var result = new List<KnowledgeStructureInput>();
        foreach (var book in verses.GroupBy(value => GetBookCode(value.Reference), StringComparer.Ordinal)
                     .OrderBy(group => group.First().BookNumber))
        {
            var first = book.First();
            var documentId = $"document:book:{book.Key}";
            var bookNodeId = $"book:{book.Key}";
            result.Add(new KnowledgeStructureInput(
                bookNodeId, documentId, null, "book", first.BookName, first.BookNumber, book.Key,
                new Dictionary<string, string> { ["translationId"] = first.TranslationId }));
            foreach (var chapter in book.GroupBy(value => value.Chapter).OrderBy(group => group.Key))
            {
                var chapterNodeId = $"chapter:{book.Key}.{chapter.Key}";
                result.Add(new KnowledgeStructureInput(
                    chapterNodeId, documentId, bookNodeId, "chapter", $"{first.BookName} {chapter.Key}장", chapter.Key, $"{book.Key}.{chapter.Key}"));
                foreach (var verse in chapter.OrderBy(value => value.Verse))
                {
                    result.Add(new KnowledgeStructureInput(
                        $"passage:{verse.Reference}", documentId, chapterNodeId, "verse",
                        $"{first.BookName} {verse.Chapter}:{verse.Verse}", verse.Verse, verse.Reference,
                        new Dictionary<string, string>
                        {
                            ["translationId"] = verse.TranslationId,
                            ["contentHash"] = verse.ContentHash,
                            ["sourceId"] = verse.SourceId
                        }));
                }
            }
        }

        return result.ToArray();
    }

    private IReadOnlyList<KnowledgeChunkInput> CreateChunks(
        BibleCorpusOptions options,
        IReadOnlyList<BibleVerseCorpusInput> verses)
    {
        var result = new List<KnowledgeChunkInput>();
        foreach (var book in verses.GroupBy(value => GetBookCode(value.Reference), StringComparer.Ordinal)
                     .OrderBy(group => group.First().BookNumber))
        {
            var bookChunks = new List<KnowledgeChunkInput>();
            foreach (var chapter in book.GroupBy(value => value.Chapter).OrderBy(group => group.Key))
            {
                var first = chapter.First();
                var units = chapter.OrderBy(value => value.Verse).Select(value => new KnowledgeTextUnit(
                    $"passage:{value.Reference}",
                    value.Reference,
                    $"{value.BookName} {value.Chapter}:{value.Verse} {value.Text}",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["translationId"] = value.TranslationId,
                        ["bookCode"] = book.Key,
                        ["bookName"] = value.BookName,
                        ["chapter"] = value.Chapter.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["sourceId"] = value.SourceId,
                        ["distributionPolicy"] = value.DistributionPolicy
                    })).ToArray();
                bookChunks.AddRange(chunker.CreateChunks(
                    options.CollectionId,
                    options.Version,
                    $"document:book:{book.Key}",
                    $"chapter:{book.Key}.{first.Chapter}",
                    units,
                    options.Chunking));
            }

            for (var index = 0; index < bookChunks.Count; index++)
            {
                result.Add(bookChunks[index] with
                {
                    Ordinal = index,
                    PreviousChunkId = index == 0 ? null : bookChunks[index - 1].ChunkId,
                    NextChunkId = index == bookChunks.Count - 1 ? null : bookChunks[index + 1].ChunkId
                });
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildPassageChunkIndex(
        IReadOnlyList<KnowledgeChunkInput> chunks,
        IReadOnlyList<BibleVerseCorpusInput> verses)
    {
        var expected = verses.Select(value => $"passage:{value.Reference}").ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            foreach (var passageId in chunk.SearchAliases ?? [])
            {
                if (expected.Contains(passageId))
                {
                    result.TryAdd(passageId["passage:".Length..], chunk.ChunkId);
                }
            }
        }

        var missing = verses.Select(value => value.Reference).Where(reference => !result.ContainsKey(reference)).Take(5).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"구절을 포함하는 청크를 찾을 수 없습니다: {string.Join(',', missing)}");
        }

        return result;
    }

    private static KnowledgeEntityInput[] MapEntities(IReadOnlyList<BibleEntityCorpusInput> entities)
    {
        if (entities.Select(value => value.EntityId).Distinct(StringComparer.Ordinal).Count() != entities.Count)
        {
            throw new InvalidDataException("성경 엔터티 id는 고유해야 합니다.");
        }

        return entities.Select(value =>
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceId"] = value.SourceId
            };
            if (!string.IsNullOrWhiteSpace(value.Description))
            {
                metadata["description"] = value.Description;
            }

            if (value.StrongIds.Count > 0)
            {
                metadata["strongIds"] = string.Join(',', value.StrongIds);
            }

            return new KnowledgeEntityInput(
                value.EntityId, value.EntityType, value.CanonicalName, value.Aliases, metadata);
        }).ToArray();
    }

    private static KnowledgeRelationInput[] MapRelations(
        IReadOnlyList<BibleRelationCorpusInput> relations,
        IReadOnlyList<KnowledgeStructureInput> structures,
        IReadOnlyList<KnowledgeEntityInput> entities,
        IReadOnlyDictionary<string, string> passageChunkIds)
    {
        var validNodes = structures.Select(value => value.NodeId)
            .Concat(entities.Select(value => value.EntityId))
            .Concat(passageChunkIds.Values)
            .ToHashSet(StringComparer.Ordinal);
        if (relations.Select(value => value.RelationId).Distinct(StringComparer.Ordinal).Count() != relations.Count)
        {
            throw new InvalidDataException("성경 relation id는 고유해야 합니다.");
        }

        return relations.Select(value =>
        {
            if (!validNodes.Contains(value.FromNodeId) || !validNodes.Contains(value.ToNodeId))
            {
                throw new InvalidDataException($"관계 끝점이 현재 컬렉션에 없습니다: {value.RelationId}");
            }

            var endpointReferences = new[] { TryGetPassageReference(value.FromNodeId), TryGetPassageReference(value.ToNodeId) }
                .Where(reference => reference is not null).Cast<string>();
            var evidence = value.Evidence.Select(item =>
            {
                var references = new[] { item.Reference }.Where(reference => reference is not null).Cast<string>()
                    .Concat(endpointReferences)
                    .Distinct(StringComparer.Ordinal);
                var chunkIds = references.Where(passageChunkIds.ContainsKey).Select(reference => passageChunkIds[reference])
                    .Distinct(StringComparer.Ordinal).ToArray();
                return new KnowledgeEvidenceInput(item.SourceId, item.Locator, item.EvidenceType, chunkIds);
            }).ToArray();
            return new KnowledgeRelationInput(
                value.RelationId, value.FromNodeId, value.RelationType, value.ToNodeId, value.ClaimClass,
                value.ReviewStatus, value.Confidence, evidence, value.CreatedBy, value.Metadata);
        }).ToArray();
    }

    private static KnowledgeRelationInput[] CreatePassageContainmentRelations(
        IReadOnlyList<BibleVerseCorpusInput> verses,
        IReadOnlyDictionary<string, string> passageChunkIds)
        => verses.Select(value => new KnowledgeRelationInput(
            $"edge:bible:contains:{value.TranslationId}:{value.Reference}",
            passageChunkIds[value.Reference],
            "contains_passage",
            $"passage:{value.Reference}",
            "source_explicit",
            "approved",
            1.0,
            [new KnowledgeEvidenceInput(value.SourceId, value.Reference, "source_verse", [passageChunkIds[value.Reference]])],
            "deterministic_adapter",
            new Dictionary<string, string> { ["translationId"] = value.TranslationId })).ToArray();

    internal static IReadOnlyList<KnowledgeCorpusIngestRequest> CreateBatches(
        KnowledgeCollectionInput collection,
        IReadOnlyList<KnowledgeDocumentInput> documents,
        IReadOnlyList<KnowledgeStructureInput> structures,
        IReadOnlyList<KnowledgeChunkInput> chunks,
        IReadOnlyList<KnowledgeEntityInput> entities,
        IReadOnlyList<KnowledgeRelationInput> relations,
        IReadOnlyList<KnowledgeAclGrantInput>? acl)
    {
        var count = new[]
        {
            BatchCount(documents.Count, KnowledgeCorpusBatchLimits.Documents),
            BatchCount(structures.Count, KnowledgeCorpusBatchLimits.StructureNodes),
            BatchCount(chunks.Count, KnowledgeCorpusBatchLimits.Chunks),
            BatchCount(entities.Count, KnowledgeCorpusBatchLimits.Entities),
            BatchCount(relations.Count, KnowledgeCorpusBatchLimits.Relations)
        }.Max();
        var result = new List<KnowledgeCorpusIngestRequest>(count);
        for (var index = 0; index < count; index++)
        {
            result.Add(new KnowledgeCorpusIngestRequest(
                collection,
                Slice(documents, index, KnowledgeCorpusBatchLimits.Documents),
                Slice(structures, index, KnowledgeCorpusBatchLimits.StructureNodes),
                Slice(chunks, index, KnowledgeCorpusBatchLimits.Chunks),
                Slice(entities, index, KnowledgeCorpusBatchLimits.Entities),
                Slice(relations, index, KnowledgeCorpusBatchLimits.Relations),
                index == 0 ? acl : null,
                Activate: index == count - 1,
                RefreshContentHash: index == count - 1));
        }

        return result;
    }

    private static int BatchCount(int count, int size) => Math.Max(1, (count + size - 1) / size);

    private static IReadOnlyList<T> Slice<T>(IReadOnlyList<T> values, int index, int size)
        => values.Skip(index * size).Take(size).ToArray();

    private static string GetBookCode(string reference) => ReferencePattern().Match(reference).Groups["book"].Value;

    private static string? TryGetPassageReference(string nodeId)
        => nodeId.StartsWith("passage:", StringComparison.Ordinal) ? nodeId["passage:".Length..] : null;
}
