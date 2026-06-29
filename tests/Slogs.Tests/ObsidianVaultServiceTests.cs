using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slogs.Data;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Slogs.Tests;

public sealed class ObsidianVaultServiceTests
{
    [Fact]
    public async Task UpsertMarkdownStoresCursorAndRejectsStaleBaseVersion()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var vault = await fixture.Obsidian.GetOrCreateVaultAsync("alice", new ObsidianVaultCreateRequest("Vault"));
        var first = await fixture.Obsidian.UpsertFileAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileUpsertRequest("Notes/Today.md", "# Today"));

        Assert.NotNull(first?.File);
        Assert.Equal(1, first.File.Version);
        Assert.Equal(Sha256Text("# Today"), first.File.ContentHash);

        var conflict = await fixture.Obsidian.UpsertFileAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileUpsertRequest("Notes/Today.md", "# Changed", BaseVersion: 0));

        Assert.NotNull(conflict);
        Assert.True(conflict.IsConflict);
        Assert.Equal(1, conflict.RemoteFile?.Version);
    }

    [Fact]
    public async Task OwnerIsolationPreventsCrossUserVaultReads()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var vault = await fixture.Obsidian.GetOrCreateVaultAsync("alice", new ObsidianVaultCreateRequest("Vault"));
        await fixture.Obsidian.UpsertFileAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileUpsertRequest("Notes/Owner.md", "owner"));

        var bobFiles = await fixture.Obsidian.GetFilesAsync("bob", vault.Id, null, includeDeleted: true);

        Assert.Null(bobFiles);
    }

    [Fact]
    public async Task PathValidationRequiresExplicitAttachmentAndSettingsScopes()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var vault = await fixture.Obsidian.GetOrCreateVaultAsync("alice", new ObsidianVaultCreateRequest("Vault"));

        var attachmentError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Obsidian.UpsertFileAsync(
                "alice",
                vault.Id,
                new ObsidianVaultFileUpsertRequest("images/logo.png", "abc")));
        Assert.Equal("obsidianMarkdownPathRequired", attachmentError.Message);

        var settingsError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Obsidian.UpsertFileAsync(
                "alice",
                vault.Id,
                new ObsidianVaultFileUpsertRequest(".obsidian/app.json", "{}")));
        Assert.Equal("obsidianSettingsScopeRequired", settingsError.Message);
    }

    [Fact]
    public async Task ExplicitAttachmentsAndSettingsScopesAreStoredAndFiltered()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var vault = await fixture.Obsidian.GetOrCreateVaultAsync("alice", new ObsidianVaultCreateRequest("Vault"));
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var imageBase64 = Convert.ToBase64String(imageBytes);

        var attachment = await fixture.Obsidian.UpsertFileAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileUpsertRequest(
                "images/logo.png",
                imageBase64,
                MediaType: "image/png",
                Scope: ObsidianSyncScopes.Attachments,
                Kind: ObsidianVaultFileKinds.Attachment,
                Encoding: ObsidianVaultContentEncodings.Base64,
                MetadataJson: "{\"role\":\"cover\"}"));
        var settings = await fixture.Obsidian.UpsertFileAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileUpsertRequest(
                ".obsidian/app.json",
                "{}",
                MediaType: "application/json",
                Scope: ObsidianSyncScopes.Settings,
                Kind: ObsidianVaultFileKinds.Setting,
                Encoding: ObsidianVaultContentEncodings.Utf8));

        Assert.Equal(Sha256Bytes(imageBytes), attachment?.File?.ContentHash);
        Assert.Equal(ObsidianSyncScopes.Settings, settings?.File?.Scope);

        var attachmentOnly = await fixture.Obsidian.GetFilesAsync(
            "alice",
            vault.Id,
            null,
            includeDeleted: true,
            scopes: ObsidianSyncScopes.Attachments);

        Assert.NotNull(attachmentOnly);
        Assert.Single(attachmentOnly.Files);
        Assert.Equal("images/logo.png", attachmentOnly.Files[0].Path);
    }

    [Fact]
    public async Task DeleteTombstonePreservesContentAndRestoreAddsVersionHistory()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var vault = await fixture.Obsidian.GetOrCreateVaultAsync("alice", new ObsidianVaultCreateRequest("Vault"));
        var created = await fixture.Obsidian.UpsertFileAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileUpsertRequest("Notes/Keep.md", "keep me"));

        var deleted = await fixture.Obsidian.DeleteFileAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileDeleteRequest("Notes/Keep.md", created!.File!.Version));

        Assert.True(deleted?.File?.IsDeleted);
        Assert.Equal("keep me", deleted?.File?.Content);

        var restored = await fixture.Obsidian.RestoreFileAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileRestoreRequest("Notes/Keep.md", deleted!.File!.Version));
        var history = await fixture.Obsidian.GetFileHistoryAsync("alice", vault.Id, "Notes/Keep.md");

        Assert.False(restored?.File?.IsDeleted);
        Assert.NotNull(history);
        Assert.Equal([3L, 2L, 1L], history.Select(x => x.Version).ToArray());
    }

    [Fact]
    public async Task BatchUpsertRecordsClientStatus()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var vault = await fixture.Obsidian.GetOrCreateVaultAsync("alice", new ObsidianVaultCreateRequest("Vault"));

        var batch = await fixture.Obsidian.UpsertFilesAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileBatchUpsertRequest(
                [
                    new ObsidianVaultFileUpsertRequest("A.md", "A"),
                    new ObsidianVaultFileUpsertRequest("B.md", "B")
                ],
                "client-1",
                "Plugin",
                "obsidian-plugin"));
        var status = await fixture.Obsidian.GetStatusAsync("alice", vault.Id);

        Assert.NotNull(batch);
        Assert.Equal(2, batch.Files.Count);
        Assert.Empty(batch.Conflicts);
        Assert.NotNull(status);
        Assert.Equal(2, status.ActiveFileCount);
        Assert.Single(status.Clients);
        Assert.Equal("client-1", status.Clients[0].ClientId);
    }

    [Fact]
    public async Task AdminRenameMovesObsidianVaultFilesClientsAndHistory()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var vault = await fixture.Obsidian.GetOrCreateVaultAsync("alice", new ObsidianVaultCreateRequest("Vault"));
        await fixture.Obsidian.UpsertFilesAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileBatchUpsertRequest(
                [new ObsidianVaultFileUpsertRequest("Rename.md", "before")],
                "client-1",
                "Plugin",
                "obsidian-plugin"));

        await fixture.Auth.ChangeAdminUserNameAsync("alice", "alice2");

        var movedVaults = await fixture.Obsidian.GetVaultsAsync("alice2");
        var oldVaults = await fixture.Obsidian.GetVaultsAsync("alice");
        var movedFiles = await fixture.Obsidian.GetFilesAsync("alice2", vault.Id, null, includeDeleted: true);
        var movedHistory = await fixture.Obsidian.GetFileHistoryAsync("alice2", vault.Id, "Rename.md");
        var movedClients = await fixture.Obsidian.GetClientsAsync("alice2", vault.Id);

        Assert.Empty(oldVaults);
        Assert.Single(movedVaults);
        Assert.NotNull(movedFiles);
        Assert.Single(movedFiles.Files);
        Assert.NotNull(movedHistory);
        Assert.Single(movedHistory);
        Assert.NotNull(movedClients);
        Assert.Single(movedClients);
    }

    [Fact]
    public async Task UpsertRejectsFilesOverAccountStorageQuota()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SetObsidianStorageLimitsAsync(perAccountBytes: 5, totalBytes: 100);
        var vault = await fixture.Obsidian.GetOrCreateVaultAsync("alice", new ObsidianVaultCreateRequest("Vault"));

        await fixture.Obsidian.UpsertFileAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileUpsertRequest("A.md", "12345"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Obsidian.UpsertFileAsync(
                "alice",
                vault.Id,
                new ObsidianVaultFileUpsertRequest("B.md", "1")));

        Assert.Equal("obsidianAccountStorageQuotaExceeded", error.Message);
    }

    [Fact]
    public async Task UpsertRejectsFilesOverTotalStorageQuota()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SetObsidianStorageLimitsAsync(perAccountBytes: 100, totalBytes: 5);
        var aliceVault = await fixture.Obsidian.GetOrCreateVaultAsync("alice", new ObsidianVaultCreateRequest("Vault"));
        var bobVault = await fixture.Obsidian.GetOrCreateVaultAsync("bob", new ObsidianVaultCreateRequest("Vault"));

        await fixture.Obsidian.UpsertFileAsync(
            "alice",
            aliceVault.Id,
            new ObsidianVaultFileUpsertRequest("A.md", "123"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Obsidian.UpsertFileAsync(
                "bob",
                bobVault.Id,
                new ObsidianVaultFileUpsertRequest("B.md", "123")));

        Assert.Equal("obsidianTotalStorageQuotaExceeded", error.Message);
    }

    [Fact]
    public async Task StorageSettingsDefaultToOneGiBPerAccountAndUserBasedTotal()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var settings = await fixture.StorageQuota.GetSettingsAsync();

        Assert.Equal(ObsidianStorageQuotaService.DefaultPerAccountStorageLimitBytes, settings.PerAccountStorageLimitBytes);
        Assert.Equal(2 * ObsidianStorageQuotaService.DefaultPerAccountStorageLimitBytes, settings.TotalCapacityBytes);
        Assert.Equal(settings.TotalCapacityBytes, settings.TotalRemainingBytes);
        Assert.Equal(0, settings.TotalUsedBytes);
        Assert.Equal(0, settings.TotalUsagePercent);
        Assert.False(settings.TotalCapacityConfigured);
    }

    [Fact]
    public async Task StorageSettingsUpdatePersistsTotalCapacityAndRejectsBelowUsage()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var capacityBytes = 3 * ObsidianStorageQuotaService.DefaultPerAccountStorageLimitBytes;

        var updated = await fixture.StorageQuota.UpdateSettingsAsync(new AdminObsidianStorageSettingsUpdateRequest(capacityBytes));

        Assert.Equal(capacityBytes, updated.TotalCapacityBytes);
        Assert.True(updated.TotalCapacityConfigured);

        var vault = await fixture.Obsidian.GetOrCreateVaultAsync("alice", new ObsidianVaultCreateRequest("Vault"));
        await fixture.Obsidian.UpsertFileAsync(
            "alice",
            vault.Id,
            new ObsidianVaultFileUpsertRequest("Usage.md", "12345"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.StorageQuota.UpdateSettingsAsync(new AdminObsidianStorageSettingsUpdateRequest(4)));

        Assert.Equal("obsidianStorageCapacityBelowUsage", error.Message);
    }

    private static string Sha256Text(string value)
        => Sha256Bytes(Encoding.UTF8.GetBytes(value));

    private static string Sha256Bytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider services;

        private TestFixture(SqliteConnection connection, ServiceProvider services)
        {
            this.connection = connection;
            this.services = services;
            Obsidian = services.GetRequiredService<ObsidianVaultService>();
            Auth = services.GetRequiredService<AuthService>();
            StorageQuota = services.GetRequiredService<ObsidianStorageQuotaService>();
        }

        public ObsidianVaultService Obsidian { get; }

        public AuthService Auth { get; }

        public ObsidianStorageQuotaService StorageQuota { get; }

        public async Task SetObsidianStorageLimitsAsync(long perAccountBytes, long totalBytes)
        {
            await using var db = await services.GetRequiredService<IDbContextFactory<SlogsDbContext>>().CreateDbContextAsync();
            await WriteSettingAsync(db, ObsidianStorageQuotaService.PerAccountLimitKey, perAccountBytes);
            await WriteSettingAsync(db, ObsidianStorageQuotaService.TotalCapacityKey, totalBytes);
        }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddHttpContextAccessor();
            services.AddDbContextFactory<SlogsDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<ObsidianVaultService>();
            services.AddScoped<AuthService>();
            services.AddScoped<ObsidianStorageQuotaService>();
            var provider = services.BuildServiceProvider();

            await using var db = await provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            await EnsureAdminRenameSupportTablesAsync(db);
            await EnsureSettingsTableAsync(db);
            db.Users.AddRange(
                new UserRecord
                {
                    UserName = "alice",
                    DisplayName = "Alice",
                    Password = "pw"
                },
                new UserRecord
                {
                    UserName = "bob",
                    DisplayName = "Bob",
                    Password = "pw"
                });
            await db.SaveChangesAsync();

            return new TestFixture(connection, provider);
        }

        private static async Task EnsureSettingsTableAsync(SlogsDbContext db)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "SlogsSettings" (
                    "Key" TEXT NOT NULL PRIMARY KEY,
                    "Value" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL
                );
                """);
        }

        private static async Task WriteSettingAsync(SlogsDbContext db, string key, long value)
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO "SlogsSettings" ("Key", "Value", "UpdatedAt")
                 VALUES ({key}, {value.ToString()}, {DateTime.UtcNow})
                 ON CONFLICT ("Key") DO UPDATE SET
                    "Value" = EXCLUDED."Value",
                    "UpdatedAt" = EXCLUDED."UpdatedAt";
                 """);
        }

        private static async Task EnsureAdminRenameSupportTablesAsync(SlogsDbContext db)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "PostImages" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "OwnerUserName" TEXT NOT NULL,
                    "PostId" TEXT NULL,
                    "Url" TEXT NOT NULL,
                    "FileName" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "LastReferencedAt" TEXT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "LlmWikiMcpAudits" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "OwnerUserName" TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "LlmWikiEntryEmbeddings" (
                    "EntryId" TEXT NOT NULL PRIMARY KEY,
                    "OwnerUserName" TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "LlmWikiEntryGraphNodes" (
                    "EntryId" TEXT NOT NULL,
                    "OwnerUserName" TEXT NOT NULL,
                    "NodeKey" TEXT NOT NULL,
                    PRIMARY KEY ("EntryId", "NodeKey")
                );
                """);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
