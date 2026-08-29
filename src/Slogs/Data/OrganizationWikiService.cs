using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class OrganizationWikiService(
    IDbContextFactory<OrganizationDbContext> dbFactory,
    OrganizationActorResolver actorResolver,
    IOrganizationSemanticIndex semanticIndex)
{
    private const int MaxQueryLength = 1000;
    private const int MaxContentLength = 80_000;

    public async Task<OrganizationMemoryResponse> CaptureAsync(
        Guid organizationId,
        OrganizationMemoryDraftRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
        => await CreateMemoryAsync(
            organizationId,
            request with { ScopeKind = OrganizationMemoryScopes.PersonalCandidate, ScopeKey = null },
            OrganizationMemoryStates.Draft,
            "organization.memory.capture",
            principal,
            cancellationToken);

    public async Task<OrganizationMemoryResponse> ProposeAsync(
        Guid organizationId,
        OrganizationMemoryDraftRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
        => await CreateMemoryAsync(
            organizationId,
            request,
            OrganizationMemoryStates.ReviewRequested,
            "organization.memory.propose",
            principal,
            cancellationToken);

    public async Task<OrganizationMemoryResponse> ApproveAsync(
        Guid organizationId,
        Guid memoryId,
        OrganizationMemoryDecisionRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.Approve,
            cancellationToken);
        var reason = RequireText(request.Reason, 1000, nameof(request.Reason));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var memory = await db.OrganizationMemories
            .Include(x => x.Sources)
            .FirstOrDefaultAsync(x => x.Id == memoryId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new OrganizationNotFoundException("Organization memory not found.");
        RequireState(memory, OrganizationMemoryStates.ReviewRequested);

        var hasPendingConflict = await db.OrganizationConflicts.AsNoTracking().AnyAsync(
            x => x.OrganizationId == organizationId
                && x.State == OrganizationConflictStates.Pending
                && (x.LeftMemoryId == memoryId || x.RightMemoryId == memoryId),
            cancellationToken);
        if (hasPendingConflict)
        {
            throw new OrganizationConflictException("Pending conflicts must be resolved before this memory can be approved.");
        }

        if (!string.IsNullOrWhiteSpace(request.CorrectedContent))
        {
            memory.Content = RequireContent(request.CorrectedContent);
        }

        await semanticIndex.IndexAsync(memory, cancellationToken);

        var now = DateTime.UtcNow;
        memory.State = OrganizationMemoryStates.Active;
        memory.ApprovedBy = actor.ActorId;
        memory.ApprovedAt = now;
        memory.DecisionReason = reason;
        memory.UpdatedAt = now;
        memory.Revision++;
        db.OrganizationMemoryRevisions.Add(CreateRevision(memory, actor, "approve", reason));

        if (memory.SupersedesMemoryId is { } supersededId)
        {
            var superseded = await db.OrganizationMemories.FirstOrDefaultAsync(
                x => x.Id == supersededId && x.OrganizationId == organizationId,
                cancellationToken)
                ?? throw new OrganizationValidationException("The superseded memory must belong to the same organization.");
            if (superseded.State == OrganizationMemoryStates.Active)
            {
                superseded.State = OrganizationMemoryStates.Superseded;
                superseded.UpdatedAt = now;
                superseded.Revision++;
                db.OrganizationMemoryRevisions.Add(CreateRevision(superseded, actor, "supersede", $"Superseded by {memory.Id}."));
            }
        }

        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.memory.approve",
            "memory",
            memory.Id.ToString(),
            detail: new { memory.ScopeKind, memory.ScopeKey, memory.Revision }));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(memory);
    }

    public async Task<OrganizationMemoryResponse> RejectAsync(
        Guid organizationId,
        Guid memoryId,
        OrganizationMemoryDecisionRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
        => await DecideAsync(
            organizationId,
            memoryId,
            request,
            OrganizationTokenScopes.Reject,
            OrganizationMemoryStates.Rejected,
            "organization.memory.reject",
            principal,
            cancellationToken);

    public async Task<OrganizationMemoryResponse> WithdrawAsync(
        Guid organizationId,
        Guid memoryId,
        OrganizationMemoryDecisionRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.Propose,
            cancellationToken);
        var reason = RequireText(request.Reason, 1000, nameof(request.Reason));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var memory = await db.OrganizationMemories
            .Include(x => x.Sources)
            .FirstOrDefaultAsync(x => x.Id == memoryId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new OrganizationNotFoundException("Organization memory not found.");
        if (memory.ProposedBy != actor.ActorId && !actor.Scopes.Contains(OrganizationTokenScopes.Reject))
        {
            throw new OrganizationAccessDeniedException("Only the proposer or an approver can withdraw this memory.");
        }

        if (memory.State is OrganizationMemoryStates.Superseded or OrganizationMemoryStates.Withdrawn)
        {
            throw new OrganizationConflictException("This memory is already inactive.");
        }

        memory.State = OrganizationMemoryStates.Withdrawn;
        memory.DecisionReason = reason;
        memory.UpdatedAt = DateTime.UtcNow;
        memory.Revision++;
        db.OrganizationMemoryRevisions.Add(CreateRevision(memory, actor, "withdraw", reason));
        db.OrganizationAudits.Add(OrganizationAudit.Create(actor, "organization.memory.withdraw", "memory", memory.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(memory);
    }

    public async Task<OrganizationMemoryResponse> ReadAsync(
        Guid organizationId,
        Guid memoryId,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.Read,
            cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var memory = await db.OrganizationMemories.AsNoTracking()
            .Include(x => x.Sources)
            .FirstOrDefaultAsync(x => x.Id == memoryId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new OrganizationNotFoundException("Organization memory not found.");
        EnsureMemoryVisibleToActor(memory, actor);
        db.OrganizationAudits.Add(OrganizationAudit.Create(actor, "organization.memory.read", "memory", memory.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(memory);
    }

    public async Task<IReadOnlyList<OrganizationMemorySummaryResponse>> SearchAsync(
        Guid organizationId,
        string? query,
        string? categoryPath,
        string? scopeKind,
        string? scopeKey,
        int limit,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.Read,
            cancellationToken);
        var normalizedQuery = NormalizeOptionalText(query, MaxQueryLength);
        var normalizedCategory = NormalizeOptionalCategory(categoryPath);
        var effectiveLimit = Math.Clamp(limit, 1, 50);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var memories = db.OrganizationMemories.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.State == OrganizationMemoryStates.Active);
        memories = ApplyScopeFilter(memories, scopeKind, scopeKey);
        if (normalizedCategory is not null)
        {
            memories = memories.Where(x => x.CategoryPath == normalizedCategory
                || x.CategoryPath.StartsWith(normalizedCategory + "/"));
        }

        var candidates = await memories
            .OrderByDescending(x => x.UpdatedAt)
            .Take(5000)
            .ToListAsync(cancellationToken);
        var terms = SplitTerms(normalizedQuery);
        var semanticScores = terms.Count == 0
            ? new Dictionary<Guid, double>()
            : await semanticIndex.ScoreAsync(
                organizationId,
                normalizedQuery!,
                candidates.Select(x => x.Id).ToArray(),
                Math.Max(effectiveLimit * 20, 100),
                cancellationToken);
        var ranked = candidates
            .Select(x =>
            {
                var lexicalScore = CalculateRelevance(x, terms);
                var score = terms.Count == 0
                    ? 0
                    : semanticScores.TryGetValue(x.Id, out var semanticScore)
                        ? (int)Math.Round(Math.Clamp(semanticScore, 0, 1) * 75 + lexicalScore * 0.25)
                        : -1;
                return (Memory: x, Score: score);
            })
            .Where(x => terms.Count == 0 || x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Memory.UpdatedAt)
            .Take(effectiveLimit)
            .Select(x => ToSummary(x.Memory, terms.Count == 0 ? null : x.Score))
            .ToList();
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.memory.search",
            "memory",
            null,
            detail: new { ResultCount = ranked.Count, effectiveLimit, scopeKind, scopeKey }));
        await db.SaveChangesAsync(cancellationToken);
        return ranked;
    }

    public async Task<IReadOnlyList<OrganizationMemoryRecallResponse>> RecallAsync(
        Guid organizationId,
        string query,
        string? scopeKind,
        string? scopeKey,
        int limit,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var summaries = await SearchAsync(
            organizationId,
            query,
            null,
            scopeKind,
            scopeKey,
            limit,
            principal,
            cancellationToken);
        if (summaries.Count == 0)
        {
            return [];
        }

        var orderedIds = summaries.Select(x => x.Id).ToArray();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var activeMemories = await db.OrganizationMemories.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.State == OrganizationMemoryStates.Active
                && orderedIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return summaries
            .Select(summary => activeMemories.TryGetValue(summary.Id, out var memory)
                ? ToRecall(memory, summary.RelevancePercent)
                : throw new InvalidOperationException("Recalled organization memory is no longer active."))
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> CategoriesAsync(
        Guid organizationId,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        await actorResolver.RequireAsync(organizationId, principal, OrganizationTokenScopes.Read, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.OrganizationMemories.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.State == OrganizationMemoryStates.Active)
            .Select(x => x.CategoryPath)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationMemoryRevisionResponse>> RevisionsAsync(
        Guid organizationId,
        Guid memoryId,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(organizationId, principal, OrganizationTokenScopes.Read, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var memory = await db.OrganizationMemories.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == memoryId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new OrganizationNotFoundException("Organization memory not found.");
        EnsureMemoryVisibleToActor(memory, actor);
        return await db.OrganizationMemoryRevisions.AsNoTracking()
            .Where(x => x.MemoryId == memoryId)
            .OrderByDescending(x => x.Revision)
            .Select(x => new OrganizationMemoryRevisionResponse(
                x.Id,
                x.MemoryId,
                x.Revision,
                x.Action,
                x.ActorUserName,
                x.PresenterUserName,
                x.Reason,
                x.State,
                x.ScopeKind,
                x.ScopeKey,
                x.Content,
                x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationMemorySourceResponse> IngestSourceAsync(
        Guid organizationId,
        OrganizationMemorySourceIngestRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.SourcesManage,
            cancellationToken);
        ValidateSourceRequest(request);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var sameHash = await db.OrganizationMemorySources.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId
                && x.SourceUri == request.SourceUri
                && x.ContentHash == request.ContentHash,
            cancellationToken);
        if (sameHash is not null)
        {
            sameHash.LastCheckedAt = DateTime.UtcNow;
            sameHash.FailureMessage = request.FailureMessage;
            if (!string.IsNullOrWhiteSpace(request.FailureMessage))
            {
                sameHash.State = OrganizationSourceStates.Failed;
            }

            db.OrganizationAudits.Add(OrganizationAudit.Create(
                actor,
                "organization.source.unchanged",
                "source",
                sameHash.Id.ToString()));
            await db.SaveChangesAsync(cancellationToken);
            return ToResponse(sameHash);
        }

        if (request.MemoryId is not null
            && !await db.OrganizationMemories.AsNoTracking().AnyAsync(
                x => x.OrganizationId == organizationId && x.Id == request.MemoryId,
                cancellationToken))
        {
            throw new OrganizationValidationException("A linked memory must belong to the same organization.");
        }

        var now = DateTime.UtcNow;
        var source = new OrganizationMemorySourceRecord
        {
            OrganizationId = organizationId,
            MemoryId = request.MemoryId,
            Title = request.Title.Trim(),
            SourceUri = request.SourceUri.Trim(),
            SourceKind = request.SourceKind.Trim(),
            Grade = NormalizeSourceGrade(request.Grade),
            State = string.IsNullOrWhiteSpace(request.FailureMessage)
                ? OrganizationSourceStates.Pending
                : OrganizationSourceStates.Failed,
            ContentHash = request.ContentHash.Trim().ToLowerInvariant(),
            CapturedAt = request.CapturedAt.ToUniversalTime(),
            LastCheckedAt = now,
            Excerpt = NormalizeNullableText(request.Excerpt, 4000),
            FailureMessage = NormalizeNullableText(request.FailureMessage, 1000),
            CreatedBy = actor.ActorId
        };
        db.OrganizationMemorySources.Add(source);

        var previous = await db.OrganizationMemorySources.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.SourceUri == source.SourceUri)
            .OrderByDescending(x => x.CapturedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (previous is not null)
        {
            db.OrganizationConflicts.Add(new OrganizationConflictRecord
            {
                OrganizationId = organizationId,
                FieldName = "source.content_hash",
                LeftValue = previous.ContentHash,
                RightValue = source.ContentHash,
                LeftSourceId = previous.Id,
                RightSourceId = source.Id,
                State = OrganizationConflictStates.Pending,
                CreatedAt = now
            });
        }

        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            "organization.source.ingest",
            "source",
            source.Id.ToString(),
            detail: new { source.Grade, source.State, Changed = previous is not null }));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(source);
    }

    public async Task<IReadOnlyList<OrganizationMemorySourceResponse>> ListSourcesAsync(
        Guid organizationId,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        await actorResolver.RequireAsync(organizationId, principal, OrganizationTokenScopes.Read, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.OrganizationMemorySources.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.LastCheckedAt)
            .Select(x => new OrganizationMemorySourceResponse(
                x.Id,
                x.OrganizationId,
                x.MemoryId,
                x.Title,
                x.SourceUri,
                x.SourceKind,
                x.Grade,
                x.State,
                x.ContentHash,
                x.CapturedAt,
                x.LastCheckedAt,
                x.Excerpt,
                x.FailureMessage))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationConflictResponse> CreateConflictAsync(
        Guid organizationId,
        OrganizationConflictCreateRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.SourcesManage,
            cancellationToken);
        var conflict = new OrganizationConflictRecord
        {
            OrganizationId = organizationId,
            FieldName = RequireText(request.FieldName, 160, nameof(request.FieldName)),
            LeftValue = RequireText(request.LeftValue, 20_000, nameof(request.LeftValue)),
            RightValue = RequireText(request.RightValue, 20_000, nameof(request.RightValue)),
            LeftMemoryId = request.LeftMemoryId,
            RightMemoryId = request.RightMemoryId,
            LeftSourceId = request.LeftSourceId,
            RightSourceId = request.RightSourceId,
            CreatedAt = DateTime.UtcNow
        };
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.OrganizationConflicts.Add(conflict);
        db.OrganizationAudits.Add(OrganizationAudit.Create(actor, "organization.conflict.create", "conflict", conflict.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(conflict);
    }

    public async Task<IReadOnlyList<OrganizationConflictResponse>> ListConflictsAsync(
        Guid organizationId,
        bool pendingOnly,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        await actorResolver.RequireAsync(organizationId, principal, OrganizationTokenScopes.Read, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.OrganizationConflicts.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (pendingOnly)
        {
            query = query.Where(x => x.State == OrganizationConflictStates.Pending);
        }

        return await query.OrderByDescending(x => x.CreatedAt)
            .Select(x => new OrganizationConflictResponse(
                x.Id,
                x.OrganizationId,
                x.FieldName,
                x.LeftValue,
                x.RightValue,
                x.LeftMemoryId,
                x.RightMemoryId,
                x.LeftSourceId,
                x.RightSourceId,
                x.State,
                x.Resolution,
                x.ResolvedBy,
                x.CreatedAt,
                x.ResolvedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationConflictResponse> ResolveConflictAsync(
        Guid organizationId,
        Guid conflictId,
        OrganizationConflictDecisionRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.Approve,
            cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var conflict = await db.OrganizationConflicts.FirstOrDefaultAsync(
            x => x.Id == conflictId && x.OrganizationId == organizationId,
            cancellationToken)
            ?? throw new OrganizationNotFoundException("Organization conflict not found.");
        if (conflict.State != OrganizationConflictStates.Pending)
        {
            throw new OrganizationConflictException("The conflict was already resolved.");
        }

        if (request.SelectedMemoryId is not null
            && request.SelectedMemoryId != conflict.LeftMemoryId
            && request.SelectedMemoryId != conflict.RightMemoryId)
        {
            throw new OrganizationValidationException("The selected memory is not part of this conflict.");
        }

        conflict.State = OrganizationConflictStates.Resolved;
        conflict.Resolution = RequireText(request.Resolution, 1000, nameof(request.Resolution));
        conflict.ResolvedBy = actor.ActorId;
        conflict.ResolvedAt = DateTime.UtcNow;
        db.OrganizationAudits.Add(OrganizationAudit.Create(actor, "organization.conflict.resolve", "conflict", conflict.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(conflict);
    }

    private async Task<OrganizationMemoryResponse> CreateMemoryAsync(
        Guid organizationId,
        OrganizationMemoryDraftRequest request,
        string state,
        string auditAction,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await actorResolver.RequireAsync(
            organizationId,
            principal,
            OrganizationTokenScopes.Propose,
            cancellationToken);
        ValidateMemoryRequest(request);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Organizations.AsNoTracking().AnyAsync(x => x.Id == organizationId, cancellationToken))
        {
            throw new OrganizationNotFoundException("Organization not found.");
        }

        if (request.SupersedesMemoryId is not null
            && !await db.OrganizationMemories.AsNoTracking().AnyAsync(
                x => x.Id == request.SupersedesMemoryId && x.OrganizationId == organizationId,
                cancellationToken))
        {
            throw new OrganizationValidationException("The superseded memory must belong to the same organization.");
        }

        var now = DateTime.UtcNow;
        var category = NormalizeCategory(request.CategoryPath);
        var memory = new OrganizationMemoryRecord
        {
            OrganizationId = organizationId,
            Slug = await CreateUniqueSlugAsync(db, organizationId, request.Title, cancellationToken),
            Title = request.Title.Trim(),
            Summary = request.Summary.Trim(),
            Content = RequireContent(request.Content),
            SourcePrompt = RequireText(request.SourcePrompt, 12_000, nameof(request.SourcePrompt)),
            TagsJson = JsonSerializer.Serialize(NormalizeTags(request.Tags)),
            CategoryPath = category,
            CategoryDepth = category.Split('/').Length,
            State = state,
            ScopeKind = NormalizeScope(request.ScopeKind),
            ScopeKey = NormalizeScopeKey(request.ScopeKind, request.ScopeKey),
            ProposedBy = actor.ActorId,
            SupersedesMemoryId = request.SupersedesMemoryId,
            DecisionReason = NormalizeNullableText(request.ProposalReason, 1000),
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.OrganizationMemories.Add(memory);
        db.OrganizationMemoryRevisions.Add(CreateRevision(memory, actor, state == OrganizationMemoryStates.Draft ? "capture" : "propose", memory.DecisionReason ?? string.Empty));
        db.OrganizationAudits.Add(OrganizationAudit.Create(
            actor,
            auditAction,
            "memory",
            memory.Id.ToString(),
            detail: new { memory.State, memory.ScopeKind, memory.ScopeKey }));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(memory);
    }

    private async Task<OrganizationMemoryResponse> DecideAsync(
        Guid organizationId,
        Guid memoryId,
        OrganizationMemoryDecisionRequest request,
        string requiredScope,
        string targetState,
        string auditAction,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await actorResolver.RequireAsync(organizationId, principal, requiredScope, cancellationToken);
        var reason = RequireText(request.Reason, 1000, nameof(request.Reason));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var memory = await db.OrganizationMemories.Include(x => x.Sources)
            .FirstOrDefaultAsync(x => x.Id == memoryId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new OrganizationNotFoundException("Organization memory not found.");
        RequireState(memory, OrganizationMemoryStates.ReviewRequested);
        memory.State = targetState;
        memory.DecisionReason = reason;
        memory.UpdatedAt = DateTime.UtcNow;
        memory.Revision++;
        db.OrganizationMemoryRevisions.Add(CreateRevision(memory, actor, targetState, reason));
        db.OrganizationAudits.Add(OrganizationAudit.Create(actor, auditAction, "memory", memory.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(memory);
    }

    private static IQueryable<OrganizationMemoryRecord> ApplyScopeFilter(
        IQueryable<OrganizationMemoryRecord> query,
        string? scopeKind,
        string? scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKind))
        {
            return query.Where(x => x.ScopeKind == OrganizationMemoryScopes.Organization);
        }

        var kind = NormalizeScope(scopeKind);
        var key = NormalizeScopeKey(kind, scopeKey);
        return query.Where(x => x.ScopeKind == OrganizationMemoryScopes.Organization
            || (x.ScopeKind == kind && x.ScopeKey == key));
    }

    private static void EnsureMemoryVisibleToActor(OrganizationMemoryRecord memory, OrganizationActorContext actor)
    {
        if (memory.ScopeKind == OrganizationMemoryScopes.PersonalCandidate
            && memory.ProposedBy != actor.ActorId
            && !actor.Scopes.Contains(OrganizationTokenScopes.Approve))
        {
            throw new OrganizationAccessDeniedException("Personal memory candidates are visible only to their proposer and approvers.");
        }
    }

    private static OrganizationMemoryRevisionRecord CreateRevision(
        OrganizationMemoryRecord memory,
        OrganizationActorContext actor,
        string action,
        string reason)
        => new()
        {
            MemoryId = memory.Id,
            Revision = memory.Revision,
            Action = action,
            ActorUserName = actor.ActorId,
            PresenterUserName = actor.PresenterUserName,
            Reason = reason,
            Title = memory.Title,
            Summary = memory.Summary,
            Content = memory.Content,
            SourcePrompt = memory.SourcePrompt,
            TagsJson = memory.TagsJson,
            CategoryPath = memory.CategoryPath,
            State = memory.State,
            ScopeKind = memory.ScopeKind,
            ScopeKey = memory.ScopeKey,
            CreatedAt = DateTime.UtcNow
        };

    private static void RequireState(OrganizationMemoryRecord memory, string requiredState)
    {
        if (memory.State != requiredState)
        {
            throw new OrganizationConflictException($"Memory state must be '{requiredState}', but is '{memory.State}'.");
        }
    }

    private static void ValidateMemoryRequest(OrganizationMemoryDraftRequest request)
    {
        RequireText(request.Title, 200, nameof(request.Title));
        RequireText(request.Summary, 500, nameof(request.Summary));
        RequireContent(request.Content);
        RequireText(request.SourcePrompt, 12_000, nameof(request.SourcePrompt));
        NormalizeCategory(request.CategoryPath);
        var scope = NormalizeScope(request.ScopeKind);
        NormalizeScopeKey(scope, request.ScopeKey);
        NormalizeTags(request.Tags);
    }

    private static void ValidateSourceRequest(OrganizationMemorySourceIngestRequest request)
    {
        RequireText(request.Title, 300, nameof(request.Title));
        var uriText = RequireText(request.SourceUri, 1000, nameof(request.SourceUri));
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new OrganizationValidationException("SourceUri must be an absolute HTTP or HTTPS URL.");
        }

        RequireText(request.SourceKind, 80, nameof(request.SourceKind));
        NormalizeSourceGrade(request.Grade);
        var hash = RequireText(request.ContentHash, 128, nameof(request.ContentHash));
        if (hash.Any(x => !Uri.IsHexDigit(x)))
        {
            throw new OrganizationValidationException("ContentHash must contain hexadecimal characters only.");
        }
    }

    private static string NormalizeSourceGrade(string value)
    {
        var grade = value.Trim().ToUpperInvariant();
        if (grade is not OrganizationSourceGrades.Official
            and not OrganizationSourceGrades.ManufacturerOrContract
            and not OrganizationSourceGrades.TrustedExternal
            and not OrganizationSourceGrades.UnverifiedCandidate
            and not OrganizationSourceGrades.DemoAssumption)
        {
            throw new OrganizationValidationException("Unsupported source grade.");
        }

        return grade;
    }

    private static string NormalizeScope(string value)
    {
        var scope = value.Trim().ToLowerInvariant();
        if (!OrganizationMemoryScopes.All.Contains(scope, StringComparer.Ordinal))
        {
            throw new OrganizationValidationException("Unsupported organization memory scope.");
        }

        return scope;
    }

    private static string? NormalizeScopeKey(string scopeKind, string? scopeKey)
    {
        if (scopeKind is OrganizationMemoryScopes.Organization or OrganizationMemoryScopes.PersonalCandidate)
        {
            if (!string.IsNullOrWhiteSpace(scopeKey))
            {
                throw new OrganizationValidationException($"Scope '{scopeKind}' cannot have a scope key.");
            }

            return null;
        }

        return RequireText(scopeKey ?? string.Empty, 240, nameof(scopeKey)).ToLowerInvariant();
    }

    private static string NormalizeCategory(string value)
    {
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToArray();
        if (segments.Length is < 1 or > 6
            || segments.Any(x => x.Length > 48 || x.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_'))))
        {
            throw new OrganizationValidationException("CategoryPath must contain 1 to 6 slash-separated letter, digit, hyphen, or underscore segments.");
        }

        return string.Join('/', segments);
    }

    private static string? NormalizeOptionalCategory(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : NormalizeCategory(value);

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string> tags)
        => tags.Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();

    private static async Task<string> CreateUniqueSlugAsync(
        OrganizationDbContext db,
        Guid organizationId,
        string title,
        CancellationToken cancellationToken)
    {
        var root = SlugGenerator.Normalize(title);
        if (root.Length > 150)
        {
            root = root[..150].Trim('-');
        }

        var candidate = root;
        for (var suffix = 2; suffix <= 1000; suffix++)
        {
            if (!await db.OrganizationMemories.AsNoTracking()
                .AnyAsync(x => x.OrganizationId == organizationId && x.Slug == candidate, cancellationToken))
            {
                return candidate;
            }

            candidate = $"{root}-{suffix}";
        }

        throw new OrganizationConflictException("Unable to create a unique organization memory slug.");
    }

    private static IReadOnlyList<string> SplitTerms(string? query)
        => string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split([' ', '\t', '\r', '\n', ',', '.', '?', '!', '/', ':', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => x.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .Take(20)
                .ToArray();

    private static int CalculateRelevance(OrganizationMemoryRecord memory, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return 0;
        }

        var title = memory.Title.ToLowerInvariant();
        var summary = memory.Summary.ToLowerInvariant();
        var content = memory.Content.ToLowerInvariant();
        var category = memory.CategoryPath.ToLowerInvariant();
        var matches = 0;
        var weighted = 0;
        foreach (var term in terms)
        {
            var matched = false;
            if (title.Contains(term, StringComparison.Ordinal))
            {
                weighted += 4;
                matched = true;
            }

            if (summary.Contains(term, StringComparison.Ordinal))
            {
                weighted += 3;
                matched = true;
            }

            if (category.Contains(term, StringComparison.Ordinal))
            {
                weighted += 2;
                matched = true;
            }

            if (content.Contains(term, StringComparison.Ordinal))
            {
                weighted += 1;
                matched = true;
            }

            if (matched)
            {
                matches++;
            }
        }

        if (matches == 0)
        {
            return 0;
        }

        var coverage = (int)Math.Round(70d * matches / terms.Count);
        var density = Math.Min(30, weighted * 2);
        return Math.Min(100, coverage + density);
    }

    private static OrganizationMemoryResponse ToResponse(OrganizationMemoryRecord record)
        => new(
            record.Id,
            record.OrganizationId,
            record.Slug,
            record.Title,
            record.Summary,
            record.Content,
            record.SourcePrompt,
            DeserializeTags(record.TagsJson),
            record.CategoryPath,
            record.CategoryDepth,
            record.State,
            record.ScopeKind,
            record.ScopeKey,
            record.ProposedBy,
            record.ApprovedBy,
            record.ApprovedAt,
            record.DecisionReason,
            record.SupersedesMemoryId,
            record.Revision,
            record.CreatedAt,
            record.UpdatedAt,
            record.Sources.Select(ToResponse).ToArray());

    private static OrganizationMemorySummaryResponse ToSummary(OrganizationMemoryRecord record, int? relevancePercent)
        => new(
            record.Id,
            record.Slug,
            record.Title,
            record.Summary,
            record.CategoryPath,
            record.State,
            record.ScopeKind,
            record.ScopeKey,
            record.ProposedBy,
            record.ApprovedBy,
            record.UpdatedAt,
            relevancePercent);

    private static OrganizationMemoryRecallResponse ToRecall(OrganizationMemoryRecord record, int? relevancePercent)
        => new(
            record.Id,
            record.Slug,
            record.Title,
            record.Summary,
            record.Content,
            record.CategoryPath,
            record.State,
            record.ScopeKind,
            record.ScopeKey,
            record.ProposedBy,
            record.ApprovedBy,
            record.UpdatedAt,
            relevancePercent);

    private static OrganizationMemorySourceResponse ToResponse(OrganizationMemorySourceRecord record)
        => new(
            record.Id,
            record.OrganizationId,
            record.MemoryId,
            record.Title,
            record.SourceUri,
            record.SourceKind,
            record.Grade,
            record.State,
            record.ContentHash,
            record.CapturedAt,
            record.LastCheckedAt,
            record.Excerpt,
            record.FailureMessage);

    private static OrganizationConflictResponse ToResponse(OrganizationConflictRecord record)
        => new(
            record.Id,
            record.OrganizationId,
            record.FieldName,
            record.LeftValue,
            record.RightValue,
            record.LeftMemoryId,
            record.RightMemoryId,
            record.LeftSourceId,
            record.RightSourceId,
            record.State,
            record.Resolution,
            record.ResolvedBy,
            record.CreatedAt,
            record.ResolvedAt);

    private static IReadOnlyList<string> DeserializeTags(string json)
        => JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static string RequireContent(string value)
        => RequireText(value, MaxContentLength, nameof(value));

    private static string RequireText(string value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
        {
            throw new OrganizationValidationException($"{fieldName} must be between 1 and {maxLength} characters.");
        }

        return normalized;
    }

    private static string? NormalizeNullableText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new OrganizationValidationException($"Text cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
        => NormalizeNullableText(value, maxLength)?.ToLowerInvariant();
}
