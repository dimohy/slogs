using System.Security.Cryptography;
using System.Text.Json;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BibleCorpusPackageReaderTests
{
    [Fact]
    public async Task VerifyChecksManifestHashesCountsSourcesAndCoordinateExceptionsBeforeReading()
    {
        var root = Path.Combine(Path.GetTempPath(), $"slogs-bible-package-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sources = new BiblePackageSourceLock(
                1,
                DateTimeOffset.UtcNow,
                [new BiblePackageSource(
                    "korean-translations", "verified", "workspace:data", "1", "copyrighted",
                    "publisher_and_collection", "restricted_no_public_redistribution")]);
            await File.WriteAllTextAsync(Path.Combine(root, "sources.lock.json"), JsonSerializer.Serialize(sources));
            var files = new[]
            {
                "verses.ndjson", "entities.ndjson", "original-tokens.ndjson", "entity-mentions.ndjson",
                "cross-references.ndjson", "relation-candidates.ndjson"
            };
            foreach (var file in files)
            {
                await File.WriteAllTextAsync(Path.Combine(root, file), "{}\n");
            }

            var entries = files.Append("sources.lock.json").Select(file =>
            {
                var path = Path.Combine(root, file);
                return new BiblePackageFile(
                    file, file.EndsWith(".ndjson", StringComparison.Ordinal) ? 1 : 1,
                    new FileInfo(path).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            }).ToArray();
            var manifest = new BibleCorpusPackageManifest(
                1, "fixture", "1", DateTimeOffset.UtcNow, "internal_research", "OSIS", entries,
                new Dictionary<string, long> { ["verses"] = 1 }, ["restricted translation"],
                [new BibleDeclaredOmission("ko-nkrv", "Acts.24.7", "publisher omits this verse", "https://example.test/acts-24")]);
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest));

            var reader = new BibleCorpusPackageReader();
            var verified = await reader.VerifyAsync(root);
            Assert.Equal("OSIS", verified.Manifest.CoordinateSystem);
            Assert.Single(verified.Manifest.CoordinateExceptions!);
            Assert.Equal(64, verified.PackageHash.Length);

            await File.AppendAllTextAsync(Path.Combine(root, "verses.ndjson"), "{}\n");
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => reader.VerifyAsync(root));
            Assert.Contains("바이트 수", exception.Message);
        }
        finally
        {
            if (Directory.Exists(root) && root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VerifyRejectsPathTraversalBeforeOpeningManifestEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"slogs-bible-package-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var manifest = new BibleCorpusPackageManifest(
                1, "fixture", "1", DateTimeOffset.UtcNow, "internal_research", "OSIS",
                [new BiblePackageFile("../outside.ndjson", 1, 2, new string('0', 64))],
                new Dictionary<string, long>(), ["restricted"]);
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest));

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new BibleCorpusPackageReader().VerifyAsync(root));
            Assert.Contains("안전하지 않은", exception.Message);
        }
        finally
        {
            if (Directory.Exists(root) && root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
