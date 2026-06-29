using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class SlogsDbContext(DbContextOptions<SlogsDbContext> options) : DbContext(options)
{
    public DbSet<PostRecord> Posts => Set<PostRecord>();

    public DbSet<PostRevisionRecord> PostRevisions => Set<PostRevisionRecord>();

    public DbSet<CommentRecord> Comments => Set<CommentRecord>();

    public DbSet<UserRecord> Users => Set<UserRecord>();

    public DbSet<FollowRecord> Follows => Set<FollowRecord>();

    public DbSet<ExternalLoginRecord> ExternalLogins => Set<ExternalLoginRecord>();

    public DbSet<LlmWikiEntryRecord> LlmWikiEntries => Set<LlmWikiEntryRecord>();

    public DbSet<LlmWikiEntrySourceRecord> LlmWikiEntrySources => Set<LlmWikiEntrySourceRecord>();

    public DbSet<LlmWikiMcpTokenRecord> LlmWikiMcpTokens => Set<LlmWikiMcpTokenRecord>();

    public DbSet<ObsidianVaultRecord> ObsidianVaults => Set<ObsidianVaultRecord>();

    public DbSet<ObsidianVaultFileRecord> ObsidianVaultFiles => Set<ObsidianVaultFileRecord>();

    public DbSet<ObsidianVaultClientRecord> ObsidianVaultClients => Set<ObsidianVaultClientRecord>();

    public DbSet<ObsidianVaultFileVersionRecord> ObsidianVaultFileVersions => Set<ObsidianVaultFileVersionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PostRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => x.Author);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Slug).HasMaxLength(220);
            entity.Property(x => x.Author).HasMaxLength(80);
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.ThumbnailUrl).HasMaxLength(500);
            entity.Property(x => x.IsDraft).HasDefaultValue(false);
            entity.Property(x => x.TagsJson).HasColumnType("jsonb");
            entity.Property(x => x.SeriesJson).HasColumnType("jsonb");
            entity.Property(x => x.LikedByJson).HasColumnType("jsonb");
            entity.Property(x => x.BookmarkedByJson).HasColumnType("jsonb");
            entity.HasMany(x => x.Comments)
                .WithOne(x => x.Post)
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Revisions)
                .WithOne(x => x.Post)
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PostRevisionRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PostId);
            entity.HasIndex(x => new { x.PostId, x.RevisionNumber }).IsUnique();
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.ThumbnailUrl).HasMaxLength(500);
            entity.Property(x => x.Author).HasMaxLength(80);
            entity.Property(x => x.TagsJson).HasColumnType("jsonb");
            entity.Property(x => x.SeriesJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CommentRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PostId);
            entity.HasIndex(x => x.ParentCommentId);
            entity.Property(x => x.Author).HasMaxLength(80);
            entity.Property(x => x.AuthorNormalized).HasMaxLength(80);
            entity.Property(x => x.Content).HasMaxLength(1000);
        });

        modelBuilder.Entity<UserRecord>(entity =>
        {
            entity.HasKey(x => x.UserName);
            entity.Property(x => x.UserName).HasMaxLength(80);
            entity.Property(x => x.DisplayName).HasMaxLength(80);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.Password).HasMaxLength(200);
            entity.Property(x => x.ProfileImageUrl).HasMaxLength(500);
            entity.Property(x => x.Bio).HasMaxLength(280);
        });

        modelBuilder.Entity<FollowRecord>(entity =>
        {
            entity.HasKey(x => new { x.FollowerUserName, x.TargetUserName });
            entity.Property(x => x.FollowerUserName).HasMaxLength(80);
            entity.Property(x => x.TargetUserName).HasMaxLength(80);
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(x => x.FollowerUserName)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(x => x.TargetUserName)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalLoginRecord>(entity =>
        {
            entity.HasKey(x => new { x.Provider, x.ProviderUserId });
            entity.HasIndex(x => x.UserName);
            entity.HasIndex(x => new { x.Provider, x.Email });
            entity.Property(x => x.Provider).HasMaxLength(40);
            entity.Property(x => x.ProviderUserId).HasMaxLength(200);
            entity.Property(x => x.UserName).HasMaxLength(80);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(x => x.UserName)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LlmWikiEntryRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OwnerUserName, x.Slug }).IsUnique();
            entity.HasIndex(x => x.OwnerUserName);
            entity.HasIndex(x => x.UpdatedAt);
            entity.Property(x => x.OwnerUserName).HasMaxLength(80);
            entity.Property(x => x.Slug).HasMaxLength(160);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.CategoryPath).HasMaxLength(240);
            entity.Property(x => x.TagsJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.OwnerUserName, x.CategoryPath, x.UpdatedAt });
            entity.HasIndex(x => new { x.OwnerUserName, x.IsPublic, x.UpdatedAt });
            entity.HasIndex(x => new { x.OwnerUserName, x.IsPublic, x.CategoryPath, x.UpdatedAt });
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserName)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Sources)
                .WithOne(x => x.Entry)
                .HasForeignKey(x => x.EntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LlmWikiEntrySourceRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.EntryId, x.CreatedAt });
            entity.HasIndex(x => new { x.OwnerUserName, x.CreatedAt });
            entity.Property(x => x.OwnerUserName).HasMaxLength(80);
            entity.Property(x => x.Action).HasMaxLength(40);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.CategoryPath).HasMaxLength(240);
            entity.HasOne(x => x.Entry)
                .WithMany(x => x.Sources)
                .HasForeignKey(x => x.EntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LlmWikiMcpTokenRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.OwnerUserName);
            entity.Property(x => x.OwnerUserName).HasMaxLength(80);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.TokenHash).HasMaxLength(128);
            entity.Property(x => x.TokenPrefix).HasMaxLength(32);
            entity.Property(x => x.ScopesJson).HasColumnType("jsonb");
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserName)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ObsidianVaultRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OwnerUserName, x.NameKey }).IsUnique();
            entity.HasIndex(x => new { x.OwnerUserName, x.UpdatedAt });
            entity.Property(x => x.OwnerUserName).HasMaxLength(80);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.NameKey).HasMaxLength(120);
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserName)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Files)
                .WithOne(x => x.Vault)
                .HasForeignKey(x => x.VaultId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Clients)
                .WithOne(x => x.Vault)
                .HasForeignKey(x => x.VaultId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ObsidianVaultFileRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.VaultId, x.PathKey }).IsUnique();
            entity.HasIndex(x => new { x.OwnerUserName, x.VaultId, x.Version });
            entity.HasIndex(x => new { x.OwnerUserName, x.VaultId, x.IsDeleted, x.UpdatedAt });
            entity.Property(x => x.OwnerUserName).HasMaxLength(80);
            entity.Property(x => x.Path).HasMaxLength(700);
            entity.Property(x => x.PathKey).HasMaxLength(700);
            entity.Property(x => x.ContentHash).HasMaxLength(64);
            entity.Property(x => x.MediaType).HasMaxLength(120);
            entity.Property(x => x.Scope).HasMaxLength(40);
            entity.Property(x => x.Kind).HasMaxLength(40);
            entity.Property(x => x.Encoding).HasMaxLength(20);
            entity.Property(x => x.MetadataJson).HasColumnType("jsonb");
            entity.Property(x => x.LastClientId).HasMaxLength(120);
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserName)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ObsidianVaultClientRecord>(entity =>
        {
            entity.HasKey(x => new { x.VaultId, x.ClientId });
            entity.HasIndex(x => new { x.OwnerUserName, x.VaultId, x.LastSeenAt });
            entity.Property(x => x.OwnerUserName).HasMaxLength(80);
            entity.Property(x => x.ClientId).HasMaxLength(120);
            entity.Property(x => x.ClientName).HasMaxLength(120);
            entity.Property(x => x.ClientKind).HasMaxLength(80);
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserName)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ObsidianVaultFileVersionRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OwnerUserName, x.VaultId, x.PathKey, x.Version }).IsUnique();
            entity.HasIndex(x => new { x.OwnerUserName, x.VaultId, x.Version });
            entity.Property(x => x.OwnerUserName).HasMaxLength(80);
            entity.Property(x => x.Path).HasMaxLength(700);
            entity.Property(x => x.PathKey).HasMaxLength(700);
            entity.Property(x => x.ContentHash).HasMaxLength(64);
            entity.Property(x => x.MediaType).HasMaxLength(120);
            entity.Property(x => x.Scope).HasMaxLength(40);
            entity.Property(x => x.Kind).HasMaxLength(40);
            entity.Property(x => x.Encoding).HasMaxLength(20);
            entity.Property(x => x.MetadataJson).HasColumnType("jsonb");
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserName)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class PostRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string ThumbnailUrl { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDraft { get; set; }

    public int ReadTimeMinutes { get; set; }

    public int ViewCount { get; set; }

    public string TagsJson { get; set; } = "[]";

    public string SeriesJson { get; set; } = "[]";

    public string LikedByJson { get; set; } = "[]";

    public string BookmarkedByJson { get; set; } = "[]";

    public List<CommentRecord> Comments { get; set; } = [];

    public List<PostRevisionRecord> Revisions { get; set; } = [];
}

