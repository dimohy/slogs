using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed partial class OrganizationDirectoryService(
    IDbContextFactory<OrganizationDbContext> organizationDbFactory,
    IDbContextFactory<SlogsDbContext> slogsDbFactory,
    OrganizationActorResolver actorResolver)
{
    public async Task<OrganizationResponse> CreateAsync(
        OrganizationCreateRequest request,
        AuthUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAdmin)
        {
            throw new OrganizationAccessDeniedException("Only a Slogs administrator can create an organization.");
        }

        var slug = NormalizeSlug(request.Slug);
        var displayName = RequireText(request.DisplayName, 160, nameof(request.DisplayName));
        var environmentLabel = RequireText(request.EnvironmentLabel, 80, nameof(request.EnvironmentLabel));
        ValidateMinimumCohort(request.MinimumAggregateCohort);

        await using (var slogsDb = await slogsDbFactory.CreateDbContextAsync(cancellationToken))
        {
            var ownerExists = await slogsDb.Users.AsNoTracking()
                .AnyAsync(x => x.UserName == request.OwnerUserName, cancellationToken);
            if (!ownerExists)
            {
                throw new OrganizationValidationException("The organization owner must be an existing Slogs user.");
            }
        }

        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Organizations.AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            throw new OrganizationConflictException("The organization slug is already in use.");
        }

        var now = DateTime.UtcNow;
        var organization = new OrganizationRecord
        {
            Slug = slug,
            DisplayName = displayName,
            EnvironmentLabel = environmentLabel,
            MinimumAggregateCohort = request.MinimumAggregateCohort,
            CreatedAt = now,
            UpdatedAt = now
        };
        var membership = new OrganizationMembershipRecord
        {
            OrganizationId = organization.Id,
            UserName = request.OwnerUserName.Trim(),
            Role = OrganizationRoles.Owner,
            DisplayRole = "소유자",
            Status = OrganizationMemberStatuses.Active,
            InvitedBy = currentUser.UserName,
            CreatedAt = now,
            UpdatedAt = now
        };
        organization.Memberships.Add(membership);
        db.Organizations.Add(organization);
        db.OrganizationAudits.Add(new OrganizationAuditRecord
        {
            OrganizationId = organization.Id,
            ActorKind = OrganizationActorKinds.User,
            ActorId = currentUser.UserName,
            Action = "organization.create",
            TargetType = "organization",
            TargetId = organization.Id.ToString(),
            Outcome = "success",
            DetailJson = "{}",
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(organization);
    }

    public async Task<IReadOnlyList<OrganizationResponse>> ListForUserAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        return await db.OrganizationMemberships.AsNoTracking()
            .Where(x => x.UserName == userName && x.Status == OrganizationMemberStatuses.Active)
            .OrderBy(x => x.Organization!.DisplayName)
            .Select(x => new OrganizationResponse(
                x.OrganizationId,
                x.Organization!.Slug,
                x.Organization.DisplayName,
                x.Organization.EnvironmentLabel,
                x.Organization.MinimumAggregateCohort,
                x.Organization.CreatedAt,
                x.Organization.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationResponse>> ListAllAsync(
        AuthUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAdmin)
        {
            throw new OrganizationAccessDeniedException("Only a Slogs administrator can list all organizations.");
        }

        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Organizations.AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .Select(x => new OrganizationResponse(
                x.Id,
                x.Slug,
                x.DisplayName,
                x.EnvironmentLabel,
                x.MinimumAggregateCohort,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationResponse> UpdateAsync(
        Guid organizationId,
        OrganizationUpdateRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.MembersManage,
            cancellationToken);
        ValidateMinimumCohort(request.MinimumAggregateCohort);
        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        var organization = await db.Organizations.FirstOrDefaultAsync(x => x.Id == organizationId, cancellationToken)
            ?? throw new OrganizationNotFoundException("Organization not found.");
        organization.DisplayName = RequireText(request.DisplayName, 160, nameof(request.DisplayName));
        organization.EnvironmentLabel = RequireText(request.EnvironmentLabel, 80, nameof(request.EnvironmentLabel));
        organization.MinimumAggregateCohort = request.MinimumAggregateCohort;
        organization.UpdatedAt = DateTime.UtcNow;
        db.OrganizationAudits.Add(OrganizationAudit.Create(actor, "organization.update", "organization", organizationId.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(organization);
    }

    public async Task<OrganizationMembershipResponse> UpsertMembershipAsync(
        Guid organizationId,
        OrganizationMembershipUpsertRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.MembersManage,
            cancellationToken);
        var role = NormalizeRole(request.Role);
        var userName = RequireText(request.UserName, 80, nameof(request.UserName));
        var displayRole = RequireText(request.DisplayRole, 80, nameof(request.DisplayRole));

        if (role == OrganizationRoles.Owner && actor.Role != OrganizationRoles.Owner)
        {
            throw new OrganizationAccessDeniedException("Only an organization owner can assign the owner role.");
        }

        await using (var slogsDb = await slogsDbFactory.CreateDbContextAsync(cancellationToken))
        {
            if (!await slogsDb.Users.AsNoTracking().AnyAsync(x => x.UserName == userName, cancellationToken))
            {
                throw new OrganizationValidationException("An organization member must be an existing Slogs user.");
            }
        }

        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Organizations.AnyAsync(x => x.Id == organizationId, cancellationToken))
        {
            throw new OrganizationNotFoundException("Organization not found.");
        }

        var membership = await db.OrganizationMemberships
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserName == userName, cancellationToken);
        var now = DateTime.UtcNow;
        if (membership is null)
        {
            membership = new OrganizationMembershipRecord
            {
                OrganizationId = organizationId,
                UserName = userName,
                CreatedAt = now,
                InvitedBy = actor.ActorId
            };
            db.OrganizationMemberships.Add(membership);
        }

        membership.Role = role;
        membership.DisplayRole = displayRole;
        membership.Status = OrganizationMemberStatuses.Active;
        membership.IsSyntheticAccount = request.IsSyntheticAccount;
        membership.UpdatedAt = now;
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.member.upsert",
            "membership",
            userName,
            detail: new { role, request.IsSyntheticAccount }));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(membership);
    }

    public async Task<IReadOnlyList<OrganizationMembershipResponse>> ListMembershipsAsync(
        Guid organizationId,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.Read,
            cancellationToken);
        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        return await db.OrganizationMemberships.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Role)
            .ThenBy(x => x.UserName)
            .Select(x => new OrganizationMembershipResponse(
                x.OrganizationId,
                x.UserName,
                x.Role,
                x.DisplayRole,
                x.Status,
                x.IsSyntheticAccount,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationUnitResponse> CreateUnitAsync(
        Guid organizationId,
        OrganizationUnitCreateRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.MembersManage,
            cancellationToken);
        var name = RequireText(request.Name, 120, nameof(request.Name));
        var kind = request.Kind.Trim().ToLowerInvariant();
        if (kind is not OrganizationUnitKinds.Department and not OrganizationUnitKinds.Team)
        {
            throw new OrganizationValidationException("Organization units must be a department or a team.");
        }

        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        if (kind == OrganizationUnitKinds.Department && request.ParentUnitId is not null)
        {
            throw new OrganizationValidationException("A department cannot have a parent unit.");
        }

        if (kind == OrganizationUnitKinds.Team)
        {
            if (request.ParentUnitId is null)
            {
                throw new OrganizationValidationException("A team must belong to a department.");
            }

            var parent = await db.OrganizationUnits.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == request.ParentUnitId && x.OrganizationId == organizationId,
                cancellationToken);
            if (parent?.Kind != OrganizationUnitKinds.Department)
            {
                throw new OrganizationValidationException("A team parent must be a department in the same organization.");
            }
        }

        var record = new OrganizationUnitRecord
        {
            OrganizationId = organizationId,
            Name = name,
            NameKey = NormalizeNameKey(name),
            Kind = kind,
            ParentUnitId = request.ParentUnitId,
            CreatedAt = DateTime.UtcNow
        };
        db.OrganizationUnits.Add(record);
        db.OrganizationAudits.Add(OrganizationAudit.Create(actor, "organization.unit.create", "unit", record.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(record);
    }

    public async Task AddUnitMembershipAsync(
        Guid organizationId,
        Guid unitId,
        OrganizationUnitMembershipRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.MembersManage,
            cancellationToken);
        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        var unitExists = await db.OrganizationUnits.AsNoTracking()
            .AnyAsync(x => x.Id == unitId && x.OrganizationId == organizationId, cancellationToken);
        var memberExists = await db.OrganizationMemberships.AsNoTracking()
            .AnyAsync(x => x.OrganizationId == organizationId
                && x.UserName == request.UserName
                && x.Status == OrganizationMemberStatuses.Active,
                cancellationToken);
        if (!unitExists || !memberExists)
        {
            throw new OrganizationValidationException("The unit and active member must belong to the same organization.");
        }

        var exists = await db.OrganizationUnitMemberships.AnyAsync(
            x => x.OrganizationId == organizationId && x.UnitId == unitId && x.UserName == request.UserName,
            cancellationToken);
        if (!exists)
        {
            db.OrganizationUnitMemberships.Add(new OrganizationUnitMembershipRecord
            {
                OrganizationId = organizationId,
                UnitId = unitId,
                UserName = request.UserName,
                CreatedAt = DateTime.UtcNow
            });
            db.OrganizationAudits.Add(OrganizationAudit.Create(
                actor,
                "organization.unit.member.add",
                "unit_membership",
                $"{unitId}:{request.UserName}"));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrganizationUnitResponse>> ListUnitsAsync(
        Guid organizationId,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.Read,
            cancellationToken);
        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        return await db.OrganizationUnits.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Name)
            .Select(x => new OrganizationUnitResponse(
                x.Id,
                x.OrganizationId,
                x.Name,
                x.Kind,
                x.ParentUnitId,
                x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationGuidedSessionResponse> StartGuidedSessionAsync(
        Guid organizationId,
        OrganizationGuidedSessionCreateRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.GuidedSession,
            cancellationToken);
        if (actor.Role != OrganizationRoles.Owner)
        {
            throw new OrganizationAccessDeniedException("Only an organization owner can start a guided session.");
        }

        var duration = Math.Clamp(request.DurationMinutes, 5, 120);
        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        var activeRole = await RequireSyntheticMemberAsync(db, organizationId, request.ActiveRoleUserName, cancellationToken);
        var now = DateTime.UtcNow;
        await db.OrganizationGuidedSessions
            .Where(x => x.OrganizationId == organizationId && x.PresenterUserName == actor.ActorId && x.EndedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EndedAt, now), cancellationToken);
        var session = new OrganizationGuidedSessionRecord
        {
            OrganizationId = organizationId,
            PresenterUserName = actor.ActorId,
            ActiveRoleUserName = activeRole.UserName,
            StartedAt = now,
            ExpiresAt = now.AddMinutes(duration)
        };
        db.OrganizationGuidedSessions.Add(session);
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.guided-session.start",
            "guided_session",
            session.Id.ToString(),
            detail: new { activeRole.UserName, activeRole.Role, duration }));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(session, activeRole.Role);
    }

    public async Task<OrganizationGuidedSessionResponse> SwitchGuidedSessionAsync(
        Guid organizationId,
        Guid sessionId,
        OrganizationGuidedSessionSwitchRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.GuidedSession,
            cancellationToken);
        if (actor.Role != OrganizationRoles.Owner)
        {
            throw new OrganizationAccessDeniedException("Only an organization owner can switch a guided session role.");
        }

        await using var db = await organizationDbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.OrganizationGuidedSessions.FirstOrDefaultAsync(
            x => x.Id == sessionId
                && x.OrganizationId == organizationId
                && x.PresenterUserName == actor.ActorId
                && x.EndedAt == null,
            cancellationToken)
            ?? throw new OrganizationNotFoundException("Active guided session not found.");
        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            throw new OrganizationAccessDeniedException("The guided session has expired.");
        }

        var activeRole = await RequireSyntheticMemberAsync(db, organizationId, request.ActiveRoleUserName, cancellationToken);
        session.ActiveRoleUserName = activeRole.UserName;
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.guided-session.switch",
            "guided_session",
            session.Id.ToString(),
            detail: new { activeRole.UserName, activeRole.Role }));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(session, activeRole.Role);
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
            ?? throw new OrganizationValidationException("Guided sessions can switch only to active synthetic organization accounts.");

    private static OrganizationResponse ToResponse(OrganizationRecord record)
        => new(
            record.Id,
            record.Slug,
            record.DisplayName,
            record.EnvironmentLabel,
            record.MinimumAggregateCohort,
            record.CreatedAt,
            record.UpdatedAt);

    private static OrganizationMembershipResponse ToResponse(OrganizationMembershipRecord record)
        => new(
            record.OrganizationId,
            record.UserName,
            record.Role,
            record.DisplayRole,
            record.Status,
            record.IsSyntheticAccount,
            record.CreatedAt,
            record.UpdatedAt);

    private static OrganizationUnitResponse ToResponse(OrganizationUnitRecord record)
        => new(record.Id, record.OrganizationId, record.Name, record.Kind, record.ParentUnitId, record.CreatedAt);

    private static OrganizationGuidedSessionResponse ToResponse(OrganizationGuidedSessionRecord record, string role)
        => new(
            record.Id,
            record.OrganizationId,
            record.PresenterUserName,
            record.ActiveRoleUserName,
            role,
            record.StartedAt,
            record.ExpiresAt,
            record.EndedAt);

    private static string NormalizeSlug(string value)
    {
        var slug = RequireText(value, 80, "Slug").ToLowerInvariant();
        if (!OrganizationSlugPattern().IsMatch(slug))
        {
            throw new OrganizationValidationException("Organization slugs must start with a letter or digit and contain only lowercase letters, digits, or hyphens.");
        }

        return slug;
    }

    private static string NormalizeRole(string value)
    {
        var role = value.Trim().ToLowerInvariant();
        if (!OrganizationRoles.All.Contains(role, StringComparer.Ordinal))
        {
            throw new OrganizationValidationException("Unsupported organization role.");
        }

        return role;
    }

    private static string NormalizeNameKey(string value)
        => string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string RequireText(string value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
        {
            throw new OrganizationValidationException($"{fieldName} must be between 1 and {maxLength} characters.");
        }

        return normalized;
    }

    private static void ValidateMinimumCohort(int value)
    {
        if (value is < 3 or > 100)
        {
            throw new OrganizationValidationException("Minimum aggregate cohort must be between 3 and 100.");
        }
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex OrganizationSlugPattern();
}
