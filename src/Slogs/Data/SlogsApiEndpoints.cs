using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Slogs.Data;

public static class SlogsApiEndpoints
{
    public static IEndpointRouteBuilder MapSlogsApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/auth/me", (HttpContext httpContext) =>
        {
            var user = GetCurrentUser(httpContext);
            return user is null ? Results.NoContent() : Results.Ok(user);
        });

        api.MapPost("/auth/login", async (HttpContext httpContext, AuthService authService, AuthRequest request) =>
        {
            var returnUrl = NormalizeLocalReturnUrl(request.ReturnUrl, "/me");
            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new ApiErrorResponse("required"));
            }

            var user = await authService.LoginAsync(request.UserName, request.Password);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await SlogsAuthentication.SignInPersistentAsync(httpContext, user);

            return Results.Ok(new AuthResponse(user, returnUrl));
        });

        api.MapPost("/auth/register", async (HttpContext httpContext, AuthService authService, RegisterRequest request) =>
        {
            var returnUrl = NormalizeLocalReturnUrl(request.ReturnUrl, "/me");
            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new ApiErrorResponse("required"));
            }

            if (request.Password.Length < 4)
            {
                return Results.BadRequest(new ApiErrorResponse("passwordLength"));
            }

            if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
            {
                return Results.BadRequest(new ApiErrorResponse("passwordMismatch"));
            }

            if (request.DisplayName.Length > 30)
            {
                return Results.BadRequest(new ApiErrorResponse("displayNameLength"));
            }

            try
            {
                var user = await authService.RegisterAsync(request.UserName, request.DisplayName, request.Password);
                await SlogsAuthentication.SignInPersistentAsync(httpContext, user);

                return Results.Ok(new AuthResponse(user, returnUrl));
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict(new ApiErrorResponse("duplicate"));
            }
        });

        api.MapPut("/auth/profile", async (HttpContext httpContext, AuthService authService, ProfileUpdateRequest request) =>
        {
            var currentUser = GetCurrentUser(httpContext);
            if (currentUser is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var updatedUser = await authService.UpdateProfileAsync(
                    currentUser.UserName,
                    request.DisplayName,
                    request.Email,
                    request.ProfileImageUrl,
                    request.Bio);

                await SlogsAuthentication.SignInPersistentAsync(httpContext, updatedUser);

                return Results.Ok(new AuthResponse(updatedUser, "/me"));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
        });

        api.MapPost("/auth/logout", async (HttpContext httpContext, AuthService authService) =>
        {
            await authService.LogoutAsync();
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new LogoutResponse(true));
        });

        api.MapPost("/auth/admin-mode/enter", async (HttpContext httpContext, AuthService authService) =>
        {
            var currentUser = GetCurrentUser(httpContext);
            if (currentUser is null)
            {
                return Results.Unauthorized();
            }

            if (!currentUser.CanSwitchToAdminMode)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                var adminUser = await authService.EnterAdminModeAsync(currentUser);
                await SlogsAuthentication.SignInPersistentAsync(httpContext, adminUser);
                return Results.Ok(new AuthResponse(adminUser, "/me"));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
        });

        api.MapPost("/auth/admin-mode/exit", async (HttpContext httpContext, AuthService authService) =>
        {
            var currentUser = GetCurrentUser(httpContext);
            if (currentUser is null)
            {
                return Results.Unauthorized();
            }

            if (!currentUser.CanExitAdminMode)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                var sourceUser = await authService.ExitAdminModeAsync(currentUser);
                await SlogsAuthentication.SignInPersistentAsync(httpContext, sourceUser);
                return Results.Ok(new AuthResponse(sourceUser, "/me"));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
        });

        api.MapGet("/posts/latest", async (BlogService blogService, int? count) =>
            Results.Ok(await blogService.GetLatestAsync(NormalizeCount(count, 15, 500))));

        api.MapGet("/posts/search", async (BlogService blogService, string? q) =>
            Results.Ok(await blogService.SearchPostsAsync(q)));

        api.MapPost("/posts/by-authors", async (BlogService blogService, AuthorsRequest request) =>
            Results.Ok(await blogService.GetByAuthorsAsync(request.Authors)));

        api.MapPost("/posts", async (HttpContext httpContext, BlogService blogService, PostUpsertRequest request) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var post = await blogService.CreatePostAsync(
                request.Title,
                user.UserName,
                request.Summary,
                request.Body,
                request.Tags,
                request.Series,
                request.ThumbnailUrl,
                request.IsDraft.GetValueOrDefault(false),
                request.Slug);

            return Results.Created($"/api/posts/{Uri.EscapeDataString(post.Slug)}", post);
        });

        api.MapGet("/posts/{slug}/read", async (HttpContext httpContext, BlogService blogService, string slug) =>
        {
            var post = await blogService.GetBySlugForReadAsync(slug, GetCurrentUser(httpContext)?.UserName);
            return post is null ? Results.NotFound() : Results.Ok(post);
        });

        api.MapGet("/posts/{slug}/related", async (BlogService blogService, string slug, int? maxCount) =>
            Results.Ok(await blogService.GetRelatedPostsAsync(slug, NormalizeCount(maxCount, 3, 20))));

        api.MapGet("/posts/{slug}/adjacent", async (BlogService blogService, string slug) =>
        {
            var adjacent = await blogService.GetAdjacentPostsAsync(slug);
            return Results.Ok(new AdjacentPostsResponse(adjacent.Previous, adjacent.Next));
        });

        api.MapGet("/posts/{slug}/revisions", async (HttpContext httpContext, BlogService blogService, string slug) =>
            Results.Ok(await blogService.GetPostRevisionsAsync(slug, GetCurrentUser(httpContext)?.UserName)));

        api.MapGet("/posts/{slug}", async (HttpContext httpContext, BlogService blogService, string slug) =>
        {
            var post = await blogService.GetBySlugAsync(slug, GetCurrentUser(httpContext)?.UserName);
            return post is null ? Results.NotFound() : Results.Ok(post);
        });

        api.MapPut("/posts/{slug}", async (HttpContext httpContext, BlogService blogService, string slug, PostUpsertRequest request) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var post = await blogService.UpdatePostAsync(
                slug,
                user.UserName,
                request.Title,
                request.Summary,
                request.Body,
                request.Tags,
                request.Series,
                request.ThumbnailUrl,
                request.IsDraft,
                request.Slug);

            return post is null ? Results.NotFound() : Results.Ok(post);
        });

        api.MapDelete("/posts/{slug}", async (HttpContext httpContext, BlogService blogService, string slug) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var deleted = await blogService.DeletePostAsync(slug, user.UserName);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        api.MapPost("/posts/{slug}/like", async (HttpContext httpContext, BlogService blogService, string slug) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var active = await blogService.ToggleLikeAsync(slug, user.UserName);
            return Results.Ok(new ActionStateResponse(active));
        });

        api.MapPost("/posts/{slug}/bookmark", async (HttpContext httpContext, BlogService blogService, string slug) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var active = await blogService.ToggleBookmarkAsync(slug, user.UserName);
            return Results.Ok(new ActionStateResponse(active));
        });

        api.MapPost("/posts/{slug}/comments", async (HttpContext httpContext, BlogService blogService, string slug, CommentRequest request) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var comment = await blogService.AddCommentAsync(
                slug,
                user.UserName,
                user.DisplayName,
                request.Content,
                request.ParentCommentId);

            return comment is null ? Results.BadRequest(new ApiErrorResponse("commentFailed")) : Results.Ok(comment);
        });

        api.MapPut("/posts/{slug}/comments/{commentId:guid}", async (
            HttpContext httpContext,
            BlogService blogService,
            string slug,
            Guid commentId,
            CommentRequest request) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var updated = await blogService.UpdateCommentAsync(slug, commentId, user.UserName, request.Content, user.IsAdmin);
            return updated ? Results.Ok(new UpdateStateResponse(true)) : Results.NotFound();
        });

        api.MapDelete("/posts/{slug}/comments/{commentId:guid}", async (
            HttpContext httpContext,
            BlogService blogService,
            string slug,
            Guid commentId) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var deleted = await blogService.RemoveCommentAsync(slug, commentId, user.UserName, user.IsAdmin);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        api.MapGet("/tags/trending", async (BlogService blogService, int? topCount) =>
            Results.Ok(await blogService.GetTrendingTagsAsync(NormalizeCount(topCount, 8, 200))));

        api.MapGet("/tags/cloud", async (BlogService blogService, int? topCount) =>
            Results.Ok((await blogService.GetTagCloudAsync(NormalizeCount(topCount, 50, 500)))
                .Select(x => new TagSummary(x.Tag, x.Count))
                .ToList()));

        api.MapGet("/tags/{tag}/posts", async (BlogService blogService, string tag) =>
            Results.Ok(await blogService.GetByTagAsync(tag)));

        api.MapGet("/series/cloud", async (BlogService blogService, int? topCount) =>
            Results.Ok((await blogService.GetSeriesCloudAsync(NormalizeCount(topCount, 50, 500)))
                .Select(x => new SeriesSummary(x.Series, x.Count))
                .ToList()));

        api.MapGet("/series/popular", async (BlogService blogService, int? topCount) =>
            Results.Ok((await blogService.GetPopularSeriesAsync(NormalizeCount(topCount, 8, 200)))
                .Select(x => new SeriesSummary(x.Series, x.Count, x.LikeCount))
                .ToList()));

        api.MapGet("/series/names", async (BlogService blogService, int? topCount) =>
            Results.Ok(await blogService.GetSeriesAsync(NormalizeCount(topCount, 8, 200))));

        api.MapGet("/series/{series}/posts", async (BlogService blogService, string series) =>
            Results.Ok(await blogService.GetBySeriesAsync(series)));

        api.MapGet("/authors/cloud", async (BlogService blogService, int? topCount) =>
            Results.Ok((await blogService.GetAuthorCloudAsync(NormalizeCount(topCount, 50, 500)))
                .Select(x => new AuthorSummary(x.Author, x.Count))
                .ToList()));

        api.MapGet("/authors/{author}/posts", async (BlogService blogService, string author) =>
            Results.Ok(await blogService.GetByAuthorAsync(author)));

        api.MapGet("/authors/{author}/series", async (BlogService blogService, string author, int? topCount) =>
            Results.Ok((await blogService.GetSeriesByAuthorAsync(author, NormalizeCount(topCount, 8, 200)))
                .Select(x => new SeriesSummary(x.Series, x.Count, x.LikeCount))
                .ToList()));

        api.MapGet("/users/{userName}/exists", async (AuthService authService, string userName) =>
            Results.Ok(await authService.IsKnownUserAsync(userName)));

        api.MapGet("/users/{userName}", async (AuthService authService, string userName) =>
        {
            var user = await authService.GetUserAsync(userName);
            return user is null ? Results.NotFound() : Results.Ok(user);
        });

        api.MapPost("/users/by-names", async (AuthService authService, UserNamesRequest request) =>
            Results.Ok(await authService.GetUsersAsync(request.UserNames)));

        api.MapGet("/admin/users/llm-wiki-usage", async (HttpContext httpContext, AuthService authService) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (!user.IsAdmin)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return Results.Ok(await authService.GetAdminUserUsageAsync());
        });

        api.MapPut("/admin/users/{userName}/name", async (
            HttpContext httpContext,
            AuthService authService,
            string userName,
            AdminUserNameUpdateRequest request) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (!user.IsAdmin)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                return Results.Ok(await authService.ChangeAdminUserNameAsync(userName, request.UserName));
            }
            catch (InvalidOperationException ex) when (ex.Message is "adminUserNameTaken")
            {
                return Results.Conflict(new ApiErrorResponse(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
        });

        api.MapGet("/users/{userName}/following", async (AuthService authService, string userName) =>
            Results.Ok(await authService.GetFollowingAsync(userName)));

        api.MapGet("/users/{userName}/followers", async (AuthService authService, string userName) =>
            Results.Ok(await authService.GetFollowersAsync(userName)));

        api.MapGet("/users/{userName}/follower-count", async (AuthService authService, string userName) =>
            Results.Ok(await authService.GetFollowerCountAsync(userName)));

        api.MapGet("/users/{userName}/is-following", async (AuthService authService, string userName, string targetUser) =>
            Results.Ok(await authService.IsFollowingAsync(userName, targetUser)));

        api.MapPost("/users/{targetUser}/follow", async (HttpContext httpContext, AuthService authService, string targetUser) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var active = await authService.ToggleFollowAsync(user.UserName, targetUser);
            return Results.Ok(new ActionStateResponse(active));
        });

        api.MapGet("/me/likes", async (HttpContext httpContext, BlogService blogService) =>
        {
            var user = GetCurrentUser(httpContext);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await blogService.GetLikedPostsAsync(user.UserName));
        });

        api.MapGet("/me/bookmarks", async (HttpContext httpContext, BlogService blogService) =>
        {
            var user = GetCurrentUser(httpContext);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await blogService.GetBookmarkedPostsAsync(user.UserName));
        });

        api.MapGet("/me/posts", async (HttpContext httpContext, BlogService blogService) =>
        {
            var user = GetCurrentUser(httpContext);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await blogService.GetManageByAuthorAsync(user.UserName));
        });

        api.MapGet("/llm-wiki/entries", async (
            HttpContext httpContext,
            LlmWikiService llmWikiService,
            string? q,
            int? limit,
            int? offset,
            int? minRelevancePercent,
            string? categoryPath,
            CancellationToken cancellationToken) =>
        {
            var user = GetCurrentUser(httpContext);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await llmWikiService.SearchAsync(
                    user.UserName,
                    q,
                    NormalizeCount(limit, 20, 100),
                    NormalizeOffset(offset),
                    NormalizeRelevancePercent(minRelevancePercent),
                    categoryPath,
                    cancellationToken));
        });

        api.MapGet("/llm-wiki/categories", async (
            HttpContext httpContext,
            LlmWikiService llmWikiService,
            CancellationToken cancellationToken) =>
        {
            var user = GetCurrentUser(httpContext);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await llmWikiService.GetCategoriesAsync(user.UserName, cancellationToken));
        });

        api.MapPost("/llm-wiki/entries", async (
            HttpContext httpContext,
            LlmWikiService llmWikiService,
            LlmWikiRememberRequest request,
            CancellationToken cancellationToken) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var entry = await llmWikiService.RememberAsync(user.UserName, request, cancellationToken);
                return Results.Created($"/api/llm-wiki/entries/{entry.Id}", entry);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
        });

        api.MapGet("/llm-wiki/entries/{idOrSlug}", async (
            HttpContext httpContext,
            LlmWikiService llmWikiService,
            string idOrSlug,
            CancellationToken cancellationToken) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var entry = await llmWikiService.GetEntryAsync(user.UserName, idOrSlug, cancellationToken);
            return entry is null ? Results.NotFound() : Results.Ok(entry);
        });

        api.MapPut("/llm-wiki/entries/{idOrSlug}", async (
            HttpContext httpContext,
            LlmWikiService llmWikiService,
            string idOrSlug,
            LlmWikiUpdateRequest request,
            CancellationToken cancellationToken) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var entry = await llmWikiService.UpdateAsync(user.UserName, idOrSlug, request, cancellationToken);
                return entry is null ? Results.NotFound() : Results.Ok(entry);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
        });

        api.MapGet("/llm-wiki/llms.txt", async (
            HttpContext httpContext,
            LlmWikiService llmWikiService,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var user = GetCurrentUser(httpContext);
            return user is null
                ? Results.Unauthorized()
                : Results.Text(
                    await llmWikiService.BuildLlmsTextAsync(user.UserName, NormalizeCount(limit, 50, 200), cancellationToken),
                    "text/markdown; charset=utf-8");
        });

        api.MapGet("/llm-wiki/tokens", async (
            HttpContext httpContext,
            LlmWikiService llmWikiService,
            CancellationToken cancellationToken) =>
        {
            var user = GetCurrentUser(httpContext);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await llmWikiService.GetTokensAsync(user.UserName, cancellationToken));
        });

        api.MapPost("/llm-wiki/tokens", async (
            HttpContext httpContext,
            LlmWikiService llmWikiService,
            LlmWikiTokenCreateRequest request,
            CancellationToken cancellationToken) =>
        {
            var user = GetCurrentUser(httpContext);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await llmWikiService.CreateTokenAsync(user.UserName, request.Name, cancellationToken));
        });

        api.MapDelete("/llm-wiki/tokens/{tokenId:guid}", async (
            HttpContext httpContext,
            LlmWikiService llmWikiService,
            Guid tokenId,
            CancellationToken cancellationToken) =>
        {
            var user = GetCurrentUser(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var revoked = await llmWikiService.RevokeTokenAsync(user.UserName, tokenId, cancellationToken);
            return revoked ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }

    private static AuthUser? GetCurrentUser(HttpContext httpContext)
        => SlogsAuthentication.TryCreateUser(httpContext.User);

    private static int NormalizeCount(int? count, int defaultValue, int maxValue)
        => Math.Clamp(count.GetValueOrDefault(defaultValue), 1, maxValue);

    private static int NormalizeOffset(int? offset)
        => Math.Clamp(offset.GetValueOrDefault(0), 0, 10_000);

    private static int NormalizeRelevancePercent(int? minRelevancePercent)
        => Math.Clamp(minRelevancePercent.GetValueOrDefault(50), 0, 100);

    private static string NormalizeLocalReturnUrl(string? returnUrl, string fallback)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || !Uri.TryCreate(returnUrl, UriKind.RelativeOrAbsolute, out var parsedUrl))
        {
            return fallback;
        }

        if (!parsedUrl.IsAbsoluteUri && parsedUrl.OriginalString.StartsWith('/'))
        {
            return parsedUrl.OriginalString;
        }

        return fallback;
    }
}
