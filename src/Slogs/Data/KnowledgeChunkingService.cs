using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Slogs.Data;

public sealed class KnowledgeChunkingService
{
    private static readonly Regex TokenPattern = new(
        @"[\p{L}\p{N}]+|[^\s\p{L}\p{N}]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<KnowledgeChunkInput> CreateChunks(
        string collectionId,
        string version,
        string documentId,
        string? structureNodeId,
        IReadOnlyList<KnowledgeTextUnit> units,
        KnowledgeChunkingOptions? options = null)
    {
        var settings = Validate(options ?? new KnowledgeChunkingOptions());
        if (units.Count == 0)
        {
            return [];
        }

        ValidateUnits(units);
        var groups = BuildGroups(units, settings);
        var chunks = new List<KnowledgeChunkInput>(groups.Count);
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var chunkId = BuildStableChunkId(collectionId, version, documentId, structureNodeId, group);
            var previousChunkId = index == 0
                ? null
                : BuildStableChunkId(collectionId, version, documentId, structureNodeId, groups[index - 1]);
            var nextChunkId = index == groups.Count - 1
                ? null
                : BuildStableChunkId(collectionId, version, documentId, structureNodeId, groups[index + 1]);
            var text = string.Join('\n', group.Select(unit => unit.Text.Trim()));
            var metadata = MergeMetadata(group);
            chunks.Add(new KnowledgeChunkInput(
                chunkId,
                documentId,
                structureNodeId,
                index,
                text,
                group[0].Locator,
                group[^1].Locator,
                previousChunkId,
                nextChunkId,
                index == 0 ? 0 : Math.Min(settings.OverlapUnits, group.Count),
                CountTokens(text),
                settings.TokenizerId,
                group.Select(unit => unit.UnitId).ToArray(),
                metadata));
        }

        return chunks;
    }

    public static int CountTokens(string text) => TokenPattern.Matches(text).Count;

    private static KnowledgeChunkingOptions Validate(KnowledgeChunkingOptions options)
    {
        if (options.MinTokens <= 0
            || options.TargetTokens < options.MinTokens
            || options.MaxTokens < options.TargetTokens
            || options.OverlapUnits < 0
            || string.IsNullOrWhiteSpace(options.TokenizerId))
        {
            throw new InvalidDataException("청킹 옵션은 0 < min <= target <= max 및 overlap >= 0을 만족해야 합니다.");
        }

        return options;
    }

    private static void ValidateUnits(IReadOnlyList<KnowledgeTextUnit> units)
    {
        if (units.Any(unit => string.IsNullOrWhiteSpace(unit.UnitId)
            || string.IsNullOrWhiteSpace(unit.Locator)
            || string.IsNullOrWhiteSpace(unit.Text)))
        {
            throw new InvalidDataException("지식 단위에는 unitId, locator, text가 필요합니다.");
        }

        if (units.Select(unit => unit.UnitId).Distinct(StringComparer.Ordinal).Count() != units.Count)
        {
            throw new InvalidDataException("지식 단위 ID는 문서 안에서 고유해야 합니다.");
        }
    }

    private static List<List<KnowledgeTextUnit>> BuildGroups(
        IReadOnlyList<KnowledgeTextUnit> units,
        KnowledgeChunkingOptions options)
    {
        var result = new List<List<KnowledgeTextUnit>>();
        var current = new List<KnowledgeTextUnit>();
        var currentTokens = 0;
        foreach (var unit in units)
        {
            var unitTokens = CountTokens(unit.Text);
            if (unitTokens > options.MaxTokens)
            {
                throw new InvalidDataException(
                    $"자연 경계 단위가 maxTokens를 초과합니다. 도메인 어댑터가 먼저 분할해야 합니다: {unit.UnitId}, tokens={unitTokens}");
            }

            var wouldOverflow = currentTokens + unitTokens > options.MaxTokens;
            var hardBoundary = unit.HardBoundary && current.Count > 0;
            var reachedTarget = currentTokens >= options.TargetTokens && current.Count > 0;
            if (hardBoundary || wouldOverflow || reachedTarget)
            {
                result.Add(current);
                current = CopyOverlap(current, options.OverlapUnits);
                currentTokens = current.Sum(item => CountTokens(item.Text));
            }

            current.Add(unit);
            currentTokens += unitTokens;
        }

        if (current.Count > 0)
        {
            if (result.Count > 0 && currentTokens < options.MinTokens)
            {
                var prior = result[^1];
                var combined = prior.Concat(current.Skip(Math.Min(options.OverlapUnits, current.Count))).ToList();
                if (combined.Sum(item => CountTokens(item.Text)) <= options.MaxTokens)
                {
                    result[^1] = combined;
                    return result;
                }
            }

            result.Add(current);
        }

        return result;
    }

    private static List<KnowledgeTextUnit> CopyOverlap(IReadOnlyList<KnowledgeTextUnit> source, int overlapUnits)
        => overlapUnits == 0
            ? []
            : source.Skip(Math.Max(0, source.Count - overlapUnits)).ToList();

    private static string BuildStableChunkId(
        string collectionId,
        string version,
        string documentId,
        string? structureNodeId,
        IReadOnlyList<KnowledgeTextUnit> units)
    {
        var identity = string.Join('\n', new[]
        {
            collectionId.Trim(),
            documentId.Trim(),
            structureNodeId?.Trim() ?? string.Empty,
            string.Join('|', units.Select(unit => unit.UnitId))
        });
        _ = version;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..20];
        return $"chunk:{documentId}:{hash}";
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(IReadOnlyList<KnowledgeTextUnit> units)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            foreach (var pair in unit.Metadata ?? new Dictionary<string, string>())
            {
                if (!result.TryGetValue(pair.Key, out var existing))
                {
                    result[pair.Key] = pair.Value;
                }
                else if (!string.Equals(existing, pair.Value, StringComparison.Ordinal))
                {
                    result.Remove(pair.Key);
                }
            }
        }

        return result;
    }
}
