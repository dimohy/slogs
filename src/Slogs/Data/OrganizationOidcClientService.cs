using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Slogs.Data;

public sealed class OrganizationOidcClientService(
    IDbContextFactory<OrganizationDbContext> dbFactory,
    OrganizationActorResolver actorResolver,
    IOpenIddictApplicationManager applicationManager)
{
    private static readonly IReadOnlySet<string> StandardScopes = new HashSet<string>(StringComparer.Ordinal)
    {
        OpenIddictConstants.Scopes.OpenId,
        OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Scopes.Email,
        OpenIddictConstants.Scopes.Roles
    };

    public async Task<OrganizationOidcClientCreatedResponse> CreateAsync(
        Guid organizationId,
        OrganizationOidcClientCreateRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.OidcManage,
            cancellationToken);
        if (actor.Role != OrganizationRoles.Owner)
        {
            throw new OrganizationAccessDeniedException("Only an organization owner can register a connected OIDC application.");
        }

        var clientId = RequireIdentifier(request.ClientId, 120, nameof(request.ClientId));
        var displayName = RequireText(request.DisplayName, 200, nameof(request.DisplayName));
        var redirectUris = NormalizeRedirectUris(request.RedirectUris);
        var scopes = NormalizeScopes(request.Scopes, actor);
        if (await applicationManager.FindByClientIdAsync(clientId, cancellationToken) is not null)
        {
            throw new OrganizationConflictException("The OIDC client identifier is already registered.");
        }

        var clientSecret = CreateSecret();
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = displayName,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.ResponseTypes.Code
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
            }
        };
        foreach (var uri in redirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        foreach (var scope in scopes)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
        }

        var application = await applicationManager.CreateAsync(descriptor, cancellationToken);
        var applicationId = await applicationManager.GetIdAsync(application, cancellationToken)
            ?? throw new InvalidOperationException("OpenIddict did not return a connected application identifier.");
        var now = DateTime.UtcNow;
        var record = new OrganizationOidcClientRecord
        {
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            ClientId = clientId,
            DisplayName = displayName,
            RedirectUrisJson = JsonSerializer.Serialize(redirectUris),
            ScopesJson = JsonSerializer.Serialize(scopes),
            SecretVersion = 1,
            CreatedBy = actor.ActorId,
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.OrganizationOidcClients.Add(record);
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.oidc-client.create",
            "oidc_client",
            record.Id.ToString(),
            detail: new { record.ClientId, RedirectUris = redirectUris, Scopes = scopes }));
        await db.SaveChangesAsync(cancellationToken);
        return new(ToResponse(record), clientSecret);
    }

    public async Task<OrganizationOidcClientCreatedResponse> RotateSecretAsync(
        Guid organizationId,
        Guid clientRecordId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.OidcManage,
            cancellationToken);
        if (actor.Role != OrganizationRoles.Owner)
        {
            throw new OrganizationAccessDeniedException("Only an organization owner can rotate a connected application secret.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.OrganizationOidcClients.FirstOrDefaultAsync(
            x => x.Id == clientRecordId && x.OrganizationId == organizationId && x.RevokedAt == null,
            cancellationToken)
            ?? throw new OrganizationNotFoundException("Active OIDC client not found.");
        var application = await applicationManager.FindByIdAsync(record.ApplicationId, cancellationToken)
            ?? throw new OrganizationNotFoundException("OpenIddict application not found.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, application, cancellationToken);
        var clientSecret = CreateSecret();
        descriptor.ClientSecret = clientSecret;
        await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
        record.SecretVersion++;
        record.UpdatedAt = DateTime.UtcNow;
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.oidc-client.rotate-secret",
            "oidc_client",
            record.Id.ToString(),
            detail: new { record.SecretVersion }));
        await db.SaveChangesAsync(cancellationToken);
        return new(ToResponse(record), clientSecret);
    }

    public async Task<bool> RevokeAsync(
        Guid organizationId,
        Guid clientRecordId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.OidcManage,
            cancellationToken);
        if (actor.Role != OrganizationRoles.Owner)
        {
            throw new OrganizationAccessDeniedException("Only an organization owner can revoke a connected application.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.OrganizationOidcClients.FirstOrDefaultAsync(
            x => x.Id == clientRecordId && x.OrganizationId == organizationId && x.RevokedAt == null,
            cancellationToken);
        if (record is null)
        {
            return false;
        }

        var application = await applicationManager.FindByIdAsync(record.ApplicationId, cancellationToken);
        if (application is not null)
        {
            await applicationManager.DeleteAsync(application, cancellationToken);
        }

        record.RevokedAt = DateTime.UtcNow;
        record.UpdatedAt = record.RevokedAt.Value;
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.oidc-client.revoke",
            "oidc_client",
            record.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<OrganizationOidcClientResponse>> ListAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        await actorResolver.RequireAsync(organizationId, principal, OrganizationTokenScopes.OidcManage, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var records = await db.OrganizationOidcClients.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return records.Select(ToResponse).ToArray();
    }

    public async Task<OrganizationOidcClientRecord?> FindActiveBindingAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.OrganizationOidcClients.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientId == clientId && x.RevokedAt == null, cancellationToken);
    }

    public async Task<OrganizationOidcClientRecord> RequireAuthenticatedBindingAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new OrganizationAccessDeniedException("Connected application credentials are required.");
        }

        var application = await applicationManager.FindByClientIdAsync(clientId.Trim(), cancellationToken);
        if (application is null
            || !await applicationManager.ValidateClientSecretAsync(application, clientSecret, cancellationToken))
        {
            throw new OrganizationAccessDeniedException("Connected application credentials are invalid.");
        }

        return await FindActiveBindingAsync(clientId.Trim(), cancellationToken)
            ?? throw new OrganizationAccessDeniedException("The connected application binding is not active.");
    }

    private static OrganizationOidcClientResponse ToResponse(OrganizationOidcClientRecord record)
        => new(
            record.Id,
            record.OrganizationId,
            record.ClientId,
            record.DisplayName,
            Deserialize(record.RedirectUrisJson),
            Deserialize(record.ScopesJson),
            record.SecretVersion,
            record.CreatedAt,
            record.UpdatedAt,
            record.RevokedAt is not null);

    private static IReadOnlyList<string> NormalizeRedirectUris(IReadOnlyList<string> values)
    {
        var result = new List<string>();
        foreach (var value in values.Distinct(StringComparer.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new OrganizationValidationException("OIDC redirect URIs must use HTTPS, except for loopback development URIs, and cannot contain fragments.");
            }

            result.Add(uri.AbsoluteUri);
        }

        if (result.Count is < 1 or > 10)
        {
            throw new OrganizationValidationException("An OIDC client requires between 1 and 10 redirect URIs.");
        }

        return result;
    }

    private static IReadOnlyList<string> NormalizeScopes(IReadOnlyList<string> values, OrganizationActorContext actor)
    {
        var scopes = values.Concat(StandardScopes)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var scope in scopes.Where(x => !StandardScopes.Contains(x)))
        {
            if (!OrganizationTokenScopes.All.Contains(scope, StringComparer.Ordinal)
                || !actor.Scopes.Contains(scope))
            {
                throw new OrganizationAccessDeniedException($"The OIDC client scope '{scope}' is not available to this owner.");
            }
        }

        return scopes;
    }

    private static string RequireIdentifier(string value, int maxLength, string fieldName)
    {
        var normalized = RequireText(value, maxLength, fieldName);
        if (normalized.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.')))
        {
            throw new OrganizationValidationException($"{fieldName} contains unsupported characters.");
        }

        return normalized;
    }

    private static string RequireText(string value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
        {
            throw new OrganizationValidationException($"{fieldName} must be between 1 and {maxLength} characters.");
        }

        return normalized;
    }

    private static string CreateSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static IReadOnlyList<string> Deserialize(string json)
        => JsonSerializer.Deserialize<string[]>(json) ?? [];
}