public sealed class PostRevisionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PostId { get; set; }

    public PostRecord? Post { get; set; }

    public int RevisionNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string ThumbnailUrl { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string TagsJson { get; set; } = "[]";

    public string SeriesJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string Author { get; set; } = string.Empty;
}

public sealed class CommentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PostId { get; set; }

    public PostRecord? Post { get; set; }

    public string Author { get; set; } = string.Empty;

    public string AuthorNormalized { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid? ParentCommentId { get; set; }
}

public sealed class UserRecord
{
    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ProfileImageUrl { get; set; } = string.Empty;

    public string Bio { get; set; } = string.Empty;

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProfileUpdatedAt { get; set; }
}

public sealed class FollowRecord
{
    public string FollowerUserName { get; set; } = string.Empty;

    public string TargetUserName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ExternalLoginRecord
{
    public string Provider { get; set; } = string.Empty;

    public string ProviderUserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;
}

public sealed class LlmWikiEntryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string OwnerUserName { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string SourcePrompt { get; set; } = string.Empty;

    public string TagsJson { get; set; } = "[]";

    public string CategoryPath { get; set; } = "general";

    public int CategoryDepth { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastAccessedAt { get; set; }

    public int AccessCount { get; set; }

    public bool IsPublic { get; set; }

    public DateTime? PublishedAt { get; set; }

    public List<LlmWikiEntrySourceRecord> Sources { get; set; } = [];
}

public sealed class LlmWikiEntrySourceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EntryId { get; set; }

