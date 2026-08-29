using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Slogs.Data;

public static class OrganizationClaimTypes
{
    public const string OrganizationId = "slogs:organization-id";
    public const string OrganizationRole = "slogs:organization-role";
    public const string ActorKind = "slogs:actor-kind";
    public const string PresenterUserName = "slogs:presenter-user";
    public const string TokenId = "slogs:token-id";
    public const string TokenScope = "slogs:token-scope";
    public const string GuidedSessionId = "slogs:guided-session-id";
}

public sealed record OrganizationActorContext(
    Guid OrganizationId,
    string ActorId,
    string ActorKind,
    string Role,
    IReadOnlySet<string> Scopes,
    string? PresenterUserName,
    Guid? TokenId,
    Guid? GuidedSessionId);

public sealed class OrganizationActorResolver(IDbContextFactory<OrganizationDbContext> dbFactory)
{
    public async Task<OrganizationActorContext> RequireAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        string requiredScope,
        CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new OrganizationAccessDeniedException("Organization authentication is required.");
        }

        var claimedOrganizationId = TryParseGuid(principal.FindFirstValue(OrganizationClaimTypes.OrganizationId));
        if (claimedOrganizationId is not null && claimedOrganizationId != organizationId)
        {
            throw new OrganizationAccessDeniedException("The authenticated organization does not match the requested organization.");
        }

        var actorKind = principal.FindFirstValue(OrganizationClaimTypes.ActorKind) ?? OrganizationActorKinds.User;
        var actorId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(OpenIddictConstants.Claims.Subject)
            ?? throw new OrganizationAccessDeniedException("The authenticated actor identifier is missing.");
        var presenter = principal.FindFirstValue(OrganizationClaimTypes.PresenterUserName);
        var tokenId = TryParseGuid(principal.FindFirstValue(OrganizationClaimTypes.TokenId));
        var guidedSessionId = TryParseGuid(principal.FindFirstValue(OrganizationClaimTypes.GuidedSessionId));

        if (actorKind.Equals(OrganizationActorKinds.Service, StringComparison.Ordinal))
        {
            var serviceScopes = ReadExplicitScopes(principal);
            RequireScope(serviceScopes, requiredScope);
            return new(
                organizationId,
                actorId,
                actorKind,
                "service",
                serviceScopes,
                presenter,
                tokenId,
                guidedSessionId);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var membership = await db.OrganizationMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.UserName == actorId
                    && x.Status == OrganizationMemberStatuses.Active,
                cancellationToken)
            ?? throw new OrganizationAccessDeniedException("An active organization membership is required.");

        var roleScopes = OrganizationRolePermissions.GetScopes(membership.Role);
        var explicitScopes = ReadExplicitScopes(principal);
        IReadOnlySet<string> effectiveScopes = explicitScopes.Count == 0
            ? roleScopes
            : roleScopes.Where(explicitScopes.Contains).ToHashSet(StringComparer.Ordinal);
        RequireScope(effectiveScopes, requiredScope);

        return new(
            organizationId,
            actorId,
            actorKind,
            membership.Role,
            effectiveScopes,
            presenter,
            tokenId,
            guidedSessionId);
    }

    private static IReadOnlySet<string> ReadExplicitScopes(ClaimsPrincipal principal)
    {
        var scopes = principal.FindAll(OrganizationClaimTypes.TokenScope)
            .Select(x => x.Value)
            .Concat(principal.FindAll(OpenIddictConstants.Claims.Scope)
                .SelectMany(x => x.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        return scopes;
    }

    private static void RequireScope(IReadOnlySet<string> scopes, string requiredScope)
    {
        if (!scopes.Contains(requiredScope))
        {
            throw new OrganizationAccessDeniedException($"The '{requiredScope}' organization scope is required.");
        }
    }

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;
}

public static class OrganizationRolePermissions
{
    private static readonly IReadOnlySet<string> MemberScopes = new HashSet<string>(StringComparer.Ordinal)
    {
        OrganizationTokenScopes.Read,
        OrganizationTokenScopes.Propose
    };

    private static readonly IReadOnlySet<string> ApproverScopes = MemberScopes
        .Concat([OrganizationTokenScopes.Approve, OrganizationTokenScopes.Reject])
        .ToHashSet(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> AdminScopes = ApproverScopes
        .Concat([
            OrganizationTokenScopes.MembersManage,
            OrganizationTokenScopes.SourcesManage,
            OrganizationTokenScopes.McpManage,
            OrganizationTokenScopes.MetricsRead,
            OrganizationTokenScopes.MetricsWrite
        ])
        .ToHashSet(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> OwnerScopes = OrganizationTokenScopes.All
        .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> GetScopes(string role)
        => role switch
        {
            OrganizationRoles.Owner => OwnerScopes,
            OrganizationRoles.Admin => AdminScopes,
            OrganizationRoles.Approver => ApproverScopes,
            OrganizationRoles.Member => MemberScopes,
            _ => throw new OrganizationValidationException($"Unsupported organization role '{role}'.")
        };
}

public sealed class OrganizationValidationException(string message) : InvalidOperationException(message);
public sealed class OrganizationAccessDeniedException(string message) : InvalidOperationException(message);
public sealed class OrganizationNotFoundException(string message) : InvalidOperationException(message);
public sealed class OrganizationConflictException(string message) : InvalidOperationException(message);
