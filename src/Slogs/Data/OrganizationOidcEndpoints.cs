using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Slogs.Data;

public static class OrganizationOidcEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationOidcEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], (Delegate)AuthorizeAsync);
        endpoints.MapMethods("/connect/logout", [HttpMethods.Get, HttpMethods.Post], (Delegate)LogoutAsync);
        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext httpContext,
        OrganizationOidcClientService clientService,
        OrganizationDirectoryService directoryService,
        IDbContextFactory<OrganizationDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict authorization request is unavailable.");
        var cookieResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (cookieResult.Principal?.Identity?.IsAuthenticated != true)
        {
            var properties = new AuthenticationProperties
            {
                RedirectUri = httpContext.Request.PathBase + httpContext.Request.Path + httpContext.Request.QueryString
            };
            return Results.Challenge(properties, [CookieAuthenticationDefaults.AuthenticationScheme]);
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return Results.BadRequest(new ApiErrorResponse("invalid_client: OIDC client_id is required."));
        }

        var binding = await clientService.FindActiveBindingAsync(request.ClientId, cancellationToken);
        if (binding is null)
        {
            return Results.Forbid();
        }

        var presenterUserName = cookieResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new OrganizationAccessDeniedException("The signed-in Slogs account has no identifier.");
        var subjectUserName = presenterUserName;
        string actorKind = OrganizationActorKinds.User;
        Guid? guidedSessionId = null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var guidedSessionValue = request.GetParameter("guided_session_id")?.ToString();
        if (!string.IsNullOrWhiteSpace(guidedSessionValue))
        {
            if (!Guid.TryParse(guidedSessionValue, out var parsedSessionId))
            {
                return Results.BadRequest(new ApiErrorResponse("invalid_request: guided_session_id is invalid."));
            }

            var requestedRoleUserName = request.GetParameter("guided_role_user_name")?.ToString();
            if (!string.IsNullOrWhiteSpace(requestedRoleUserName))
            {
                await directoryService.SwitchGuidedSessionAsync(
                    binding.OrganizationId,
                    parsedSessionId,
                    new OrganizationGuidedSessionSwitchRequest(requestedRoleUserName),
                    cookieResult.Principal,
                    cancellationToken);
            }

            var session = await db.OrganizationGuidedSessions.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == parsedSessionId
                    && x.OrganizationId == binding.OrganizationId
                    && x.PresenterUserName == presenterUserName
                    && x.EndedAt == null
                    && x.ExpiresAt > DateTime.UtcNow,
                cancellationToken);
            if (session is null)
            {
                return Results.Forbid();
            }

            subjectUserName = session.ActiveRoleUserName;
            actorKind = OrganizationActorKinds.GuidedRole;
            guidedSessionId = session.Id;
        }

        var membership = await db.OrganizationMemberships.AsNoTracking().FirstOrDefaultAsync(
            x => x.OrganizationId == binding.OrganizationId
                && x.UserName == subjectUserName
                && x.Status == OrganizationMemberStatuses.Active,
            cancellationToken);
        if (membership is null)
        {
            return Results.Forbid();
        }

        var allowedClientScopes = Deserialize(binding.ScopesJson).ToHashSet(StringComparer.Ordinal);
        var requestedScopes = request.GetScopes().ToHashSet(StringComparer.Ordinal);
        if (!requestedScopes.IsSubsetOf(allowedClientScopes))
        {
            return Results.Forbid();
        }

        var roleScopes = OrganizationRolePermissions.GetScopes(membership.Role);
        if (requestedScopes.Any(scope => OrganizationTokenScopes.All.Contains(scope, StringComparer.Ordinal)
            && !roleScopes.Contains(scope)))
        {
            return Results.Forbid();
        }

        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);
        AddClaim(identity, OpenIddictConstants.Claims.Subject, subjectUserName, true);
        AddClaim(identity, OpenIddictConstants.Claims.Name, subjectUserName, true);
        AddClaim(identity, OpenIddictConstants.Claims.PreferredUsername, subjectUserName, true);
        AddClaim(identity, OpenIddictConstants.Claims.Role, membership.Role, true);
        AddClaim(identity, OrganizationClaimTypes.OrganizationId, binding.OrganizationId.ToString(), true);
        AddClaim(identity, OrganizationClaimTypes.OrganizationRole, membership.Role, true);
        AddClaim(identity, OrganizationClaimTypes.ActorKind, actorKind, false);
        if (guidedSessionId is not null)
        {
            AddClaim(identity, OrganizationClaimTypes.PresenterUserName, presenterUserName, false);
            AddClaim(identity, OrganizationClaimTypes.GuidedSessionId, guidedSessionId.Value.ToString(), false);
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(requestedScopes);
        db.OrganizationAudits.Add(new OrganizationAuditRecord
        {
            OrganizationId = binding.OrganizationId,
            ActorKind = actorKind,
            ActorId = subjectUserName,
            PresenterUserName = actorKind == OrganizationActorKinds.GuidedRole ? presenterUserName : null,
            Action = "organization.oidc.authorize",
            TargetType = "oidc_client",
            TargetId = binding.Id.ToString(),
            Outcome = "success",
            DetailJson = JsonSerializer.Serialize(new { RequestedScopes = requestedScopes, guidedSessionId }),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return Results.SignIn(
            principal,
            properties: null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> LogoutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.SignOut(
            properties: null,
            authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static void AddClaim(ClaimsIdentity identity, string type, string value, bool identityToken)
    {
        var destinations = identityToken
            ? [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken]
            : new[] { OpenIddictConstants.Destinations.AccessToken };
        identity.AddClaim(new Claim(type, value).SetDestinations(destinations));
    }

    private static IReadOnlyList<string> Deserialize(string json)
        => JsonSerializer.Deserialize<string[]>(json) ?? [];
}
