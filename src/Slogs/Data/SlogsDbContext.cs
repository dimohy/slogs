using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class SlogsDbContext(DbContextOptions<SlogsDbContext> options) : DbContext(options)
{
    public DbSet<PostRecord> Posts => Set<PostRecord>();

    public DbSet<CommentRecord> Comments => Set<CommentRecord>();

    public DbSet<UserRecord> Users => Set<UserRecord>();

    public DbSet<FollowRecord> Follows => Set<FollowRecord>();

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
            entity.Property(x => x.TagsJson).HasColumnType("jsonb");
            entity.Property(x => x.SeriesJson).HasColumnType("jsonb");
            entity.Property(x => x.LikedByJson).HasColumnType("jsonb");
            entity.Property(x => x.BookmarkedByJson).HasColumnType("jsonb");
            entity.HasMany(x => x.Comments)
                .WithOne(x => x.Post)
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Cascade);
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

    public int ReadTimeMinutes { get; set; }

    public int ViewCount { get; set; }

    public string TagsJson { get; set; } = "[]";

    public string SeriesJson { get; set; } = "[]";

    public string LikedByJson { get; set; } = "[]";

    public string BookmarkedByJson { get; set; } = "[]";

    public List<CommentRecord> Comments { get; set; } = [];
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

    public string Password { get; set; } = string.Empty;

    public string ProfileImageUrl { get; set; } = string.Empty;

    public string Bio { get; set; } = string.Empty;

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

public sealed class FollowRecord
{
    public string FollowerUserName { get; set; } = string.Empty;

    public string TargetUserName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
