using System.Text.RegularExpressions;

namespace Slogs.Data;

public sealed record BibleOriginalCorpusOptions(
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
    bool RequireAllBooks = true,
    KnowledgeChunkingOptions? Chunking = null,
    IReadOnlyList<KnowledgeAclGrantInput>? Acl = null);

public sealed partial class BibleOriginalKnowledgeCorpusAdapter(KnowledgeChunkingService chunker)
{
    [GeneratedRegex(@"^(?<book>[1-3]?[A-Za-z]+)\.(?<chapter>[1-9][0-9]*)\.(?<verse>[0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    public BibleCorpusPlan CreatePlan(
        BibleOriginalCorpusOptions options,
        IReadOnlyList<BibleVerseCorpusInput> coordinateVerses,
        IReadOnlyList<BibleOriginalTokenCorpusInput> originalTokens,
        IReadOnlyList<BibleEntityCorpusInput> entities,
        IReadOnlyList<BibleGraphEdgeCorpusInput> edges)
    {
        ValidateOptions(options);
        var books = BuildBookCatalog(coordinateVerses, options.RequireAllBooks);
        var tokens = ValidateTokens(originalTokens, books);
        var references = CollectReferences(coordinateVerses, tokens, edges, books);
        var documents = CreateDocuments(options.SourceUri, books);
        var structures = CreateStructures(references, books, edges);
        var chunks = CreateChunks(options, tokens, books);
        var tokenChunkIds = BuildTokenChunkIndex(chunks, tokens);
        var passageChunkIds = BuildPassageChunkIndex(tokens, tokenChunkIds);
        var mappedEntities = MapEntities(entities);
        var mappedEdges = MapEdges(edges, structures, mappedEntities, tokenChunkIds, passageChunkIds);
        var containment = CreateContainmentRelations(tokens, tokenChunkIds);
        var allRelations = containment.Concat(mappedEdges).ToArray();
        var collection = new KnowledgeCollectionInput(
            options.CollectionId,
            options.Version,
            options.Title,
            "bible-original",
            "hbo+grc",
            options.License,
            options.SourceUri,
            options.OwnerKind,
            options.OwnerKey,
            options.Visibility,
            options.ScopeKey,
            options.RedistributionAllowed,
            chunks.Count);
        var batches = BibleKnowledgeCorpusAdapter.CreateBatches(
            collection, documents, structures, chunks, mappedEntities, allRelations, options.Acl);
        var firstChunks = passageChunkIds.ToDictionary(
            pair => pair.Key, pair => pair.Value[0], StringComparer.Ordinal);
        return new BibleCorpusPlan(collection, batches, firstChunks);
    }

    private static void ValidateOptions(BibleOriginalCorpusOptions options)
    {
        if (new[]
            {
                options.CollectionId, options.Version, options.Title, options.License, options.SourceUri,
                options.OwnerKind, options.OwnerKey, options.Visibility
            }.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("원문 코퍼스 옵션의 필수 값이 비어 있습니다.");
        }

        if (options.Visibility == "public_shared" && !options.RedistributionAllowed)
        {
            throw new InvalidDataException("공용 원문 코퍼스는 재배포가 허용된 출처만 사용할 수 있습니다.");
        }

        if (options.Chunking?.OverlapUnits > 0)
        {
            throw new InvalidDataException("원문 토큰의 단일 청크 귀속을 위해 overlapUnits는 0이어야 합니다.");
        }
    }

    private static IReadOnlyDictionary<string, BibleBook> BuildBookCatalog(
        IReadOnlyList<BibleVerseCorpusInput> coordinateVerses,
        bool requireAllBooks)
    {
        if (coordinateVerses.Count == 0)
        {
            throw new InvalidDataException("원문 코퍼스의 책 좌표 골격이 필요합니다.");
        }

        var books = coordinateVerses.GroupBy(value => GetBookCode(value.Reference), StringComparer.Ordinal)
            .Select(group =>
            {
                var numbers = group.Select(value => value.BookNumber).Distinct().ToArray();
                var names = group.Select(value => value.BookName).Distinct(StringComparer.Ordinal).ToArray();
                if (numbers.Length != 1 || names.Length != 1)
                {
                    throw new InvalidDataException($"책 좌표 골격이 일관되지 않습니다: {group.Key}");
                }

                return new BibleBook(group.Key, numbers[0], names[0]);
            }).ToArray();
        if (books.Select(value => value.Number).Distinct().Count() != books.Length
            || (requireAllBooks && books.Length != 66))
        {
            throw new InvalidDataException($"원문 코퍼스의 책 좌표 골격은 66권이어야 합니다: actual={books.Length}");
        }

        return books.ToDictionary(value => value.Code, StringComparer.Ordinal);
    }

    private static BibleOriginalTokenCorpusInput[] ValidateTokens(
        IReadOnlyList<BibleOriginalTokenCorpusInput> tokens,
        IReadOnlyDictionary<string, BibleBook> books)
    {
        if (tokens.Count == 0 || tokens.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != tokens.Count)
        {
            throw new InvalidDataException("원문 토큰은 비어 있지 않고 id가 고유해야 합니다.");
        }

        foreach (var token in tokens)
        {
            var parsed = ParseReference(token.Reference);
            if (!books.ContainsKey(parsed.BookCode) || token.Position <= 0
                || string.IsNullOrWhiteSpace(token.Language)
                || (!token.IsOmitted && (string.IsNullOrWhiteSpace(token.Surface) || string.IsNullOrWhiteSpace(token.Lemma)))
                || string.IsNullOrWhiteSpace(token.SourceId)
                || !token.Id.StartsWith($"token:{token.Reference}:", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"원문 토큰 계약이 유효하지 않습니다: {token.Id}");
            }
        }

        return tokens.OrderBy(value => books[ParseReference(value.Reference).BookCode].Number)
            .ThenBy(value => ParseReference(value.Reference).Chapter)
            .ThenBy(value => ParseReference(value.Reference).Verse)
            .ThenBy(value => value.Position)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static BibleReference[] CollectReferences(
        IReadOnlyList<BibleVerseCorpusInput> coordinateVerses,
        IReadOnlyList<BibleOriginalTokenCorpusInput> tokens,
        IReadOnlyList<BibleGraphEdgeCorpusInput> edges,
        IReadOnlyDictionary<string, BibleBook> books)
    {
        var values = coordinateVerses.Select(value => value.Reference)
            .Concat(tokens.Select(value => value.Reference))
            .Concat(edges.SelectMany(value => PassageEndpointReferences(value.From).Concat(PassageEndpointReferences(value.To))))
            .Distinct(StringComparer.Ordinal)
            .Select(ParseReference)
            .ToArray();
        var unknown = values.Where(value => !books.ContainsKey(value.BookCode)).Select(value => value.BookCode).Distinct().ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidDataException($"알 수 없는 책 코드가 관계/원문 좌표에 있습니다: {string.Join(',', unknown)}");
        }

        return values.OrderBy(value => books[value.BookCode].Number)
            .ThenBy(value => value.Chapter).ThenBy(value => value.Verse).ToArray();
    }

    private static KnowledgeDocumentInput[] CreateDocuments(
        string sourceUri,
        IReadOnlyDictionary<string, BibleBook> books)
        => books.Values.OrderBy(value => value.Number).Select(book => new KnowledgeDocumentInput(
            $"document:book:{book.Code}", book.Name, "bible_book", book.Number, $"{sourceUri}#{book.Code}",
            new Dictionary<string, string>
            {
                ["bookCode"] = book.Code,
                ["bookNumber"] = book.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["layer"] = "original-language"
            })).ToArray();

    private static KnowledgeStructureInput[] CreateStructures(
        IReadOnlyList<BibleReference> references,
        IReadOnlyDictionary<string, BibleBook> books,
        IReadOnlyList<BibleGraphEdgeCorpusInput> edges)
    {
        var result = new List<KnowledgeStructureInput>();
        foreach (var bookGroup in references.GroupBy(value => value.BookCode, StringComparer.Ordinal)
                     .OrderBy(group => books[group.Key].Number))
        {
            var book = books[bookGroup.Key];
            var documentId = $"document:book:{book.Code}";
            var bookNode = $"book:{book.Code}";
            result.Add(new KnowledgeStructureInput(
                bookNode, documentId, null, "book", book.Name, book.Number, book.Code,
                new Dictionary<string, string> { ["layer"] = "original-language" }));
            foreach (var chapter in bookGroup.GroupBy(value => value.Chapter).OrderBy(group => group.Key))
            {
                var chapterNode = $"chapter:{book.Code}.{chapter.Key}";
                result.Add(new KnowledgeStructureInput(
                    chapterNode, documentId, bookNode, "chapter", $"{book.Name} {chapter.Key}장",
                    chapter.Key, $"{book.Code}.{chapter.Key}"));
                foreach (var reference in chapter.OrderBy(value => value.Verse))
                {
                    result.Add(new KnowledgeStructureInput(
                        $"passage:{reference.Value}", documentId, chapterNode, "verse",
                        $"{book.Name} {reference.Chapter}:{reference.Verse}", reference.Verse, reference.Value,
                        new Dictionary<string, string> { ["layer"] = "original-language" }));
                }
            }
        }

        var ranges = edges.SelectMany(value => new[] { PassageReference(value.From), PassageReference(value.To) })
            .Where(value => value?.Contains('-', StringComparison.Ordinal) is true && !value.Contains(',', StringComparison.Ordinal))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(ParseRange)
            .OrderBy(value => books[value.Start.BookCode].Number)
            .ThenBy(value => value.Start.Chapter)
            .ThenBy(value => value.Start.Verse)
            .ThenBy(value => value.End.BookCode, StringComparer.Ordinal)
            .ThenBy(value => value.End.Chapter)
            .ThenBy(value => value.End.Verse);
        foreach (var range in ranges)
        {
            var startBook = books[range.Start.BookCode];
            var sameChapter = range.Start.BookCode == range.End.BookCode && range.Start.Chapter == range.End.Chapter;
            result.Add(new KnowledgeStructureInput(
                $"passage:{range.Value}",
                $"document:book:{range.Start.BookCode}",
                sameChapter ? $"chapter:{range.Start.BookCode}.{range.Start.Chapter}" : $"book:{range.Start.BookCode}",
                "passage_range",
                $"{startBook.Name} {range.Start.Chapter}:{range.Start.Verse}-{range.End.Value}",
                range.Start.Verse,
                range.Value,
                new Dictionary<string, string>
                {
                    ["startReference"] = range.Start.Value,
                    ["endReference"] = range.End.Value,
                    ["layer"] = "original-language"
                }));
        }

        var sets = edges.SelectMany(value => new[] { PassageReference(value.From), PassageReference(value.To) })
            .Where(value => value?.Contains(',', StringComparison.Ordinal) is true)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(value => new BibleSet(
                value,
                value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .SelectMany(ExpandReferenceExpression).ToArray()))
            .OrderBy(value => books[value.Members[0].BookCode].Number)
            .ThenBy(value => value.Members[0].Chapter)
            .ThenBy(value => value.Members[0].Verse);
        foreach (var set in sets)
        {
            var start = set.Members[0];
            var startBook = books[start.BookCode];
            var sameChapter = set.Members.All(value => value.BookCode == start.BookCode && value.Chapter == start.Chapter);
            result.Add(new KnowledgeStructureInput(
                $"passage:{set.Value}",
                $"document:book:{start.BookCode}",
                sameChapter ? $"chapter:{start.BookCode}.{start.Chapter}" : $"book:{start.BookCode}",
                "passage_set",
                $"{startBook.Name} 복수 구절: {set.Value}",
                start.Verse,
                set.Value,
                new Dictionary<string, string>
                {
                    ["memberReferences"] = string.Join(',', set.Members.Select(value => value.Value)),
                    ["memberCount"] = set.Members.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["layer"] = "original-language"
                }));
        }

        return result.ToArray();
    }

    private IReadOnlyList<KnowledgeChunkInput> CreateChunks(
        BibleOriginalCorpusOptions options,
        IReadOnlyList<BibleOriginalTokenCorpusInput> tokens,
        IReadOnlyDictionary<string, BibleBook> books)
    {
        var result = new List<KnowledgeChunkInput>();
        foreach (var bookGroup in tokens.GroupBy(value => ParseReference(value.Reference).BookCode, StringComparer.Ordinal)
                     .OrderBy(group => books[group.Key].Number))
        {
            var bookChunks = new List<KnowledgeChunkInput>();
            foreach (var chapter in bookGroup.GroupBy(value => ParseReference(value.Reference).Chapter).OrderBy(group => group.Key))
            {
                var units = chapter.Select(token => new KnowledgeTextUnit(
                    token.Id,
                    token.Id,
                    FormatToken(token),
                    Metadata: new Dictionary<string, string>
                    {
                        ["bookCode"] = bookGroup.Key,
                        ["chapter"] = chapter.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["layer"] = "original-language"
                    })).ToArray();
                bookChunks.AddRange(chunker.CreateChunks(
                    options.CollectionId, options.Version, $"document:book:{bookGroup.Key}",
                    $"chapter:{bookGroup.Key}.{chapter.Key}", units,
                    options.Chunking ?? new KnowledgeChunkingOptions(OverlapUnits: 0)));
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

    private static string FormatToken(BibleOriginalTokenCorpusInput token)
        => token.IsOmitted
            ? $"{token.Reference} [omitted] | tradition={token.TextTradition} | source={token.SourceId}"
            : $"{token.Reference} {token.Surface} | lemma={token.Lemma} | transliteration={token.Transliteration} | strong={token.Strong} | morphology={token.Morphology} | gloss={token.Gloss} | lemmaGloss={token.LemmaGloss} | tradition={token.TextTradition}";

    private static IReadOnlyDictionary<string, string> BuildTokenChunkIndex(
        IReadOnlyList<KnowledgeChunkInput> chunks,
        IReadOnlyList<BibleOriginalTokenCorpusInput> tokens)
    {
        var expected = tokens.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            foreach (var tokenId in chunk.SearchAliases ?? [])
            {
                if (!expected.Contains(tokenId))
                {
                    continue;
                }

                if (!result.TryAdd(tokenId, chunk.ChunkId))
                {
                    throw new InvalidDataException($"원문 토큰이 여러 청크에 속합니다: {tokenId}");
                }
            }
        }

        var missing = expected.Except(result.Keys).Take(5).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"원문 토큰을 포함하는 청크가 없습니다: {string.Join(',', missing)}");
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildPassageChunkIndex(
        IReadOnlyList<BibleOriginalTokenCorpusInput> tokens,
        IReadOnlyDictionary<string, string> tokenChunkIds)
        => tokens.GroupBy(value => value.Reference, StringComparer.Ordinal).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<string>)group.Select(value => tokenChunkIds[value.Id]).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

    private static KnowledgeEntityInput[] MapEntities(IReadOnlyList<BibleEntityCorpusInput> entities)
    {
        if (entities.Select(value => value.EntityId).Distinct(StringComparer.Ordinal).Count() != entities.Count)
        {
            throw new InvalidDataException("원문 엔터티 id는 고유해야 합니다.");
        }

        return entities.Select(value =>
        {
            var metadata = new Dictionary<string, string> { ["sourceId"] = value.SourceId };
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

    private static KnowledgeRelationInput[] MapEdges(
        IReadOnlyList<BibleGraphEdgeCorpusInput> edges,
        IReadOnlyList<KnowledgeStructureInput> structures,
        IReadOnlyList<KnowledgeEntityInput> entities,
        IReadOnlyDictionary<string, string> tokenChunkIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> passageChunkIds)
    {
        if (edges.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != edges.Count)
        {
            throw new InvalidDataException("원문/관계 edge id는 고유해야 합니다.");
        }

        var validNodes = structures.Select(value => value.NodeId).Concat(entities.Select(value => value.EntityId))
            .ToHashSet(StringComparer.Ordinal);
        return edges.Select(edge =>
        {
            if (!validNodes.Contains(edge.From) || !validNodes.Contains(edge.To))
            {
                throw new InvalidDataException($"원문/관계 edge 끝점이 현재 컬렉션에 없습니다: {edge.Id}");
            }

            var endpointReferences = PassageEndpointReferences(edge.From).Concat(PassageEndpointReferences(edge.To));
            var evidence = edge.Evidence.Select(value =>
            {
                var chunkIds = (value.TokenIds ?? []).Where(tokenChunkIds.ContainsKey).Select(tokenId => tokenChunkIds[tokenId])
                    .Concat(passageChunkIds.TryGetValue(value.Locator, out var located) ? located : [])
                    .Concat(endpointReferences.Where(passageChunkIds.ContainsKey).SelectMany(reference => passageChunkIds[reference]))
                    .Distinct(StringComparer.Ordinal).ToArray();
                return new KnowledgeEvidenceInput(value.SourceId, value.Locator, value.EvidenceType, chunkIds);
            }).ToArray();
            var metadata = new Dictionary<string, string>
            {
                ["sourceVisibility"] = edge.Visibility
            };
            if (edge.SourceWeight is not null)
            {
                metadata["sourceWeight"] = edge.SourceWeight.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            foreach (var pair in edge.Metadata ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    throw new InvalidDataException($"관계 메타데이터가 유효하지 않습니다: {edge.Id}");
                }

                metadata[pair.Key] = pair.Value;
            }

            return new KnowledgeRelationInput(
                edge.Id, edge.From, edge.Relation, edge.To, edge.ClaimClass, edge.ReviewStatus,
                edge.Confidence, evidence, edge.CreatedBy, metadata);
        }).ToArray();
    }

    private static KnowledgeRelationInput[] CreateContainmentRelations(
        IReadOnlyList<BibleOriginalTokenCorpusInput> tokens,
        IReadOnlyDictionary<string, string> tokenChunkIds)
        => tokens.GroupBy(value => value.Reference, StringComparer.Ordinal).SelectMany(group =>
            group.GroupBy(value => tokenChunkIds[value.Id], StringComparer.Ordinal).Select(chunk =>
            {
                var first = chunk.First();
                return new KnowledgeRelationInput(
                    $"edge:bible-original:contains:{group.Key}:{ShortId(chunk.Key)}",
                    chunk.Key,
                    "contains_passage",
                    $"passage:{group.Key}",
                    "source_explicit",
                    "approved",
                    1.0,
                    [new KnowledgeEvidenceInput(first.SourceId, group.Key, "original_tokens", [chunk.Key])],
                    "deterministic_adapter");
            })).ToArray();

    private static string ShortId(string value)
        => value.Length <= 20 ? value : value[^20..];

    private static string GetBookCode(string reference) => ParseReference(reference).BookCode;

    private static BibleReference ParseReference(string value)
    {
        var match = ReferencePattern().Match(value);
        if (!match.Success)
        {
            throw new InvalidDataException($"잘못된 OSIS 구절 좌표입니다: {value}");
        }

        var bookCode = match.Groups["book"].Value;
        var verse = int.Parse(match.Groups["verse"].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (verse == 0 && bookCode != "Ps")
        {
            throw new InvalidDataException($"0절 좌표는 시편 표제에만 허용됩니다: {value}");
        }

        return new BibleReference(
            value,
            bookCode,
            int.Parse(match.Groups["chapter"].Value, System.Globalization.CultureInfo.InvariantCulture),
            verse);
    }

    private static string? PassageReference(string nodeId)
        => nodeId.StartsWith("passage:", StringComparison.Ordinal) ? nodeId["passage:".Length..] : null;

    private static IEnumerable<string> PassageEndpointReferences(string nodeId)
    {
        var value = PassageReference(nodeId);
        if (value is null)
        {
            return [];
        }

        if (value.Contains(',', StringComparison.Ordinal))
        {
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .SelectMany(ExpandReferenceExpression)
                .Select(reference => reference.Value);
        }

        return ExpandReferenceExpression(value).Select(reference => reference.Value);
    }

    private static IEnumerable<BibleReference> ExpandReferenceExpression(string value)
    {
        if (!value.Contains('-', StringComparison.Ordinal))
        {
            return [ParseReference(value)];
        }

        var range = ParseRange(value);
        return [range.Start, range.End];
    }

    private static BibleRange ParseRange(string value)
    {
        var separator = value.IndexOf('-', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1 || value.IndexOf('-', separator + 1) >= 0)
        {
            throw new InvalidDataException($"잘못된 OSIS 구절 범위입니다: {value}");
        }

        var start = ParseReference(value[..separator]);
        var end = ParseReference(value[(separator + 1)..]);
        return new BibleRange(value, start, end);
    }

    private sealed record BibleBook(string Code, int Number, string Name);
    private sealed record BibleReference(string Value, string BookCode, int Chapter, int Verse);
    private sealed record BibleRange(string Value, BibleReference Start, BibleReference End);
    private sealed record BibleSet(string Value, IReadOnlyList<BibleReference> Members);
}
