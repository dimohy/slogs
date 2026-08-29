using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class KnowledgeCorpusPrincipalResolver(
    IDbContextFactory<OrganizationDbContext> organizationDbFactory)
{
    public async Task<KnowledgeCorpusActor> ResolveAsync(
        AuthUser user,
        CancellationToken cancellationToken = default)
    {
        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        var memberships = await db.OrganizationMemberships
            .AsNoTracking()
            .Where(item => item.UserName == user.UserName
                && item.Status == OrganizationMemberStatuses.Active)
            .Select(item => new { item.OrganizationId, item.Role })
            .ToArrayAsync(cancellationToken);

        var roles = memberships.ToDictionary(
            item => item.OrganizationId.ToString("D"),
            item => item.Role,
            StringComparer.Ordinal);
        return new KnowledgeCorpusActor(user.UserName, user.IsAdmin, roles);
    }
}
