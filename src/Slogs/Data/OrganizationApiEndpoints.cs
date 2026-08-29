namespace Slogs.Data;

public static class OrganizationApiEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationApi(this IEndpointRouteBuilder endpoints)
    {
        var guidedAccess = endpoints.MapGroup("/api/organization-guided-access")
            .AllowAnonymous();
        guidedAccess.AddEndpointFilter(HandleOrganizationErrorsAsync);
        guidedAccess.MapPost("/start", async (
            OrganizationGuidedAccessStartRequest request,
            OrganizationGuidedAccessService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.StartAsync(request, cancellationToken)));
        guidedAccess.MapPost("/switch", async (
            OrganizationGuidedAccessSwitchRequest request,
            OrganizationGuidedAccessService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.SwitchAsync(request, cancellationToken)));

        var api = endpoints.MapGroup("/api/organizations")
            .RequireAuthorization(OrganizationPlatformExtensions.OrganizationApiPolicy);
        api.AddEndpointFilter(HandleOrganizationErrorsAsync);

        api.MapPost("/", async (
            HttpContext httpContext,
            OrganizationCreateRequest request,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
        {
            var user = SlogsAuthentication.TryCreateUser(httpContext.User)
                ?? throw new OrganizationAccessDeniedException("A Slogs administrator session is required.");
            return Results.Ok(await service.CreateAsync(request, user, cancellationToken));
        });

        api.MapGet("/me", async (
            HttpContext httpContext,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
        {
            var userName = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? throw new OrganizationAccessDeniedException("Authenticated user identifier is required.");
            return Results.Ok(await service.ListForUserAsync(userName, cancellationToken));
        });

        api.MapGet("/all", async (
            HttpContext httpContext,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
        {
            var user = SlogsAuthentication.TryCreateUser(httpContext.User)
                ?? throw new OrganizationAccessDeniedException("A Slogs administrator session is required.");
            return Results.Ok(await service.ListAllAsync(user, cancellationToken));
        });

        api.MapPut("/{organizationId:guid}", async (
            Guid organizationId,
            OrganizationUpdateRequest request,
            HttpContext httpContext,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(organizationId, request, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/members", async (
            Guid organizationId,
            HttpContext httpContext,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListMembershipsAsync(organizationId, httpContext.User, cancellationToken)));

        api.MapPut("/{organizationId:guid}/members", async (
            Guid organizationId,
            OrganizationMembershipUpsertRequest request,
            HttpContext httpContext,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpsertMembershipAsync(organizationId, request, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/units", async (
            Guid organizationId,
            HttpContext httpContext,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListUnitsAsync(organizationId, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/units", async (
            Guid organizationId,
            OrganizationUnitCreateRequest request,
            HttpContext httpContext,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateUnitAsync(organizationId, request, httpContext.User, cancellationToken)));

        api.MapPut("/{organizationId:guid}/units/{unitId:guid}/members", async (
            Guid organizationId,
            Guid unitId,
            OrganizationUnitMembershipRequest request,
            HttpContext httpContext,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
        {
            await service.AddUnitMembershipAsync(organizationId, unitId, request, httpContext.User, cancellationToken);
            return Results.NoContent();
        });

        api.MapPost("/{organizationId:guid}/memories/capture", async (
            Guid organizationId,
            OrganizationMemoryDraftRequest request,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CaptureAsync(organizationId, request, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/memories/propose", async (
            Guid organizationId,
            OrganizationMemoryDraftRequest request,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ProposeAsync(organizationId, request, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/memories/{memoryId:guid}", async (
            Guid organizationId,
            Guid memoryId,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ReadAsync(organizationId, memoryId, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/memories/{memoryId:guid}/revisions", async (
            Guid organizationId,
            Guid memoryId,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.RevisionsAsync(organizationId, memoryId, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/memories", async (
            Guid organizationId,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken,
            string? query = null,
            string? categoryPath = null,
            string? scopeKind = null,
            string? scopeKey = null,
            int limit = 10) =>
            Results.Ok(await service.SearchAsync(
                organizationId,
                query,
                categoryPath,
                scopeKind,
                scopeKey,
                limit,
                httpContext.User,
                cancellationToken)));

        api.MapPost("/{organizationId:guid}/memories/{memoryId:guid}/approve", async (
            Guid organizationId,
            Guid memoryId,
            OrganizationMemoryDecisionRequest request,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ApproveAsync(organizationId, memoryId, request, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/memories/{memoryId:guid}/reject", async (
            Guid organizationId,
            Guid memoryId,
            OrganizationMemoryDecisionRequest request,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.RejectAsync(organizationId, memoryId, request, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/memories/{memoryId:guid}/withdraw", async (
            Guid organizationId,
            Guid memoryId,
            OrganizationMemoryDecisionRequest request,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.WithdrawAsync(organizationId, memoryId, request, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/categories", async (
            Guid organizationId,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CategoriesAsync(organizationId, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/sources", async (
            Guid organizationId,
            OrganizationMemorySourceIngestRequest request,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.IngestSourceAsync(organizationId, request, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/sources", async (
            Guid organizationId,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListSourcesAsync(organizationId, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/conflicts", async (
            Guid organizationId,
            OrganizationConflictCreateRequest request,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateConflictAsync(organizationId, request, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/conflicts", async (
            Guid organizationId,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken,
            bool pendingOnly = true) =>
            Results.Ok(await service.ListConflictsAsync(organizationId, pendingOnly, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/conflicts/{conflictId:guid}/resolve", async (
            Guid organizationId,
            Guid conflictId,
            OrganizationConflictDecisionRequest request,
            HttpContext httpContext,
            OrganizationWikiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ResolveConflictAsync(organizationId, conflictId, request, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/service-tokens", async (
            Guid organizationId,
            OrganizationServiceTokenCreateRequest request,
            HttpContext httpContext,
            OrganizationTokenService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateAsync(organizationId, request, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/service-tokens", async (
            Guid organizationId,
            HttpContext httpContext,
            OrganizationTokenService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(organizationId, httpContext.User, cancellationToken)));

        api.MapDelete("/{organizationId:guid}/service-tokens/{tokenId:guid}", async (
            Guid organizationId,
            Guid tokenId,
            HttpContext httpContext,
            OrganizationTokenService service,
            CancellationToken cancellationToken) =>
            await service.RevokeAsync(organizationId, tokenId, httpContext.User, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound());

        api.MapPost("/{organizationId:guid}/metrics/events", async (
            Guid organizationId,
            OrganizationMetricEventRequest request,
            HttpContext httpContext,
            OrganizationMetricsService service,
            CancellationToken cancellationToken) =>
        {
            await service.RecordAsync(organizationId, request, httpContext.User, cancellationToken);
            return Results.NoContent();
        });

        api.MapGet("/{organizationId:guid}/metrics", async (
            Guid organizationId,
            DateTime from,
            DateTime to,
            HttpContext httpContext,
            OrganizationMetricsService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.SummarizeAsync(organizationId, from, to, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/audits", async (
            Guid organizationId,
            HttpContext httpContext,
            OrganizationMetricsService service,
            CancellationToken cancellationToken,
            int limit = 100) =>
            Results.Ok(await service.ListAuditsAsync(organizationId, limit, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/oidc-clients", async (
            Guid organizationId,
            OrganizationOidcClientCreateRequest request,
            HttpContext httpContext,
            OrganizationOidcClientService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateAsync(organizationId, request, httpContext.User, cancellationToken)));

        api.MapGet("/{organizationId:guid}/oidc-clients", async (
            Guid organizationId,
            HttpContext httpContext,
            OrganizationOidcClientService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(organizationId, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/oidc-clients/{clientId:guid}/rotate-secret", async (
            Guid organizationId,
            Guid clientId,
            HttpContext httpContext,
            OrganizationOidcClientService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.RotateSecretAsync(organizationId, clientId, httpContext.User, cancellationToken)));

        api.MapDelete("/{organizationId:guid}/oidc-clients/{clientId:guid}", async (
            Guid organizationId,
            Guid clientId,
            HttpContext httpContext,
            OrganizationOidcClientService service,
            CancellationToken cancellationToken) =>
            await service.RevokeAsync(organizationId, clientId, httpContext.User, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound());

        api.MapPost("/{organizationId:guid}/guided-sessions", async (
            Guid organizationId,
            OrganizationGuidedSessionCreateRequest request,
            HttpContext httpContext,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.StartGuidedSessionAsync(organizationId, request, httpContext.User, cancellationToken)));

        api.MapPost("/{organizationId:guid}/guided-sessions/{sessionId:guid}/switch", async (
            Guid organizationId,
            Guid sessionId,
            OrganizationGuidedSessionSwitchRequest request,
            HttpContext httpContext,
            OrganizationDirectoryService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.SwitchGuidedSessionAsync(organizationId, sessionId, request, httpContext.User, cancellationToken)));

        return endpoints;
    }

    private static async ValueTask<object?> HandleOrganizationErrorsAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (OrganizationAccessDeniedException exception)
        {
            return Results.Json(new ApiErrorResponse(exception.Message), statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OrganizationNotFoundException exception)
        {
            return Results.NotFound(new ApiErrorResponse(exception.Message));
        }
        catch (OrganizationConflictException exception)
        {
            return Results.Conflict(new ApiErrorResponse(exception.Message));
        }
        catch (OrganizationValidationException exception)
        {
            return Results.BadRequest(new ApiErrorResponse(exception.Message));
        }
    }
}
