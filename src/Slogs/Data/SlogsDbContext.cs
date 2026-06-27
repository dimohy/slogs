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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}
