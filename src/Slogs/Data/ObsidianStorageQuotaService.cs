using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class ObsidianStorageQuotaService(IDbContextFactory<SlogsDbContext> dbFactory)
{
    public const long DefaultPerAccountStorageLimitBytes = 1L * 1024 * 1024 * 1024;
    private const long MaxStorageCapacityBytes = 1024L * 1024 * 1024 * 1024 * 1024;
    public const string PerAccountLimitKey = "obsidian.storage.perAccountLimitBytes";
    public const string TotalCapacityKey = "obsidian.storage.totalCapacityBytes";

    public async Task<AdminObsidianStorageSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var snapshot = await GetSnapshotAsync(db, cancellationToken);
        return ToResponse(snapshot);
    }

    public async Task<AdminObsidianStorageSettingsResponse> UpdateSettingsAsync(
        AdminObsidianStorageSettingsUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCapacityBytes(request.TotalCapacityBytes);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var usedBytes = await GetTotalUsedBytesAsync(db, cancellationToken);
        if (request.TotalCapacityBytes < usedBytes)
        {
            throw new InvalidOperationException("obsidianStorageCapacityBelowUsage");
        }

        await WriteLongSettingAsync(db, TotalCapacityKey, request.TotalCapacityBytes, cancellationToken);
        var snapshot = await GetSnapshotAsync(db, cancellationToken);
        return ToResponse(snapshot);
    }

    public static async Task<ObsidianStorageQuotaSnapshot> GetSnapshotAsync(
        SlogsDbContext db,
        CancellationToken cancellationToken = default)
    {
        var perAccountLimit = await ReadLongSettingAsync(db, PerAccountLimitKey, cancellationToken)
            ?? DefaultPerAccountStorageLimitBytes;
        ValidateCapacityBytes(perAccountLimit);

        var totalCapacitySetting = await ReadLongSettingAsync(db, TotalCapacityKey, cancellationToken);
        if (totalCapacitySetting is not null)
        {
            ValidateCapacityBytes(totalCapacitySetting.Value);
        }

        var totalCapacity = totalCapacitySetting
            ?? await GetDefaultTotalCapacityBytesAsync(db, perAccountLimit, cancellationToken);
        var usedBytes = await GetTotalUsedBytesAsync(db, cancellationToken);
        var remainingBytes = Math.Max(0, totalCapacity - usedBytes);

        return new ObsidianStorageQuotaSnapshot(
            perAccountLimit,
            totalCapacity,
            usedBytes,
            remainingBytes,
            ToUsagePercent(usedBytes, totalCapacity),
            totalCapacitySetting is not null);
    }

    public static async Task AssertCanApplyActiveStorageDeltaAsync(
        SlogsDbContext db,
        string ownerUserName,
        long activeSizeDeltaBytes,
        long pendingOwnerDeltaBytes = 0,
        long pendingTotalDeltaBytes = 0,
        CancellationToken cancellationToken = default)
    {
        if (activeSizeDeltaBytes <= 0)
        {
            return;
        }

        var owner = string.IsNullOrWhiteSpace(ownerUserName) ? string.Empty : ownerUserName.Trim().ToLowerInvariant();
        var snapshot = await GetSnapshotAsync(db, cancellationToken);
        var ownerUsedBytes = await GetOwnerUsedBytesAsync(db, owner, cancellationToken);
        var projectedOwnerBytes = ownerUsedBytes + pendingOwnerDeltaBytes + activeSizeDeltaBytes;
        if (projectedOwnerBytes > snapshot.PerAccountStorageLimitBytes)
        {
            throw new InvalidOperationException("obsidianAccountStorageQuotaExceeded");
        }

        var projectedTotalBytes = snapshot.TotalUsedBytes + pendingTotalDeltaBytes + activeSizeDeltaBytes;
        if (projectedTotalBytes > snapshot.TotalCapacityBytes)
        {
            throw new InvalidOperationException("obsidianTotalStorageQuotaExceeded");
        }
    }

    public static async Task<long> GetTotalUsedBytesAsync(
        SlogsDbContext db,
        CancellationToken cancellationToken = default)
        => await db.ObsidianVaultFiles
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .SumAsync(x => (long?)x.SizeBytes, cancellationToken) ?? 0;

    public static async Task<long> GetOwnerUsedBytesAsync(
        SlogsDbContext db,
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        var owner = string.IsNullOrWhiteSpace(ownerUserName) ? string.Empty : ownerUserName.Trim().ToLowerInvariant();
        return await db.ObsidianVaultFiles
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner && !x.IsDeleted)
            .SumAsync(x => (long?)x.SizeBytes, cancellationToken) ?? 0;
    }

    private static async Task<long> GetDefaultTotalCapacityBytesAsync(
        SlogsDbContext db,
        long perAccountLimitBytes,
        CancellationToken cancellationToken)
    {
        var userCount = await db.Users
            .AsNoTracking()
            .CountAsync(x => x.UserName != AuthUser.AdminUserName, cancellationToken);
        return Math.Max(1, userCount) * perAccountLimitBytes;
    }

    private static async Task<long?> ReadLongSettingAsync(
        SlogsDbContext db,
        string key,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "Value"
                FROM "SlogsSettings"
                WHERE "Key" = @key
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@key";
            parameter.Value = key;
            command.Parameters.Add(parameter);

            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is null || value is DBNull)
            {
                return null;
            }

            return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException("obsidianStorageSettingInvalid");
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task WriteLongSettingAsync(
        SlogsDbContext db,
        string key,
        long value,
        CancellationToken cancellationToken)
    {
        var valueText = value.ToString(CultureInfo.InvariantCulture);
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO "SlogsSettings" ("Key", "Value", "UpdatedAt")
             VALUES ({key}, {valueText}, {now})
             ON CONFLICT ("Key") DO UPDATE SET
                "Value" = EXCLUDED."Value",
                "UpdatedAt" = EXCLUDED."UpdatedAt";
             """,
            cancellationToken);
    }

    private static void ValidateCapacityBytes(long capacityBytes)
    {
        if (capacityBytes is < 1 or > MaxStorageCapacityBytes)
        {
            throw new InvalidOperationException("obsidianStorageCapacityInvalid");
        }
    }

    private static AdminObsidianStorageSettingsResponse ToResponse(ObsidianStorageQuotaSnapshot snapshot)
        => new(
            snapshot.PerAccountStorageLimitBytes,
            snapshot.TotalCapacityBytes,
            snapshot.TotalUsedBytes,
            snapshot.TotalRemainingBytes,
            snapshot.TotalUsagePercent,
            snapshot.TotalCapacityConfigured);

    private static int ToUsagePercent(long usedBytes, long capacityBytes)
        => capacityBytes <= 0
            ? 0
            : (int)Math.Clamp(Math.Ceiling(usedBytes * 100.0 / capacityBytes), 0, 100);
}

public sealed record ObsidianStorageQuotaSnapshot(
    long PerAccountStorageLimitBytes,
    long TotalCapacityBytes,
    long TotalUsedBytes,
    long TotalRemainingBytes,
    int TotalUsagePercent,
    bool TotalCapacityConfigured);
