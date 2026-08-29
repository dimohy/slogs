using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options) : DbContext(options)
{
    public const string Schema = "organization";

    public DbSet<OrganizationRecord> Organizations => Set<OrganizationRecord>();
    public DbSet<OrganizationMembershipRecord> OrganizationMemberships => Set<OrganizationMembershipRecord>();
    public DbSet<OrganizationUnitRecord> OrganizationUnits => Set<OrganizationUnitRecord>();
    public DbSet<OrganizationUnitMembershipRecord> OrganizationUnitMemberships => Set<OrganizationUnitMembershipRecord>();
    public DbSet<OrganizationMemoryRecord> OrganizationMemories => Set<OrganizationMemoryRecord>();
    public DbSet<OrganizationMemoryRevisionRecord> OrganizationMemoryRevisions => Set<OrganizationMemoryRevisionRecord>();
    public DbSet<OrganizationMemorySourceRecord> OrganizationMemorySources => Set<OrganizationMemorySourceRecord>();
    public DbSet<OrganizationConflictRecord> OrganizationConflicts => Set<OrganizationConflictRecord>();
    public DbSet<OrganizationServiceTokenRecord> OrganizationServiceTokens => Set<OrganizationServiceTokenRecord>();
    public DbSet<OrganizationMetricEventRecord> OrganizationMetricEvents => Set<OrganizationMetricEventRecord>();
    public DbSet<OrganizationAuditRecord> OrganizationAudits => Set<OrganizationAuditRecord>();
    public DbSet<OrganizationOidcClientRecord> OrganizationOidcClients => Set<OrganizationOidcClientRecord>();
    public DbSet<OrganizationGuidedSessionRecord> OrganizationGuidedSessions => Set<OrganizationGuidedSessionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.UseOpenIddict();

        modelBuilder.Entity<OrganizationRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Slug).HasMaxLength(80);
            entity.Property(x => x.DisplayName).HasMaxLength(160);
            entity.Property(x => x.EnvironmentLabel).HasMaxLength(80);
            entity.HasMany(x => x.Memberships).WithOne(x => x.Organization)
                .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Units).WithOne(x => x.Organization)
                .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Memories).WithOne(x => x.Organization)
                .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrganizationMembershipRecord>(entity =>
        {
            entity.HasKey(x => new { x.OrganizationId, x.UserName });
            entity.HasIndex(x => new { x.UserName, x.Status });
            entity.Property(x => x.UserName).HasMaxLength(80);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.Property(x => x.DisplayRole).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.InvitedBy).HasMaxLength(80);
        });

        modelBuilder.Entity<OrganizationUnitRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.ParentUnitId, x.NameKey }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.NameKey).HasMaxLength(120);
            entity.Property(x => x.Kind).HasMaxLength(32);
            entity.HasOne(x => x.Parent).WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrganizationUnitMembershipRecord>(entity =>
        {
            entity.HasKey(x => new { x.OrganizationId, x.UnitId, x.UserName });
            entity.HasIndex(x => new { x.OrganizationId, x.UserName });
            entity.Property(x => x.UserName).HasMaxLength(80);
            entity.HasOne(x => x.Unit).WithMany(x => x.Memberships)
                .HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrganizationMemoryRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.Slug }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.State, x.UpdatedAt });
            entity.HasIndex(x => new { x.OrganizationId, x.ScopeKind, x.ScopeKey, x.UpdatedAt });
            entity.HasIndex(x => new { x.OrganizationId, x.CategoryPath, x.UpdatedAt });
            entity.Property(x => x.Slug).HasMaxLength(160);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.TagsJson).HasColumnType("jsonb");
            entity.Property(x => x.CategoryPath).HasMaxLength(240);
            entity.Property(x => x.State).HasMaxLength(32);
            entity.Property(x => x.ScopeKind).HasMaxLength(40);
            entity.Property(x => x.ScopeKey).HasMaxLength(240);
            entity.Property(x => x.ProposedBy).HasMaxLength(80);
            entity.Property(x => x.ApprovedBy).HasMaxLength(80);
            entity.Property(x => x.DecisionReason).HasMaxLength(1000);
            entity.HasOne(x => x.SupersedesMemory).WithMany()
                .HasForeignKey(x => x.SupersedesMemoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Revisions).WithOne(x => x.Memory)
                .HasForeignKey(x => x.MemoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Sources).WithOne(x => x.Memory)
                .HasForeignKey(x => x.MemoryId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrganizationMemoryRevisionRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.MemoryId, x.Revision }).IsUnique();
            entity.Property(x => x.Action).HasMaxLength(40);
            entity.Property(x => x.ActorUserName).HasMaxLength(80);
            entity.Property(x => x.PresenterUserName).HasMaxLength(80);
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.TagsJson).HasColumnType("jsonb");
            entity.Property(x => x.CategoryPath).HasMaxLength(240);
            entity.Property(x => x.State).HasMaxLength(32);
            entity.Property(x => x.ScopeKind).HasMaxLength(40);
            entity.Property(x => x.ScopeKey).HasMaxLength(240);
        });

        modelBuilder.Entity<OrganizationMemorySourceRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.SourceUri, x.ContentHash }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.State, x.LastCheckedAt });
            entity.Property(x => x.Title).HasMaxLength(300);
            entity.Property(x => x.SourceUri).HasMaxLength(1000);
            entity.Property(x => x.SourceKind).HasMaxLength(80);
            entity.Property(x => x.Grade).HasMaxLength(16);
            entity.Property(x => x.State).HasMaxLength(32);
            entity.Property(x => x.ContentHash).HasMaxLength(128);
            entity.Property(x => x.CreatedBy).HasMaxLength(120);
            entity.Property(x => x.FailureMessage).HasMaxLength(1000);
        });

        modelBuilder.Entity<OrganizationConflictRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.State, x.CreatedAt });
            entity.Property(x => x.FieldName).HasMaxLength(160);
            entity.Property(x => x.State).HasMaxLength(32);
            entity.Property(x => x.Resolution).HasMaxLength(1000);
            entity.Property(x => x.ResolvedBy).HasMaxLength(80);
        });

        modelBuilder.Entity<OrganizationServiceTokenRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.RevokedAt });
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.TokenHash).HasMaxLength(128);
            entity.Property(x => x.TokenPrefix).HasMaxLength(32);
            entity.Property(x => x.ScopesJson).HasColumnType("jsonb");
            entity.Property(x => x.CreatedBy).HasMaxLength(80);
        });

        modelBuilder.Entity<OrganizationMetricEventRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.UnitId, x.MetricKind, x.OccurredAt });
            entity.Property(x => x.ActorKey).HasMaxLength(128);
            entity.Property(x => x.MetricKind).HasMaxLength(80);
            entity.Property(x => x.Value).HasPrecision(18, 4);
        });

        modelBuilder.Entity<OrganizationAuditRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
            entity.HasIndex(x => new { x.OrganizationId, x.ActorId, x.CreatedAt });
            entity.Property(x => x.ActorKind).HasMaxLength(32);
            entity.Property(x => x.ActorId).HasMaxLength(120);
            entity.Property(x => x.PresenterUserName).HasMaxLength(80);
            entity.Property(x => x.Action).HasMaxLength(120);
            entity.Property(x => x.TargetType).HasMaxLength(80);
            entity.Property(x => x.TargetId).HasMaxLength(160);
            entity.Property(x => x.Outcome).HasMaxLength(32);
            entity.Property(x => x.DetailJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<OrganizationOidcClientRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ClientId).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.RevokedAt });
            entity.Property(x => x.ApplicationId).HasMaxLength(100);
            entity.Property(x => x.ClientId).HasMaxLength(120);
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.RedirectUrisJson).HasColumnType("jsonb");
            entity.Property(x => x.ScopesJson).HasColumnType("jsonb");
            entity.Property(x => x.CreatedBy).HasMaxLength(80);
        });

        modelBuilder.Entity<OrganizationGuidedSessionRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.PresenterUserName, x.EndedAt });
            entity.Property(x => x.PresenterUserName).HasMaxLength(80);
            entity.Property(x => x.ActiveRoleUserName).HasMaxLength(80);
        });
    }
}

