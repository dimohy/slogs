namespace Slogs.Data;

public static class OrganizationRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Approver = "approver";
    public const string Member = "member";

    public static IReadOnlyList<string> All { get; } = [Owner, Admin, Approver, Member];
}

public static class OrganizationMemberStatuses
{
    public const string Invited = "invited";
    public const string Active = "active";
    public const string Suspended = "suspended";
}

public static class OrganizationUnitKinds
{
    public const string Department = "department";
    public const string Team = "team";
}

public static class OrganizationMemoryStates
{
    public const string Detected = "detected";
    public const string Draft = "draft";
    public const string ReviewRequested = "review_requested";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Active = "active";
    public const string Superseded = "superseded";
    public const string Withdrawn = "withdrawn";
}

public static class OrganizationMemoryScopes
{
    public const string PersonalCandidate = "personal_candidate";
    public const string Team = "team";
    public const string Organization = "organization";
    public const string Customer = "customer";
    public const string Equipment = "equipment";

    public static IReadOnlyList<string> All { get; } =
        [PersonalCandidate, Team, Organization, Customer, Equipment];
}

public static class OrganizationSourceGrades
{
    public const string Official = "A";
    public const string ManufacturerOrContract = "B";
    public const string TrustedExternal = "C";
    public const string UnverifiedCandidate = "D";
    public const string DemoAssumption = "DEMO";
}

public static class OrganizationSourceStates
{
    public const string Pending = "pending";
    public const string Verified = "verified";
    public const string Failed = "failed";
    public const string Superseded = "superseded";
}

public static class OrganizationConflictStates
{
    public const string Pending = "pending";
    public const string Resolved = "resolved";
    public const string Dismissed = "dismissed";
}

public static class OrganizationTokenScopes
{
    public const string Read = "org_wiki.read";
    public const string Propose = "org_wiki.propose";
    public const string Approve = "org_wiki.approve";
    public const string Reject = "org_wiki.reject";
    public const string MembersManage = "org.members.manage";
    public const string SourcesManage = "org.sources.manage";
    public const string McpManage = "org.mcp.manage";
    public const string OidcManage = "org.oidc.manage";
    public const string MetricsRead = "org.metrics.read";
    public const string MetricsWrite = "org.metrics.write";
    public const string GuidedSession = "org.guided_session";

    public static IReadOnlyList<string> All { get; } =
    [
        Read,
        Propose,
        Approve,
        Reject,
        MembersManage,
        SourcesManage,
        McpManage,
        OidcManage,
        MetricsRead,
        MetricsWrite,
        GuidedSession
    ];
}

public static class OrganizationActorKinds
{
    public const string User = "user";
    public const string Service = "service";
    public const string GuidedRole = "guided_role";
}

public sealed record OrganizationCreateRequest(
    string Slug,
    string DisplayName,
    string OwnerUserName,
    string EnvironmentLabel,
    int MinimumAggregateCohort = 5);

public sealed record OrganizationUpdateRequest(
    string DisplayName,
    string EnvironmentLabel,
    int MinimumAggregateCohort);

public sealed record OrganizationResponse(
    Guid Id,
    string Slug,
    string DisplayName,
    string EnvironmentLabel,
    int MinimumAggregateCohort,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record OrganizationMembershipUpsertRequest(
    string UserName,
    string Role,
    string DisplayRole,
    bool IsSyntheticAccount = false);

public sealed record OrganizationMembershipResponse(
    Guid OrganizationId,
    string UserName,
    string Role,
    string DisplayRole,
    string Status,
    bool IsSyntheticAccount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record OrganizationUnitCreateRequest(string Name, string Kind, Guid? ParentUnitId);

public sealed record OrganizationUnitResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Kind,
    Guid? ParentUnitId,
    DateTime CreatedAt);

public sealed record OrganizationUnitMembershipRequest(string UserName);

public sealed record OrganizationMemoryDraftRequest(
    string Title,
    string Summary,
    string Content,
    string SourcePrompt,
    IReadOnlyList<string> Tags,
    string CategoryPath,
    string ScopeKind,
    string? ScopeKey,
    Guid? SupersedesMemoryId = null,
    string? ProposalReason = null);

public sealed record OrganizationMemoryDecisionRequest(string Reason, string? CorrectedContent = null);

public sealed record OrganizationMemoryResponse(
    Guid Id,
    Guid OrganizationId,
    string Slug,
    string Title,
    string Summary,
    string Content,
    string SourcePrompt,
    IReadOnlyList<string> Tags,
    string CategoryPath,
    int CategoryDepth,
    string State,
    string ScopeKind,
    string? ScopeKey,
    string ProposedBy,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    string? DecisionReason,
    Guid? SupersedesMemoryId,
    int Revision,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<OrganizationMemorySourceResponse> Sources);

public sealed record OrganizationMemorySummaryResponse(
    Guid Id,
    string Slug,
    string Title,
    string Summary,
    string CategoryPath,
    string State,
    string ScopeKind,
    string? ScopeKey,
    string ProposedBy,
    string? ApprovedBy,
    DateTime UpdatedAt,
    int? RelevancePercent = null);

public sealed record OrganizationMemoryRecallResponse(
    Guid Id,
    string Slug,
    string Title,
    string Summary,
    string Content,
    string CategoryPath,
    string State,
    string ScopeKind,
    string? ScopeKey,
    string ProposedBy,
    string? ApprovedBy,
    DateTime UpdatedAt,
    int? RelevancePercent = null);

public sealed record OrganizationMemoryRevisionResponse(
    Guid Id,
    Guid MemoryId,
    int Revision,
    string Action,
    string ActorUserName,
    string? PresenterUserName,
    string Reason,
    string State,
    string ScopeKind,
    string? ScopeKey,
    string Content,
    DateTime CreatedAt);

public sealed record OrganizationMemorySourceIngestRequest(
    string Title,
    string SourceUri,
    string SourceKind,
    string Grade,
    string ContentHash,
    DateTime CapturedAt,
    string? Excerpt,
    string? FailureMessage,
    Guid? MemoryId = null);

public sealed record OrganizationMemorySourceResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? MemoryId,
    string Title,
    string SourceUri,
    string SourceKind,
    string Grade,
    string State,
    string ContentHash,
    DateTime CapturedAt,
    DateTime LastCheckedAt,
    string? Excerpt,
    string? FailureMessage);