    public LlmWikiEntryRecord? Entry { get; set; }

    public string OwnerUserName { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string? Content { get; set; }

    public string? Title { get; set; }

    public string? Tags { get; set; }

    public string? CategoryPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class LlmWikiMcpTokenRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string OwnerUserName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public string TokenPrefix { get; set; } = string.Empty;

    public string ScopesJson { get; set; } = "[\"mcp\"]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}

public sealed class ObsidianVaultRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string OwnerUserName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string NameKey { get; set; } = string.Empty;

    public long CurrentVersion { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ObsidianVaultFileRecord> Files { get; set; } = [];

    public List<ObsidianVaultClientRecord> Clients { get; set; } = [];
}

public sealed class ObsidianVaultFileRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid VaultId { get; set; }

    public ObsidianVaultRecord? Vault { get; set; }

    public string OwnerUserName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string PathKey { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public string MediaType { get; set; } = "text/markdown";

    public string Scope { get; set; } = ObsidianSyncScopes.Markdown;

    public string Kind { get; set; } = ObsidianVaultFileKinds.Markdown;

    public string Encoding { get; set; } = ObsidianVaultContentEncodings.Utf8;

    public string MetadataJson { get; set; } = "{}";

    public string LastClientId { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public long Version { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }
}

public sealed class ObsidianVaultClientRecord
{
    public Guid VaultId { get; set; }

    public ObsidianVaultRecord? Vault { get; set; }

    public string OwnerUserName { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public string ClientKind { get; set; } = string.Empty;

    public long LastSeenVersion { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}

public sealed class ObsidianVaultFileVersionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FileId { get; set; }

    public Guid VaultId { get; set; }

    public string OwnerUserName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string PathKey { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public string MediaType { get; set; } = "text/markdown";

    public string Scope { get; set; } = ObsidianSyncScopes.Markdown;

    public string Kind { get; set; } = ObsidianVaultFileKinds.Markdown;

    public string Encoding { get; set; } = ObsidianVaultContentEncodings.Utf8;

    public string MetadataJson { get; set; } = "{}";

    public long SizeBytes { get; set; }

    public long Version { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }
}