public sealed class OrganizationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string EnvironmentLabel { get; set; } = string.Empty;
    public int MinimumAggregateCohort { get; set; } = 5;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<OrganizationMembershipRecord> Memberships { get; set; } = [];
    public List<OrganizationUnitRecord> Units { get; set; } = [];
    public List<OrganizationMemoryRecord> Memories { get; set; } = [];
}

public sealed class OrganizationMembershipRecord
{
    public Guid OrganizationId { get; set; }
    public OrganizationRecord? Organization { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = OrganizationRoles.Member;
    public string DisplayRole { get; set; } = string.Empty;
    public string Status { get; set; } = OrganizationMemberStatuses.Invited;
    public bool IsSyntheticAccount { get; set; }
    public string InvitedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class OrganizationUnitRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public OrganizationRecord? Organization { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public string Kind { get; set; } = OrganizationUnitKinds.Department;
    public Guid? ParentUnitId { get; set; }
    public OrganizationUnitRecord? Parent { get; set; }
    public List<OrganizationUnitRecord> Children { get; set; } = [];
    public List<OrganizationUnitMembershipRecord> Memberships { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class OrganizationUnitMembershipRecord
{
    public Guid OrganizationId { get; set; }
    public Guid UnitId { get; set; }
    public OrganizationUnitRecord? Unit { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class OrganizationMemoryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public OrganizationRecord? Organization { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SourcePrompt { get; set; } = string.Empty;
    public string TagsJson { get; set; } = "[]";
    public string CategoryPath { get; set; } = "general";
    public int CategoryDepth { get; set; } = 1;
    public string State { get; set; } = OrganizationMemoryStates.Draft;
    public string ScopeKind { get; set; } = OrganizationMemoryScopes.PersonalCandidate;
    public string? ScopeKey { get; set; }
    public string ProposedBy { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? DecisionReason { get; set; }
    public Guid? SupersedesMemoryId { get; set; }
    public OrganizationMemoryRecord? SupersedesMemory { get; set; }
    public int Revision { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<OrganizationMemoryRevisionRecord> Revisions { get; set; } = [];
    public List<OrganizationMemorySourceRecord> Sources { get; set; } = [];
}

public sealed class OrganizationMemoryRevisionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MemoryId { get; set; }
    public OrganizationMemoryRecord? Memory { get; set; }
    public int Revision { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorUserName { get; set; } = string.Empty;
    public string? PresenterUserName { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SourcePrompt { get; set; } = string.Empty;
    public string TagsJson { get; set; } = "[]";
    public string CategoryPath { get; set; } = "general";
    public string State { get; set; } = OrganizationMemoryStates.Draft;
    public string ScopeKind { get; set; } = OrganizationMemoryScopes.PersonalCandidate;
    public string? ScopeKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class OrganizationMemorySourceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? MemoryId { get; set; }
    public OrganizationMemoryRecord? Memory { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SourceUri { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string Grade { get; set; } = OrganizationSourceGrades.UnverifiedCandidate;
    public string State { get; set; } = OrganizationSourceStates.Pending;
    public string ContentHash { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
    public string? Excerpt { get; set; }
    public string? FailureMessage { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

public sealed class OrganizationConflictRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string LeftValue { get; set; } = string.Empty;
    public string RightValue { get; set; } = string.Empty;
    public Guid? LeftMemoryId { get; set; }
    public Guid? RightMemoryId { get; set; }
    public Guid? LeftSourceId { get; set; }
    public Guid? RightSourceId { get; set; }
    public string State { get; set; } = OrganizationConflictStates.Pending;
    public string? Resolution { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

public sealed class OrganizationServiceTokenRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string TokenPrefix { get; set; } = string.Empty;
    public string ScopesJson { get; set; } = "[]";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public sealed class OrganizationMetricEventRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? UnitId { get; set; }
    public string ActorKey { get; set; } = string.Empty;
    public string MetricKind { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public bool IsDemoAssumption { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public sealed class OrganizationAuditRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string ActorKind { get; set; } = OrganizationActorKinds.User;
    public string ActorId { get; set; } = string.Empty;
    public string? PresenterUserName { get; set; }
    public Guid? TokenId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string Outcome { get; set; } = "success";
    public string DetailJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class OrganizationOidcClientRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string ApplicationId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RedirectUrisJson { get; set; } = "[]";
    public string ScopesJson { get; set; } = "[]";
    public int SecretVersion { get; set; } = 1;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
}

public sealed class OrganizationGuidedSessionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string PresenterUserName { get; set; } = string.Empty;
    public string ActiveRoleUserName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