public sealed record OrganizationConflictCreateRequest(
    string FieldName,
    string LeftValue,
    string RightValue,
    Guid? LeftMemoryId,
    Guid? RightMemoryId,
    Guid? LeftSourceId,
    Guid? RightSourceId);

public sealed record OrganizationConflictDecisionRequest(string Resolution, Guid? SelectedMemoryId = null);

public sealed record OrganizationConflictResponse(
    Guid Id,
    Guid OrganizationId,
    string FieldName,
    string LeftValue,
    string RightValue,
    Guid? LeftMemoryId,
    Guid? RightMemoryId,
    Guid? LeftSourceId,
    Guid? RightSourceId,
    string State,
    string? Resolution,
    string? ResolvedBy,
    DateTime CreatedAt,
    DateTime? ResolvedAt);

public sealed record OrganizationServiceTokenCreateRequest(
    string Name,
    IReadOnlyList<string> Scopes,
    DateTime? ExpiresAt);

public sealed record OrganizationServiceTokenResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string TokenPrefix,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    bool IsRevoked);

public sealed record OrganizationServiceTokenCreatedResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string TokenPrefix,
    string Token,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

public sealed record OrganizationMetricEventRequest(
    string ActorKey,
    string MetricKind,
    decimal Value,
    Guid? UnitId,
    bool IsDemoAssumption,
    DateTime OccurredAt);

public sealed record OrganizationMetricSummaryResponse(
    Guid OrganizationId,
    Guid? UnitId,
    string MetricKind,
    decimal Value,
    int CohortSize,
    int MinimumCohort,
    bool IsSuppressed,
    bool RolledUpToParent,
    bool IsDemoAssumption);

public sealed record OrganizationAuditResponse(
    Guid Id,
    Guid OrganizationId,
    string ActorKind,
    string ActorId,
    string? PresenterUserName,
    string Action,
    string TargetType,
    string? TargetId,
    string Outcome,
    DateTime CreatedAt);

public sealed record OrganizationOidcClientCreateRequest(
    string ClientId,
    string DisplayName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> Scopes);

public sealed record OrganizationOidcClientResponse(
    Guid Id,
    Guid OrganizationId,
    string ClientId,
    string DisplayName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> Scopes,
    int SecretVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsRevoked);

public sealed record OrganizationOidcClientCreatedResponse(
    OrganizationOidcClientResponse Client,
    string ClientSecret);

public sealed record OrganizationGuidedSessionCreateRequest(string ActiveRoleUserName, int DurationMinutes = 60);

public sealed record OrganizationGuidedSessionSwitchRequest(string ActiveRoleUserName);

public sealed record OrganizationGuidedAccessStartRequest(
    string ClientId,
    string ClientSecret,
    string ActiveRoleUserName,
    int DurationMinutes = 120);

public sealed record OrganizationGuidedAccessSwitchRequest(
    string ClientId,
    string ClientSecret,
    Guid SessionId,
    string ActiveRoleUserName);

public sealed record OrganizationGuidedAccessResponse(
    OrganizationGuidedSessionResponse Session,
    string AccessToken);

public sealed record OrganizationGuidedSessionResponse(
    Guid Id,
    Guid OrganizationId,
    string PresenterUserName,
    string ActiveRoleUserName,
    string ActiveRole,
    DateTime StartedAt,
    DateTime ExpiresAt,
    DateTime? EndedAt);
