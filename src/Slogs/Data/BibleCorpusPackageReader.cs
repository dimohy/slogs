using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Slogs.Data;

public sealed record BiblePackageFile(
    string RelativePath,
    long Records,
    long Bytes,
    string Sha256);

public sealed record BibleCorpusPackageManifest(
    int SchemaVersion,
    string PackageId,
    string PackageVersion,
    DateTimeOffset GeneratedAt,
    string Visibility,
    string CoordinateSystem,
    IReadOnlyList<BiblePackageFile> Files,
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<BibleDeclaredOmission>? CoordinateExceptions = null);

public sealed record BiblePackageSource(
    string Id,
    string Title,
    string Uri,
    string Version,
    string License,
    string AuthorityClass,
    string DistributionPolicy);

public sealed record BiblePackageSourceLock(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<BiblePackageSource> Sources);

public sealed record VerifiedBibleCorpusPackage(
    string RootPath,
    string PackageHash,
    BibleCorpusPackageManifest Manifest,
    BiblePackageSourceLock Sources);

public sealed class BibleCorpusPackageReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> RequiredFiles = new(StringComparer.Ordinal)
    {
        "verses.ndjson",
        "entities.ndjson",
        "original-tokens.ndjson",
        "entity-mentions.ndjson",
        "cross-references.ndjson",
        "relation-candidates.ndjson",
        "sources.lock.json"
    };

    public async Task<VerifiedBibleCorpusPackage> VerifyAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("성경 코퍼스 manifest.json이 없습니다.", manifestPath);
        }

        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<BibleCorpusPackageManifest>(manifestStream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("성경 코퍼스 manifest.json을 읽을 수 없습니다.");
        ValidateManifest(manifest);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = NormalizeRelativePath(file.RelativePath);
            if (!seen.Add(relativePath))
            {
                throw new InvalidDataException($"manifest에 중복된 파일이 있습니다: {relativePath}");
            }

            var path = ResolveWithinRoot(root, relativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("manifest에 선언된 파일이 없습니다.", path);
            }

            var info = new FileInfo(path);
            if (info.Length != file.Bytes)
            {
                throw new InvalidDataException($"패키지 파일 바이트 수가 manifest와 다릅니다: {relativePath}");
            }

            await using var stream = File.OpenRead(path);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!actualHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"패키지 파일 SHA-256이 manifest와 다릅니다: {relativePath}");
            }

            var actualRecords = relativePath.EndsWith(".ndjson", StringComparison.Ordinal)
                ? await CountLinesAsync(path, cancellationToken)
                : 1;
            if (actualRecords != file.Records)
            {
                throw new InvalidDataException($"패키지 레코드 수가 manifest와 다릅니다: {relativePath}");
            }
        }

        var missing = RequiredFiles.Except(seen).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"manifest에 필수 파일이 없습니다: {string.Join(',', missing)}");
        }

        var sourceLockPath = ResolveWithinRoot(root, "sources.lock.json");
        await using var sourceStream = File.OpenRead(sourceLockPath);
        var sources = await JsonSerializer.DeserializeAsync<BiblePackageSourceLock>(sourceStream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("sources.lock.json을 읽을 수 없습니다.");
        ValidateSources(sources);

        var identity = string.Join('\n', manifest.Files.OrderBy(value => value.RelativePath, StringComparer.Ordinal)
            .Select(value => $"{NormalizeRelativePath(value.RelativePath)}|{value.Records}|{value.Bytes}|{value.Sha256.ToUpperInvariant()}"));
        var packageHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new VerifiedBibleCorpusPackage(root, packageHash, manifest, sources);
    }

    public IReadOnlyList<BibleVerseCorpusInput> ReadVerses(
        VerifiedBibleCorpusPackage package,
        string? translationId = null)
        => ReadNdjson<BibleVerseCorpusInput>(package, "verses.ndjson")
            .Where(value => translationId is null || value.TranslationId == translationId)
            .ToArray();

    public IReadOnlyList<BibleEntityCorpusInput> ReadEntities(VerifiedBibleCorpusPackage package)
        => ReadNdjson<BiblePackageEntity>(package, "entities.ndjson")
            .Select(value => new BibleEntityCorpusInput(
                value.Id, value.EntityType, value.CanonicalName, value.Aliases,
                value.Description, value.StrongIds, value.SourceId))
            .ToArray();

    private static IEnumerable<T> ReadNdjson<T>(VerifiedBibleCorpusPackage package, string relativePath)
    {
        _ = package.Manifest.Files.SingleOrDefault(value =>
                NormalizeRelativePath(value.RelativePath) == relativePath)
            ?? throw new InvalidDataException($"검증된 패키지에 파일이 없습니다: {relativePath}");
        var path = ResolveWithinRoot(package.RootPath, relativePath);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                throw new InvalidDataException($"{relativePath}에 빈 레코드가 있습니다: line={lineNumber}");
            }

            yield return JsonSerializer.Deserialize<T>(line, JsonOptions)
                ?? throw new InvalidDataException($"{relativePath} 레코드를 읽을 수 없습니다: line={lineNumber}");
        }
    }

    private static void ValidateManifest(BibleCorpusPackageManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.CoordinateSystem != "OSIS"
            || string.IsNullOrWhiteSpace(manifest.PackageId) || string.IsNullOrWhiteSpace(manifest.PackageVersion))
        {
            throw new InvalidDataException("지원하지 않는 성경 패키지 manifest 계약입니다.");
        }

        if (manifest.Files.Count == 0 || manifest.Restrictions.Count == 0)
        {
            throw new InvalidDataException("성경 패키지에는 파일 목록과 배포 제한이 필요합니다.");
        }

        foreach (var omission in manifest.CoordinateExceptions ?? [])
        {
            if (string.IsNullOrWhiteSpace(omission.TranslationId) || string.IsNullOrWhiteSpace(omission.Reference)
                || string.IsNullOrWhiteSpace(omission.Reason) || !Uri.TryCreate(omission.SourceUri, UriKind.Absolute, out _))
            {
                throw new InvalidDataException("좌표 예외에는 번역본, OSIS reference, 이유와 절대 source URI가 필요합니다.");
            }
        }
    }

    private static void ValidateSources(BiblePackageSourceLock sourceLock)
    {
        if (sourceLock.SchemaVersion != 1 || sourceLock.Sources.Count == 0)
        {
            throw new InvalidDataException("지원하지 않는 sources.lock.json 계약입니다.");
        }

        var korean = sourceLock.Sources.SingleOrDefault(value => value.Id == "korean-translations")
            ?? throw new InvalidDataException("한국어 번역 성경 source lock이 없습니다.");
        if (korean.DistributionPolicy != "restricted_no_public_redistribution")
        {
            throw new InvalidDataException("한국어 번역 성경의 재배포 제한 계약이 잠기지 않았습니다.");
        }
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        if (normalized.Length == 0 || Path.IsPathRooted(normalized)
            || normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException($"안전하지 않은 패키지 상대 경로입니다: {value}");
        }

        return normalized;
    }

    private static string ResolveWithinRoot(string root, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var path = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"패키지 루트 밖의 경로입니다: {relativePath}");
        }

        return path;
    }

    private static async Task<long> CountLinesAsync(string path, CancellationToken cancellationToken)
    {
        long count = 0;
        using var reader = File.OpenText(path);
        while (await reader.ReadLineAsync(cancellationToken) is not null)
        {
            count++;
        }

        return count;
    }

    private sealed record BiblePackageEntity(
        string Id,
        string CanonicalName,
        string EntityType,
        string Description,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<string> StrongIds,
        string SourceId);
}
