using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Slogs.Data;

public sealed record BibleCorpusImportCheckpoint(
    int SchemaVersion,
    string PackageHash,
    string PlanHash,
    string CollectionId,
    string Version,
    int TotalBatches,
    int NextBatchIndex,
    string State,
    string? LastContentHash,
    DateTimeOffset UpdatedAt);

public sealed class BibleCorpusImportRunner(KnowledgeCorpusService corpus)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<BibleCorpusImportCheckpoint> RunAsync(
        KnowledgeCorpusActor actor,
        BibleCorpusPlan plan,
        string packageHash,
        string checkpointPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPackageHash = NormalizeHash(packageHash, "packageHash");
        var path = Path.GetFullPath(checkpointPath);
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("체크포인트 경로의 상위 디렉터리를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);
        var planHash = ComputePlanHash(plan);
        var checkpoint = File.Exists(path)
            ? await ReadCheckpointAsync(path, cancellationToken)
            : new BibleCorpusImportCheckpoint(
                1, normalizedPackageHash, planHash, plan.Collection.CollectionId, plan.Collection.Version,
                plan.Batches.Count, 0, "in_progress", null, DateTimeOffset.UtcNow);
        ValidateCheckpoint(checkpoint, plan, normalizedPackageHash, planHash);

        if (checkpoint.State == "complete")
        {
            return checkpoint;
        }

        for (var index = checkpoint.NextBatchIndex; index < plan.Batches.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await corpus.IngestAsync(actor, plan.Batches[index], cancellationToken);
            var isFinal = index == plan.Batches.Count - 1;
            if (isFinal && result.Status != "active")
            {
                throw new InvalidOperationException("최종 성경 코퍼스 배치가 active 상태로 전환되지 않았습니다.");
            }

            checkpoint = checkpoint with
            {
                NextBatchIndex = index + 1,
                State = isFinal ? "complete" : "in_progress",
                LastContentHash = result.ContentHash,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await WriteCheckpointAsync(path, checkpoint, cancellationToken);
        }

        return checkpoint;
    }

    public static string ComputePlanHash(BibleCorpusPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"collection|{plan.Collection.CollectionId}|{plan.Collection.Version}|{plan.Collection.OwnerKind}|{plan.Collection.OwnerKey}|{plan.Collection.Visibility}|{plan.Collection.ExpectedChunkCount}");
        foreach (var document in plan.Batches.SelectMany(value => value.Documents).OrderBy(value => value.DocumentId, StringComparer.Ordinal))
        {
            builder.AppendLine($"document|{document.DocumentId}|{document.SourceLocator}");
        }

        foreach (var structure in plan.Batches.SelectMany(value => value.StructureNodes).OrderBy(value => value.NodeId, StringComparer.Ordinal))
        {
            builder.AppendLine($"structure|{structure.NodeId}|{structure.ParentNodeId}|{structure.Locator}");
        }

        foreach (var chunk in plan.Batches.SelectMany(value => value.Chunks).OrderBy(value => value.ChunkId, StringComparer.Ordinal))
        {
            var textHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chunk.Text)));
            builder.AppendLine($"chunk|{chunk.ChunkId}|{chunk.StartLocator}|{chunk.EndLocator}|{textHash}");
        }

        foreach (var entity in plan.Batches.SelectMany(value => value.Entities).OrderBy(value => value.EntityId, StringComparer.Ordinal))
        {
            builder.AppendLine($"entity|{entity.EntityId}|{entity.EntityType}|{entity.CanonicalLabel}|{string.Join(',', entity.Aliases ?? [])}");
        }

        foreach (var relation in plan.Batches.SelectMany(value => value.Relations).OrderBy(value => value.RelationId, StringComparer.Ordinal))
        {
            builder.AppendLine($"relation|{relation.RelationId}|{relation.FromNodeId}|{relation.RelationType}|{relation.ToNodeId}|{relation.ReviewStatus}|{relation.Confidence:R}");
            foreach (var evidence in relation.Evidence.OrderBy(value => value.SourceId, StringComparer.Ordinal).ThenBy(value => value.Locator, StringComparer.Ordinal))
            {
                builder.AppendLine($"evidence|{evidence.SourceId}|{evidence.Locator}|{evidence.EvidenceType}|{string.Join(',', evidence.ChunkIds ?? [])}");
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static async Task<BibleCorpusImportCheckpoint> ReadCheckpointAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BibleCorpusImportCheckpoint>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("성경 코퍼스 체크포인트를 읽을 수 없습니다.");
    }

    private static void ValidateCheckpoint(
        BibleCorpusImportCheckpoint checkpoint,
        BibleCorpusPlan plan,
        string packageHash,
        string planHash)
    {
        if (checkpoint.SchemaVersion != 1
            || checkpoint.PackageHash != packageHash
            || checkpoint.PlanHash != planHash
            || checkpoint.CollectionId != plan.Collection.CollectionId
            || checkpoint.Version != plan.Collection.Version
            || checkpoint.TotalBatches != plan.Batches.Count
            || checkpoint.NextBatchIndex < 0
            || checkpoint.NextBatchIndex > checkpoint.TotalBatches
            || checkpoint.State is not ("in_progress" or "complete")
            || (checkpoint.State == "complete" && checkpoint.NextBatchIndex != checkpoint.TotalBatches))
        {
            throw new InvalidDataException("체크포인트가 현재 패키지와 적재 계획에 일치하지 않습니다.");
        }
    }

    private static async Task WriteCheckpointAsync(
        string path,
        BibleCorpusImportCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, checkpoint, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string NormalizeHash(string value, string field)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"{field}는 64자리 SHA-256이어야 합니다.");
        }

        return normalized;
    }
}
