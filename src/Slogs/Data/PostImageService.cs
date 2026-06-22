using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class PostImageService(
    IDbContextFactory<SlogsDbContext> dbFactory,
    EditorImageStorage imageStorage)
{
    public async Task RegisterUploadAsync(
        string ownerUserName,
        string url,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var normalizedUrl = EditorImageStorage.NormalizeUploadUrl(url);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(normalizedUrl))
        {
            throw new InvalidOperationException("업로드 이미지 추적 정보가 올바르지 않습니다.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(
            db,
            """
            INSERT INTO "PostImages" ("Id", "OwnerUserName", "PostId", "Url", "FileName", "CreatedAt", "LastReferencedAt")
            VALUES (@id, @owner, NULL, @url, @fileName, @now, NULL);
            """,
            cancellationToken,
            ("id", Guid.NewGuid()),
            ("owner", owner),
            ("url", normalizedUrl),
            ("fileName", Path.GetFileName(normalizedUrl)),
            ("now", DateTime.UtcNow));
    }

    public async Task SyncPostImagesAsync(
        string ownerUserName,
        Guid postId,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        if (string.IsNullOrWhiteSpace(owner))
        {
            return;
        }

        var referencedUrls = ExtractReferencedUploadUrls(markdown).ToHashSet(StringComparer.Ordinal);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);

        var trackedImages = await LoadOwnerPostAndPendingImagesAsync(db, owner, postId, cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var image in trackedImages)
        {
            if (referencedUrls.Contains(image.Url))
            {
                await ExecuteAsync(
                    db,
                    """
                    UPDATE "PostImages"
                    SET "PostId" = @postId,
                        "LastReferencedAt" = @now
                    WHERE "Id" = @id;
                    """,
                    cancellationToken,
                    ("postId", postId),
                    ("now", now),
                    ("id", image.Id));
                continue;
            }

            await DeleteTrackedImageAsync(db, image, postId, cancellationToken);
        }

        foreach (var referencedUrl in referencedUrls)
        {
            if (await HasPostImageAsync(db, postId, referencedUrl, cancellationToken))
            {
                continue;
            }

            await ExecuteAsync(
                db,
                """
                INSERT INTO "PostImages" ("Id", "OwnerUserName", "PostId", "Url", "FileName", "CreatedAt", "LastReferencedAt")
                VALUES (@id, @owner, @postId, @url, @fileName, @now, @now);
                """,
                cancellationToken,
                ("id", Guid.NewGuid()),
                ("owner", owner),
                ("postId", postId),
                ("url", referencedUrl),
                ("fileName", Path.GetFileName(referencedUrl)),
                ("now", now));
        }
    }

    public async Task DeletePostImagesAsync(
        string ownerUserName,
        Guid postId,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        if (string.IsNullOrWhiteSpace(owner))
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        var trackedImages = await LoadPostImagesAsync(db, postId, cancellationToken);
        var urls = trackedImages
            .Select(x => x.Url)
            .Concat(ExtractReferencedUploadUrls(markdown))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await ExecuteAsync(
            db,
            """
            DELETE FROM "PostImages"
            WHERE "PostId" = @postId;
            """,
            cancellationToken,
            ("postId", postId));

        foreach (var url in urls)
        {
            if (!await HasAnyReferenceAsync(db, url, postId, cancellationToken))
            {
                await imageStorage.DeleteUploadAsync(url);
            }
        }
    }

    public static IReadOnlyList<string> ExtractReferencedUploadUrls(string? markdown)
        => MarkdownRenderer.FindImages(markdown)
            .Select(image => EditorImageStorage.NormalizeUploadUrl(image.Url))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private async Task DeleteTrackedImageAsync(
        SlogsDbContext db,
        TrackedPostImage image,
        Guid currentPostId,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            db,
            """
            DELETE FROM "PostImages"
            WHERE "Id" = @id;
            """,
            cancellationToken,
            ("id", image.Id));

        if (!await HasAnyReferenceAsync(db, image.Url, currentPostId, cancellationToken))
        {
            await imageStorage.DeleteUploadAsync(image.Url);
        }
    }

    private static async Task<IReadOnlyList<TrackedPostImage>> LoadOwnerPostAndPendingImagesAsync(
        SlogsDbContext db,
        string owner,
        Guid postId,
        CancellationToken cancellationToken)
        => await QueryImagesAsync(
            db,
            """
            SELECT "Id", "Url", "PostId"
            FROM "PostImages"
            WHERE "OwnerUserName" = @owner
                AND ("PostId" = @postId OR "PostId" IS NULL);
            """,
            cancellationToken,
            ("owner", owner),
            ("postId", postId));

    private static async Task<IReadOnlyList<TrackedPostImage>> LoadPostImagesAsync(
        SlogsDbContext db,
        Guid postId,
        CancellationToken cancellationToken)
        => await QueryImagesAsync(
            db,
            """
            SELECT "Id", "Url", "PostId"
            FROM "PostImages"
            WHERE "PostId" = @postId;
            """,
            cancellationToken,
            ("postId", postId));

    private static async Task<bool> HasPostImageAsync(
        SlogsDbContext db,
        Guid postId,
        string url,
        CancellationToken cancellationToken)
        => await QueryExistsAsync(
            db,
            """
            SELECT EXISTS (
                SELECT 1
                FROM "PostImages"
                WHERE "PostId" = @postId
                    AND "Url" = @url
            );
            """,
            cancellationToken,
            ("postId", postId),
            ("url", url));

    private static async Task<bool> HasAnyReferenceAsync(
        SlogsDbContext db,
        string url,
        Guid currentPostId,
        CancellationToken cancellationToken)
    {
        if (await QueryExistsAsync(
            db,
            """
            SELECT EXISTS (
                SELECT 1
                FROM "PostImages"
                WHERE "Url" = @url
            );
            """,
            cancellationToken,
            ("url", url)))
        {
            return true;
        }

        return await QueryExistsAsync(
            db,
            """
            SELECT EXISTS (
                SELECT 1
                FROM "Posts"
                WHERE "Id" <> @currentPostId
                    AND strpos("Body", @url) > 0
            );
            """,
            cancellationToken,
            ("currentPostId", currentPostId),
            ("url", url));
    }

    private static async Task<IReadOnlyList<TrackedPostImage>> QueryImagesAsync(
        SlogsDbContext db,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        using var command = CreateCommand(db, sql, parameters);
        var images = new List<TrackedPostImage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            images.Add(new TrackedPostImage(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2)));
        }

        return images;
    }

    private static async Task<bool> QueryExistsAsync(
        SlogsDbContext db,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        using var command = CreateCommand(db, sql, parameters);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
    }

    private static async Task ExecuteAsync(
        SlogsDbContext db,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        using var command = CreateCommand(db, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand CreateCommand(
        SlogsDbContext db,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static string NormalizeUser(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private sealed record TrackedPostImage(Guid Id, string Url, Guid? PostId);
}
