using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Slogs.Data;

public sealed class OrganizationGuidedAccessService(
    IDbContextFactory<OrganizationDbContext> dbFactory,
    OrganizationOidcClientService clientService,
    IDataProtectionProvider dataProtection)
{
    public const string TokenPrefix = "slogs_guided_";
    private const string TokenPurpose = "Slogs.Organization.GuidedAccess.v1";

    public async Task<OrganizationGuidedAccessResponse> StartAsync(
        OrganizationGuidedAccessStartRequest request,
        CancellationToken cancellationToken = default)
    {
        var binding = await RequireGuidedClientAsync(request.ClientId, request.ClientSecret, cancellationToken);
        var duration = Math.Clamp(request.DurationMinutes, 5, 120);
        var presenter = $"client:{binding.ClientId}";
        var now = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var membership = await RequireSyntheticMemberAsync(db, binding.OrganizationId, request.ActiveRoleUserName, cancellationToken);
        await db.OrganizationGuidedSessions
            .Where(x => x.OrganizationId == binding.OrganizationId
                && x.PresenterUserName == presenter
                && x.EndedAt == null
                && x.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EndedAt, now), cancellationToken);
        var session = new OrganizationGuidedSessionRecord
        {
            OrganizationId = binding.OrganizationId,
            PresenterUserName = presenter,
            ActiveRoleUserName = membership.UserName,
            StartedAt = now,
            ExpiresAt = now.AddMinutes(duration)
        };
        db.OrganizationGuidedSessions.Add(session);
        db.OrganizationAudits.Add(CreateAudit(session, "organization.guided-access.start", membership.Role));
        await db.SaveChangesAsync(cancellationToken);
        return CreateResponse(session, membership.Role);
    }

    public async Task<OrganizationGuidedAccessResponse> SwitchAsync(
        OrganizationGuidedAccessSwitchRequest request,
        CancellationToken cancellationToken = default)
    {
        var binding = await RequireGuidedClientAsync(request.ClientId, request.ClientSecret, cancellationToken);
        var presenter = $"client:{binding.ClientId}";
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.OrganizationGuidedSessions.FirstOrDefaultAsync(
            x => x.Id == request.SessionId
                && x.OrganizationId == binding.OrganizationId
                && x.PresenterUserName == presenter
                && x.EndedAt == null,
            cancellationToken)
            ?? throw new OrganizationNotFoundException("Active guided access session not found.");
        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            throw new OrganizationAccessDeniedException("The guided access session has expired.");
        }

        var membership = await RequireSyntheticMemberAsync(db, binding.OrganizationId, request.ActiveRoleUserName, cancellationToken);
        session.ActiveRoleUserName = membership.UserName;
        db.OrganizationAudits.Add(CreateAudit(session, "organization.guided-access.switch", membership.Role));
        await db.SaveChangesAsync(cancellationToken);
        return CreateResponse(session, membership.Role);
    }

    public async Task<ClaimsPrincipal?> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        GuidedAccessTicket? ticket;
        try
        {
            var json = dataProtection.CreateProtector(TokenPurpose).Unprotect(token[TokenPrefix.Length..]);
            ticket = JsonSerializer.Deserialize<GuidedAccessTicket>(json);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (ticket is null || ticket.ExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.OrganizationGuidedSessions.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == ticket.SessionId
                && x.OrganizationId == ticket.OrganizationId
                && x.PresenterUserName == ticket.PresenterUserName
                && x.ActiveRoleUserName == ticket.ActiveRoleUserName
                && x.EndedAt == null
                && x.ExpiresAt > DateTime.UtcNow,
            cancellationToken);
        if (session is null)
        {
            return null;
        }

        var membership = await RequireSyntheticMemberAsync(db, ticket.OrganizationId, ticket.ActiveRoleUserName, cancellationToken);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, membership.UserName),
            new(ClaimTypes.Name, membership.UserName),
            new(ClaimTypes.Role, membership.Role),
            new(OrganizationClaimTypes.OrganizationId, ticket.OrganizationId.ToString()),
            new(OrganizationClaimTypes.OrganizationRole, membership.Role),
            new(OrganizationClaimTypes.ActorKind, OrganizationActorKinds.GuidedRole),
            new(OrganizationClaimTypes.PresenterUserName, ticket.PresenterUserName),
            new(OrganizationClaimTypes.GuidedSessionId, ticket.SessionId.ToString())
        };
        claims.AddRange(OrganizationRolePermissions.GetScopes(membership.Role)
            .Select(scope => new Claim(OrganizationClaimTypes.TokenScope, scope)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "SlogsGuidedAccess"));
    }

    private async Task<OrganizationOidcClientRecord> RequireGuidedClientAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var binding = await clientService.RequireAuthenticatedBindingAsync(clientId, clientSecret, cancellationToken);
        var scopes = JsonSerializer.Deserialize<string[]>(binding.ScopesJson) ?? [];
        if (!scopes.Contains(OrganizationTokenScopes.GuidedSession, StringComparer.Ordinal))
        {
            throw new OrganizationAccessDeniedException("The connected application cannot start guided access sessions.");
        }

        return binding;
    }

    private OrganizationGuidedAccessResponse CreateResponse(OrganizationGuidedSessionRecord session, string role)
    {
        var ticket = new GuidedAccessTicket(
            session.OrganizationId,
            session.Id,
            session.PresenterUserName,
            session.ActiveRoleUserName,
            session.ExpiresAt);
        var protectedTicket = dataProtection.CreateProtector(TokenPurpose)
            .Protect(JsonSerializer.Serialize(ticket));
        return new(
            new(
                session.Id,
                session.OrganizationId,
                session.PresenterUserName,
                session.ActiveRoleUserName,
                role,
                session.StartedAt,
                session.ExpiresAt,
                session.EndedAt),
            TokenPrefix + protectedTicket);
    }

    private static async Task<OrganizationMembershipRecord> RequireSyntheticMemberAsync(
        OrganizationDbContext db,
        Guid organizationId,
        string userName,
        CancellationToken cancellationToken)
        => await db.OrganizationMemberships.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId
                && x.UserName == userName
                && x.Status == OrganizationMemberStatuses.Active
                && x.IsSyntheticAccount,
            cancellationToken)
            ?? throw new OrganizationValidationException("Guided access is limited to active synthetic organization accounts.");

    private static OrganizationAuditRecord CreateAudit(
        OrganizationGuidedSessionRecord session,
        string action,
        string role)
        => new()
        {
            OrganizationId = session.OrganizationId,
            ActorKind = OrganizationActorKinds.Service,
            ActorId = session.PresenterUserName,
            PresenterUserName = session.PresenterUserName,
            Action = action,
            TargetType = "guided_session",
            TargetId = session.Id.ToString(),
            DetailJson = JsonSerializer.Serialize(new { session.ActiveRoleUserName, Role = role, session.ExpiresAt }),
            CreatedAt = DateTime.UtcNow
        };

    private sealed record GuidedAccessTicket(
        Guid OrganizationId,
        Guid SessionId,
        string PresenterUserName,
        string ActiveRoleUserName,
        DateTime ExpiresAt);
}
