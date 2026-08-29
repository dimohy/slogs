using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class OrganizationTokenService(
    IDbContextFactory<OrganizationDbContext> dbFactory,
    OrganizationActorResolver actorResolver)
{
    public const string ServiceTokenPrefix = "slogs_org_";

    public async Task<OrganizationServiceTokenCreatedResponse> CreateAsync(
        Guid organizationId,
        OrganizationServiceTokenCreateRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.McpManage,
            cancellationToken);
        var name = RequireName(request.Name);
        var scopes = NormalizeScopes(request.Scopes);
        if (scopes.Any(scope => !actor.Scopes.Contains(scope)))
        {
            throw new OrganizationAccessDeniedException("A service token cannot receive scopes that its creator does not hold.");
        }

        if (request.ExpiresAt is not null && request.ExpiresAt <= DateTime.UtcNow.AddMinutes(5))
        {
            throw new OrganizationValidationException("A service token expiry must be at least five minutes in the future.");
        }

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var token = ServiceTokenPrefix + secret;
        var now = DateTime.UtcNow;
        var record = new OrganizationServiceTokenRecord
        {
            OrganizationId = organizationId,
            Name = name,
            TokenHash = HashToken(token),
            TokenPrefix = token[..Math.Min(token.Length, 20)],
            ScopesJson = JsonSerializer.Serialize(scopes),
            CreatedBy = actor.ActorId,
            CreatedAt = now,
            ExpiresAt = request.ExpiresAt?.ToUniversalTime()
        };
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.OrganizationServiceTokens.Add(record);
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.service-token.create",
            "service_token",
            record.Id.ToString(),
            detail: new { record.Name, Scopes = scopes, record.ExpiresAt }));
        await db.SaveChangesAsync(cancellationToken);
        return new(
            record.Id,
            record.OrganizationId,
            record.Name,
            record.TokenPrefix,
            token,
            scopes,
            record.CreatedAt,
            record.ExpiresAt);
    }

    public async Task<IReadOnlyList<OrganizationServiceTokenResponse>> ListAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        await actorResolver.RequireAsync(organizationId, principal, OrganizationTokenScopes.McpManage, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var records = await db.OrganizationServiceTokens.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return records.Select(ToResponse).ToArray();
    }

    public async Task<bool> RevokeAsync(
        Guid organizationId,
        Guid tokenId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.McpManage,
            cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var token = await db.OrganizationServiceTokens.FirstOrDefaultAsync(
            x => x.Id == tokenId && x.OrganizationId == organizationId,
            cancellationToken);
        if (token is null || token.RevokedAt is not null)
        {
            return false;
        }

        token.RevokedAt = DateTime.UtcNow;
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.service-token.revoke",
            "service_token",
            token.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ClaimsPrincipal?> AuthenticateAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(ServiceTokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = HashToken(token.Trim());
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.OrganizationServiceTokens.FirstOrDefaultAsync(
            x => x.TokenHash == hash && x.RevokedAt == null,
            cancellationToken);
        if (record is null || (record.ExpiresAt is not null && record.ExpiresAt <= DateTime.UtcNow))
        {
            return null;
        }

        record.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"service:{record.Name}"),
            new(ClaimTypes.Name, record.Name),
            new(OrganizationClaimTypes.OrganizationId, record.OrganizationId.ToString()),
            new(OrganizationClaimTypes.ActorKind, OrganizationActorKinds.Service),
            new(OrganizationClaimTypes.TokenId, record.Id.ToString())
        };
        claims.AddRange(DeserializeScopes(record.ScopesJson)
            .Select(scope => new Claim(OrganizationClaimTypes.TokenScope, scope)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "SlogsOrganizationServiceToken"));
    }

    private static OrganizationServiceTokenResponse ToResponse(OrganizationServiceTokenRecord record)
        => new(
            record.Id,
            record.OrganizationId,
            record.Name,
            record.TokenPrefix,
            DeserializeScopes(record.ScopesJson),
            record.CreatedAt,
            record.ExpiresAt,
            record.LastUsedAt,
            record.RevokedAt is not null);

    private static string RequireName(string value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120)
        {
            throw new OrganizationValidationException("Service token name must be between 1 and 120 characters.");
        }

        return name;
    }

    private static IReadOnlyList<string> NormalizeScopes(IReadOnlyList<string> scopes)
    {
        var normalized = scopes.Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0 || normalized.Any(x => !OrganizationTokenScopes.All.Contains(x, StringComparer.Ordinal)))
        {
            throw new OrganizationValidationException("A service token requires one or more supported organization scopes.");
        }

        return normalized;
    }

    private static IReadOnlyList<string> DeserializeScopes(string json)
        => JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
