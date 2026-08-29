namespace Slogs.Data;

public sealed record BibleCorpusImportOptions(
    string PackageRoot,
    string CheckpointRoot,
    string OwnerUserName,
    string Layer,
    bool VerifyOnly);

public sealed record BibleCorpusLayerImportResult(
    string CollectionId,
    string Version,
    string Visibility,
    int Batches,
    int Documents,
    int Chunks,
    int Entities,
    int Relations,
    string PlanHash,
    string State);

public sealed record BibleCorpusImportSummary(
    string PackageId,
    string PackageVersion,
    string PackageHash,
    bool VerifyOnly,
    IReadOnlyList<BibleCorpusLayerImportResult> Layers);

public sealed class BibleCorpusImportOrchestrator(
    BibleCorpusPackageReader packageReader,
    BibleKnowledgeCorpusAdapter translationAdapter,
    BibleOriginalKnowledgeCorpusAdapter originalAdapter,
    BibleCorpusImportRunner importRunner)
{
    private static readonly HashSet<string> AllowedLayers = new(StringComparer.Ordinal)
    {
        "all",
        "translations",
        "original"
    };

    public async Task<BibleCorpusImportSummary> RunAsync(
        BibleCorpusImportOptions options,
        CancellationToken cancellationToken = default)
    {
        var packageRoot = Path.GetFullPath(options.PackageRoot);
        var checkpointRoot = Path.GetFullPath(options.CheckpointRoot);
        var owner = Normalize(options.OwnerUserName, 80, "ownerUserName");
        var layer = Normalize(options.Layer, 24, "layer").ToLowerInvariant();
        if (!AllowedLayers.Contains(layer))
        {
            throw new InvalidDataException($"지원하지 않는 성경 적재 계층입니다: {layer}");
        }
        if (!options.VerifyOnly)
        {
            Directory.CreateDirectory(checkpointRoot);
        }

        var package = await packageReader.VerifyAsync(packageRoot, cancellationToken);
        var actor = KnowledgeCorpusActor.User(owner, isAdmin: true);
        var results = new List<BibleCorpusLayerImportResult>(3);
        IReadOnlyList<BibleVerseCorpusInput>? verses = null;

        if (layer is "all" or "translations")
        {
            verses = packageReader.ReadVerses(package);
            foreach (var translation in verses.GroupBy(value => value.TranslationId, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var plan = translationAdapter.CreatePlan(
                    new BibleCorpusOptions(
                        $"bible-{translation.Key}",
                        package.Manifest.PackageVersion,
                        TranslationTitle(translation.Key),
                        "copyrighted-restricted",
                        $"urn:slogs:bible-package:{package.PackageHash.ToLowerInvariant()}:translation:{translation.Key}",
                        "user",
                        owner,
                        "private",
                        null,
                        false,
                        RequireContiguousVerses: true,
                        DeclaredOmissions: package.Manifest.CoordinateExceptions?
                            .Where(value => value.TranslationId == translation.Key).ToArray() ?? [],
                        Chunking: new KnowledgeChunkingOptions(OverlapUnits: 0)),
                    translation.ToArray());
                results.Add(await ExecutePlanAsync(actor, package, plan, checkpointRoot, options.VerifyOnly, cancellationToken));
            }
        }

        if (layer is "all" or "original")
        {
            verses ??= packageReader.ReadVerses(package);
            var coordinates = verses.Where(value => value.TranslationId == "ko-tkv").ToArray();
            if (coordinates.Length == 0)
            {
                throw new InvalidDataException("원문 코퍼스 계획에는 ko-tkv 66권 좌표 골격이 필요합니다.");
            }

            var tokens = packageReader.ReadOriginalTokens(package);
            var entities = packageReader.ReadEntities(package);
            var edges = packageReader.ReadEntityMentions(package)
                .Concat(packageReader.ReadCrossReferences(package))
                .Concat(packageReader.ReadRelationCandidates(package))
                .ToArray();
            var publicLicenses = package.Sources.Sources
                .Where(value => value.DistributionPolicy != "restricted_no_public_redistribution")
                .Select(value => value.License)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (publicLicenses.Length == 0)
            {
                throw new InvalidDataException("공용 원문 계층에 재배포 가능한 출처가 없습니다.");
            }

            var plan = originalAdapter.CreatePlan(
                new BibleOriginalCorpusOptions(
                    "bible-original-step",
                    package.Manifest.PackageVersion,
                    "STEPBible 원문·형태론·고유명사·상호참조",
                    string.Join("; ", publicLicenses),
                    $"urn:slogs:bible-package:{package.PackageHash.ToLowerInvariant()}:scholarly",
                    "system",
                    "slogs",
                    "public_shared",
                    null,
                    true,
                    Chunking: new KnowledgeChunkingOptions(OverlapUnits: 0)),
                coordinates,
                tokens,
                entities,
                edges);
            results.Add(await ExecutePlanAsync(actor, package, plan, checkpointRoot, options.VerifyOnly, cancellationToken));
        }

        return new BibleCorpusImportSummary(
            package.Manifest.PackageId,
            package.Manifest.PackageVersion,
            package.PackageHash,
            options.VerifyOnly,
            results);
    }

    private async Task<BibleCorpusLayerImportResult> ExecutePlanAsync(
        KnowledgeCorpusActor actor,
        VerifiedBibleCorpusPackage package,
        BibleCorpusPlan plan,
        string checkpointRoot,
        bool verifyOnly,
        CancellationToken cancellationToken)
    {
        var planHash = BibleCorpusImportRunner.ComputePlanHash(plan);
        var state = "verified";
        if (!verifyOnly)
        {
            var checkpointPath = Path.Combine(
                checkpointRoot,
                $"{plan.Collection.CollectionId}-{plan.Collection.Version}-{package.PackageHash[..12]}.json");
            var checkpoint = await importRunner.RunAsync(
                actor,
                plan,
                package.PackageHash,
                checkpointPath,
                cancellationToken);
            state = checkpoint.State;
        }

        return new BibleCorpusLayerImportResult(
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
    }

    private static string TranslationTitle(string translationId)
        => translationId switch
        {
            "ko-nkrv" => "개역개정",
            "ko-tkv" => "현대어성경",
            _ => throw new InvalidDataException($"지원하지 않는 한국어 번역본입니다: {translationId}")
        };

    private static string Normalize(string value, int maximumLength, string field)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new InvalidDataException($"{field} 길이가 유효하지 않습니다: {normalized.Length}");
        }
        return normalized;
    }
}
