using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class OrganizationMetricsService(
    IDbContextFactory<OrganizationDbContext> dbFactory,
    OrganizationActorResolver actorResolver)
{
    public async Task RecordAsync(
        Guid organizationId,
        OrganizationMetricEventRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.MetricsWrite,
            cancellationToken);
        var actorKey = RequireText(request.ActorKey, 128, nameof(request.ActorKey));
        var metricKind = RequireText(request.MetricKind, 80, nameof(request.MetricKind));
        if (request.Value < 0)
        {
            throw new OrganizationValidationException("Metric values cannot be negative.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (request.UnitId is not null
            && !await db.OrganizationUnits.AsNoTracking().AnyAsync(
                x => x.OrganizationId == organizationId && x.Id == request.UnitId,
                cancellationToken))
        {
            throw new OrganizationValidationException("Metric unit must belong to the same organization.");
        }

        var record = new OrganizationMetricEventRecord
        {
            OrganizationId = organizationId,
            UnitId = request.UnitId,
            ActorKey = actorKey,
            MetricKind = metricKind,
            Value = request.Value,
            IsDemoAssumption = request.IsDemoAssumption,
            OccurredAt = request.OccurredAt.ToUniversalTime()
        };
        db.OrganizationMetricEvents.Add(record);
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.metric.record",
            "metric_event",
            record.Id.ToString(),
            detail: new { record.MetricKind, record.UnitId, record.IsDemoAssumption }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationMetricSummaryResponse>> SummarizeAsync(
        Guid organizationId,
        DateTime from,
        DateTime to,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.MetricsRead,
            cancellationToken);
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(370))
        {
            throw new OrganizationValidationException("Metric range must be positive and cannot exceed 370 days.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var organization = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == organizationId, cancellationToken)
            ?? throw new OrganizationNotFoundException("Organization not found.");
        var units = await db.OrganizationUnits.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var events = await db.OrganizationMetricEvents.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.OccurredAt >= fromUtc
                && x.OccurredAt < toUtc)
            .ToListAsync(cancellationToken);

        var summaries = new List<OrganizationMetricSummaryResponse>();
        foreach (var metricGroup in events.GroupBy(x => x.MetricKind, StringComparer.Ordinal))
        {
            AddAggregate(summaries, organizationId, null, metricGroup, organization.MinimumAggregateCohort, false);
            var unitGroups = metricGroup.Where(x => x.UnitId is not null)
                .GroupBy(x => x.UnitId!.Value)
                .ToArray();
            foreach (var unitGroup in unitGroups)
            {
                var cohort = unitGroup.Select(x => x.ActorKey).Distinct(StringComparer.Ordinal).Count();
                if (cohort >= organization.MinimumAggregateCohort)
                {
                    AddAggregate(summaries, organizationId, unitGroup.Key, unitGroup, organization.MinimumAggregateCohort, false);
                    continue;
                }

                summaries.Add(new OrganizationMetricSummaryResponse(
                    organizationId,
                    unitGroup.Key,
                    metricGroup.Key,
                    0,
                    cohort,
                    organization.MinimumAggregateCohort,
                    true,
                    true,
                    unitGroup.All(x => x.IsDemoAssumption)));
            }

            foreach (var unitGroup in unitGroups)
            {
                var cohort = unitGroup.Select(x => x.ActorKey).Distinct(StringComparer.Ordinal).Count();
                if (cohort >= organization.MinimumAggregateCohort)
                {
                    continue;
                }

                if (units.TryGetValue(unitGroup.Key, out var unit) && unit.ParentUnitId is { } parentId)
                {
                    var parentEvents = metricGroup.Where(x => x.UnitId == parentId
                        || (x.UnitId is { } childId
                            && units.TryGetValue(childId, out var child)
                            && child.ParentUnitId == parentId));
                    var parentCohort = parentEvents.Select(x => x.ActorKey).Distinct(StringComparer.Ordinal).Count();
                    if (parentCohort >= organization.MinimumAggregateCohort)
                    {
                        var existingIndex = summaries.FindIndex(
                            x => x.UnitId == parentId && x.MetricKind == metricGroup.Key);
                        if (existingIndex < 0 || summaries[existingIndex].IsSuppressed)
                        {
                            if (existingIndex >= 0)
                            {
                                summaries.RemoveAt(existingIndex);
                            }

                            AddAggregate(summaries, organizationId, parentId, parentEvents, organization.MinimumAggregateCohort, true);
                        }
                    }
                }
            }
        }

        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.metrics.read",
            "metric_summary",
            null,
            detail: new { fromUtc, toUtc, ResultCount = summaries.Count }));
        await db.SaveChangesAsync(cancellationToken);
        return summaries
            .OrderBy(x => x.MetricKind)
            .ThenBy(x => x.UnitId)
            .ToArray();
    }

    public async Task<IReadOnlyList<OrganizationAuditResponse>> ListAuditsAsync(
        Guid organizationId,
        int limit,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        await actorResolver.RequireAsync(organizationId, principal, OrganizationTokenScopes.MetricsRead, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var effectiveLimit = Math.Clamp(limit, 1, 500);
        return await db.OrganizationAudits.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(effectiveLimit)
            .Select(x => new OrganizationAuditResponse(
                x.Id,
                x.OrganizationId,
                x.ActorKind,
                x.ActorId,
                x.PresenterUserName,
                x.Action,
                x.TargetType,
                x.TargetId,
                x.Outcome,
                x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private static void AddAggregate(
        ICollection<OrganizationMetricSummaryResponse> results,
        Guid organizationId,
        Guid? unitId,
        IEnumerable<OrganizationMetricEventRecord> records,
        int minimumCohort,
        bool rolledUp)
    {
        var materialized = records.ToArray();
        if (materialized.Length == 0)
        {
            return;
        }

        var cohort = materialized.Select(x => x.ActorKey).Distinct(StringComparer.Ordinal).Count();
        var suppressed = cohort < minimumCohort;
        results.Add(new OrganizationMetricSummaryResponse(
            organizationId,
            unitId,
            materialized[0].MetricKind,
            suppressed ? 0 : materialized.Sum(x => x.Value),
            cohort,
            minimumCohort,
            suppressed,
            rolledUp,
            materialized.All(x => x.IsDemoAssumption)));
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
}
