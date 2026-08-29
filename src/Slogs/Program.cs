using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using OpenIddict.Validation.AspNetCore;
using Slogs.Components;
using Slogs.Data;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);
const string GooglePictureClaim = "urn:google:picture";
const string ExternalLoginScheme = "slogs.external";
const string DefaultProductionPublicBaseUrl = "https://slogs.dev/";

ConfigureDataProtection(builder.Services, builder.Configuration, builder.Environment);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, SlogsJsonSerializerContext.Default);
});
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedHost
        | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
#pragma warning disable ASPDEPR005
    options.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
    options.KnownProxies.Clear();
});
var authenticationBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "slogs.auth";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = SlogsAuthentication.PersistentSessionLifetime;
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api")
                    || context.Request.Path.StartsWithSegments("/mcp"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api")
                    || context.Request.Path.StartsWithSegments("/mcp"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    })
    .AddCookie(ExternalLoginScheme, options =>
    {
        options.Cookie.Name = "slogs.external";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    });

if (IsGoogleAuthenticationConfigured(builder.Configuration))
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
        options.SignInScheme = ExternalLoginScheme;
        options.SaveTokens = false;
        options.ClaimActions.MapJsonKey(GooglePictureClaim, "picture");
        options.Events.OnCreatingTicket = context =>
        {
            var providerUserId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(providerUserId))
            {
                context.Fail("Google account identifier is missing.");
            }

            return Task.CompletedTask;
        };
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            var returnUrl = context.Properties?.Items.TryGetValue("returnUrl", out var originalReturnUrl) == true
                ? NormalizeLocalReturnUrl(originalReturnUrl, "/me")
                : "/me";
            context.Response.Redirect(BuildAuthRedirect("/login", returnUrl, "googleFailed"));
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization();
var connectionString = builder.Configuration.GetConnectionString("SlogsDatabase")
    ?? "Host=localhost;Port=54329;Database=slogs;Username=slogs;Password=slogs_dev_password";
builder.Services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddOrganizationPlatform(connectionString, builder.Configuration, builder.Environment);
builder.Services.AddScoped<BlogService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<EditorImageStorage>();
builder.Services.AddScoped<PostImageService>();
builder.Services.AddHttpClient<BgeM3EmbeddingService>((serviceProvider, httpClient) =>
    BgeM3EmbeddingService.ConfigureHttpClient(
        httpClient,
        serviceProvider.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<IKnowledgeEmbeddingService>(provider => provider.GetRequiredService<BgeM3EmbeddingService>());
builder.Services.AddScoped<BgeM3ShadowIndexMigration>();
builder.Services.AddScoped<LlmWikiService>();
builder.Services.AddScoped<SlogsMcpPolicyPromptService>();
builder.Services.AddScoped<KnowledgeCorpusService>();
builder.Services.AddSingleton<KnowledgeChunkingService>();
builder.Services.AddSingleton<BibleKnowledgeCorpusAdapter>();
builder.Services.AddSingleton<BibleCorpusPackageReader>();
builder.Services.AddSingleton<BibleOriginalKnowledgeCorpusAdapter>();
builder.Services.AddScoped<BibleCorpusImportRunner>();
builder.Services.AddScoped<BibleCorpusImportOrchestrator>();
builder.Services.AddScoped<BibleReviewedRelationsImportOrchestrator>();
builder.Services.AddScoped<BibleCorpusEvaluationRunner>();
builder.Services.AddScoped<KnowledgeCorpusPrincipalResolver>();
builder.Services.AddScoped<ObsidianVaultService>();
builder.Services.AddScoped<ObsidianStorageQuotaService>();
builder.Services.AddScoped<ISlogsApiBackend, ServerSlogsApiBackend>();
builder.Services.AddScoped<SlogsAuthState>();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<LlmWikiMcpTools>()
    .WithTools<SlogsPostMcpTools>()
    .WithTools<KnowledgeCorpusMcpTools>()
    .WithTools<OrganizationWikiMcpTools>();
builder.Services.AddHttpClient<SlogsApiClient>((serviceProvider, httpClient) =>
{
    var request = serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request;
    httpClient.BaseAddress = request is null
        ? new Uri("https://localhost:5000/")
        : new Uri(GetRequestBaseUri(request));

    var cookieHeader = request?.Headers.Cookie.ToString();
    if (!string.IsNullOrWhiteSpace(cookieHeader))
    {
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieHeader);
    }
});
builder.Services.AddHttpClient<OrganizationApiClient>((serviceProvider, httpClient) =>
{
    var request = serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request;
    httpClient.BaseAddress = request is null
        ? new Uri("https://localhost:5000/")
        : new Uri(GetRequestBaseUri(request));

    var cookieHeader = request?.Headers.Cookie.ToString();
    if (!string.IsNullOrWhiteSpace(cookieHeader))
    {
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieHeader);
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
var publicBaseUri = GetConfiguredPublicBaseUri(app.Configuration)
    ?? (app.Environment.IsProduction() ? new Uri(DefaultProductionPublicBaseUrl) : null);
if (publicBaseUri is not null)
{
    app.Logger.LogInformation("Using public base URL {PublicBaseUrl}.", publicBaseUri);
    app.Use((httpContext, next) =>
    {
        httpContext.Request.Scheme = publicBaseUri.Scheme;
        httpContext.Request.Host = ToHostString(publicBaseUri);
        httpContext.Request.PathBase = ToPathBase(publicBaseUri);

        return next(httpContext);
    });
}

app.Use(async (httpContext, next) =>
{
    if (ShouldExposeLlmsDiscoveryHeaders(httpContext.Request))
    {
        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers["Link"] = string.Join(", ", [
                $"</feed.xml>; rel=\"alternate\"; type=\"application/rss+xml\"; title=\"{SeoMetadata.PublicFeedTitle} RSS\"",
                $"</atom.xml>; rel=\"alternate\"; type=\"application/atom+xml\"; title=\"{SeoMetadata.PublicFeedTitle} Atom\"",
                $"</feed.json>; rel=\"alternate\"; type=\"application/feed+json\"; title=\"{SeoMetadata.PublicFeedTitle} JSON\"",
                "</llms.txt>; rel=\"alternate llms-txt\"; type=\"text/markdown\"; title=\"llms.txt\"",
                "</llms-full.txt>; rel=\"alternate llms-full-txt\"; type=\"text/markdown\"; title=\"llms-full.txt\""
            ]);
            httpContext.Response.Headers["X-Llms-Txt"] = "/llms.txt";
            return Task.CompletedTask;
        });
    }

    await next(httpContext);
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseWhen(
    httpContext => !httpContext.Request.Path.StartsWithSegments("/api")
        && !httpContext.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseStaticFiles();
app.UseAuthentication();
app.Use(async (httpContext, next) =>
{
    if (httpContext.User.Identity?.IsAuthenticated != true
        && TryGetBearerToken(httpContext.Request, out var guidedBearerToken)
        && guidedBearerToken.StartsWith(OrganizationGuidedAccessService.TokenPrefix, StringComparison.Ordinal))
    {
        var guidedAccessService = httpContext.RequestServices.GetRequiredService<OrganizationGuidedAccessService>();
        var principal = await guidedAccessService.AuthenticateAsync(guidedBearerToken, httpContext.RequestAborted);
        if (principal is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        httpContext.User = principal;
    }
    else if (httpContext.User.Identity?.IsAuthenticated != true
        && TryGetBearerToken(httpContext.Request, out var bearerToken)
        && bearerToken.StartsWith(OrganizationTokenService.ServiceTokenPrefix, StringComparison.Ordinal))
    {
        var organizationTokenService = httpContext.RequestServices.GetRequiredService<OrganizationTokenService>();
        var principal = await organizationTokenService.AuthenticateAsync(bearerToken, httpContext.RequestAborted);
        if (principal is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        httpContext.User = principal;
    }
    else if (TryGetRequiredBearerScope(httpContext.Request.Path, out var requiredBearerScope)
        && httpContext.User.Identity?.IsAuthenticated != true
        && TryGetBearerToken(httpContext.Request, out bearerToken))
    {
        var llmWikiService = httpContext.RequestServices.GetRequiredService<LlmWikiService>();
        var tokenAuthentication = await llmWikiService.AuthenticateBearerTokenAsync(
            bearerToken,
            requiredBearerScope,
            httpContext.RequestAborted);
        if (!tokenAuthentication.IsScopeAllowed)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (tokenAuthentication.User is not null)
        {
            httpContext.User = SlogsAuthentication.CreatePrincipal(
                tokenAuthentication.User,
                tokenAuthentication.Scopes,
                tokenAuthentication.TokenId);
        }
    }

    if (httpContext.Request.Path.StartsWithSegments("/api/organizations")
        && httpContext.User.Identity?.IsAuthenticated != true
        && TryGetBearerToken(httpContext.Request, out _))
    {
        var oidcAuthentication = await httpContext.AuthenticateAsync(
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        if (!oidcAuthentication.Succeeded || oidcAuthentication.Principal is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        httpContext.User = oidcAuthentication.Principal;
    }

    if (httpContext.Request.Path.StartsWithSegments("/mcp")
        && httpContext.User.Identity?.IsAuthenticated != true)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next(httpContext);
});
app.UseAuthorization();
app.UseAntiforgery();

app.MapSlogsApi();
app.MapOrganizationApi();
app.MapOrganizationOidcEndpoints();
app.MapMcp("/mcp").RequireAuthorization();

app.MapPost("/auth/login", async (HttpContext httpContext, AuthService authService) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var returnUrl = NormalizeLocalReturnUrl(GetFormValue(form, "returnUrl"), "/me");
    var userName = GetFormValue(form, "userName");
    var password = GetFormValue(form, "password");

    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
    {
        return Results.Redirect(BuildAuthRedirect("/login", returnUrl, "required"));
    }

    var user = await authService.LoginAsync(userName, password);
    if (user is null)
    {
        return Results.Redirect(BuildAuthRedirect("/login", returnUrl, "invalid"));
    }

    await SlogsAuthentication.SignInPersistentAsync(httpContext, user);

    return Results.Redirect(returnUrl);
}).DisableAntiforgery();

app.MapPost("/auth/register", async (HttpContext httpContext, AuthService authService) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var returnUrl = NormalizeLocalReturnUrl(GetFormValue(form, "returnUrl"), "/me");
    var userName = GetFormValue(form, "userName");
    var displayName = GetFormValue(form, "displayName");
    var password = GetFormValue(form, "password");
    var confirmPassword = GetFormValue(form, "confirmPassword");
    var profileImageUrl = GetFormValue(form, "profileImageUrl");
    var bio = GetFormValue(form, "bio");

    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
    {
        return Results.Redirect(BuildAuthRedirect("/register", returnUrl, "required"));
    }

    if (password.Length < 4)
    {
        return Results.Redirect(BuildAuthRedirect("/register", returnUrl, "passwordLength"));
    }

    if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
    {
        return Results.Redirect(BuildAuthRedirect("/register", returnUrl, "passwordMismatch"));
    }

    if (displayName.Length > 30)
    {
        return Results.Redirect(BuildAuthRedirect("/register", returnUrl, "displayNameLength"));
    }

    try
    {
        var user = await authService.RegisterAsync(userName, displayName, password, profileImageUrl, bio);
        await SlogsAuthentication.SignInPersistentAsync(httpContext, user);

        return Results.Redirect(returnUrl);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Redirect(BuildAuthRedirect("/register", returnUrl, MapRegisterErrorCode(ex.Message)));
    }
}).DisableAntiforgery();

app.MapGet("/auth/google", (HttpContext httpContext) =>
{
    var returnUrl = NormalizeLocalReturnUrl(httpContext.Request.Query["returnUrl"].ToString(), "/me");
    if (!IsGoogleAuthenticationConfigured(app.Configuration))
    {
        return Results.Redirect(BuildAuthRedirect("/login", returnUrl, "googleNotConfigured"));
    }

    var properties = new AuthenticationProperties
    {
        RedirectUri = $"/auth/google/confirm?returnUrl={Uri.EscapeDataString(returnUrl)}"
    };
    properties.Items["returnUrl"] = returnUrl;

    return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
});

app.MapGet("/auth/google/confirm", async (HttpContext httpContext, AuthService authService) =>
{
    var returnUrl = NormalizeLocalReturnUrl(httpContext.Request.Query["returnUrl"].ToString(), "/me");
    var externalLogin = await ReadGoogleExternalLoginAsync(httpContext);
    if (externalLogin is null)
    {
        return Results.Redirect(BuildAuthRedirect("/login", returnUrl, "googleFailed"));
    }

    var existingUser = await authService.LoginExternalAsync(
        "google",
        externalLogin.ProviderUserId,
        externalLogin.Email,
        externalLogin.DisplayName,
        externalLogin.ProfileImageUrl);
    if (existingUser is not null)
    {
        await httpContext.SignOutAsync(ExternalLoginScheme);
        await SlogsAuthentication.SignInPersistentAsync(httpContext, existingUser);
        return Results.Redirect(returnUrl);
    }

    var candidateUserName = await authService.CreateExternalUserNameCandidateAsync(
        "google",
        externalLogin.Email,
        externalLogin.DisplayName);
    return Results.Content(
        BuildGoogleConfirmPage(
            returnUrl,
            candidateUserName,
            externalLogin.DisplayName,
            externalLogin.Email,
            externalLogin.ProfileImageUrl,
            httpContext.Request.Query["error"].ToString()),
        "text/html; charset=utf-8");
});

app.MapPost("/auth/google/confirm", async (HttpContext httpContext, AuthService authService) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var returnUrl = NormalizeLocalReturnUrl(GetFormValue(form, "returnUrl"), "/me");
    var intent = GetFormValue(form, "intent");
    if (intent.Equals("cancel", StringComparison.OrdinalIgnoreCase))
    {
        await httpContext.SignOutAsync(ExternalLoginScheme);
        return Results.Redirect(BuildAuthRedirect("/login", returnUrl, "googleCanceled"));
    }

    var externalLogin = await ReadGoogleExternalLoginAsync(httpContext);
    if (externalLogin is null)
    {
        return Results.Redirect(BuildAuthRedirect("/login", returnUrl, "googleFailed"));
    }

    var requestedUserName = GetFormValue(form, "userName");
    try
    {
        var user = await authService.CreateConfirmedExternalLoginAsync(
            "google",
            externalLogin.ProviderUserId,
            externalLogin.Email,
            externalLogin.DisplayName,
            externalLogin.ProfileImageUrl,
            requestedUserName);

        await httpContext.SignOutAsync(ExternalLoginScheme);
        await SlogsAuthentication.SignInPersistentAsync(httpContext, user);
        return Results.Redirect(returnUrl);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Content(
            BuildGoogleConfirmPage(
                returnUrl,
                requestedUserName,
                externalLogin.DisplayName,
                externalLogin.Email,
                externalLogin.ProfileImageUrl,
                ex.Message),
            "text/html; charset=utf-8");
    }
}).DisableAntiforgery();

app.MapPost("/auth/logout", async (HttpContext httpContext, AuthService authService) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var returnUrl = NormalizeLocalReturnUrl(GetFormValue(form, "returnUrl"), "/");

    await authService.LogoutAsync();
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.Redirect(returnUrl);
}).DisableAntiforgery();

app.MapPost("/editor/images", async (
    HttpContext httpContext,
    EditorImageStorage imageStorage,
    PostImageService postImageService) =>
{
    var user = SlogsAuthentication.TryCreateUser(httpContext.User);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    if (!httpContext.Request.HasFormContentType)
    {
        return Results.BadRequest(new ApiErrorResponse("이미지 파일을 찾을 수 없습니다."));
    }

    var form = await httpContext.Request.ReadFormAsync();
    var file = form.Files.GetFile("image");
    if (file is null)
    {
        return Results.BadRequest(new ApiErrorResponse("이미지 파일을 찾을 수 없습니다."));
    }

    try
    {
        await using var source = file.OpenReadStream();
        var response = await imageStorage.SaveAsync(
            source,
            file.FileName,
            file.ContentType,
            file.Length,
            httpContext.RequestAborted);
        await postImageService.RegisterUploadAsync(user.UserName, response.Url, httpContext.RequestAborted);

        return Results.Ok(response);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ApiErrorResponse(ex.Message));
    }
}).DisableAntiforgery();

var getAndHeadMethods = new[] { HttpMethods.Get, HttpMethods.Head };

app.MapMethods("/robots.txt", getAndHeadMethods, () =>
{
    return Results.Text(SeoMetadata.BuildRobotsTxt(DefaultProductionPublicBaseUrl), "text/plain; charset=utf-8");
});

app.MapMethods(SlogsMcpPolicyPrompt.PublicPath, getAndHeadMethods, async (HttpContext httpContext, SlogsMcpPolicyPromptService promptService) =>
{
    httpContext.Response.Headers.CacheControl = "no-cache";
    return Results.Text((await promptService.GetAsync()).KoreanMarkdown, "text/markdown; charset=utf-8");
});

app.MapMethods(SlogsMcpPolicyPrompt.KoreanPublicPath, getAndHeadMethods, async (HttpContext httpContext, SlogsMcpPolicyPromptService promptService) =>
{
    httpContext.Response.Headers.CacheControl = "no-cache";
    return Results.Text(
        (await promptService.GetAsync()).KoreanMarkdown,
        "text/markdown; charset=utf-8");
});

app.MapMethods(SlogsMcpPolicyPrompt.EnglishPublicPath, getAndHeadMethods, async (HttpContext httpContext, SlogsMcpPolicyPromptService promptService) =>
{
    httpContext.Response.Headers.CacheControl = "no-cache";
    return Results.Text(
        (await promptService.GetAsync()).EnglishMarkdown,
        "text/markdown; charset=utf-8");
});

app.MapMethods(SlogsMcpPolicyPrompt.VersionPath, getAndHeadMethods, async (HttpContext httpContext, SlogsMcpPolicyPromptService promptService) =>
{
    httpContext.Response.Headers.CacheControl = "no-cache";
    return Results.Text(
        $"{(await promptService.GetAsync()).Version}\n",
        "text/plain; charset=utf-8");
});

app.MapMethods("/llms.txt", getAndHeadMethods, async (
    HttpContext httpContext,
    BlogService blogService) =>
{
    var posts = await blogService.GetLatestAsync(500);
    var tags = await blogService.GetTagCloudAsync(100);
    var series = await blogService.GetSeriesCloudAsync(100);
    var authors = await blogService.GetAuthorCloudAsync(100);

    httpContext.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Text(
        SeoMetadata.BuildLlmsTxt(DefaultProductionPublicBaseUrl, posts, tags, series, authors),
        "text/markdown; charset=utf-8");
});

app.MapMethods("/.well-known/llms.txt", getAndHeadMethods, async (
    HttpContext httpContext,
    BlogService blogService) =>
{
    var posts = await blogService.GetLatestAsync(500);
    var tags = await blogService.GetTagCloudAsync(100);
    var series = await blogService.GetSeriesCloudAsync(100);
    var authors = await blogService.GetAuthorCloudAsync(100);

    httpContext.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Text(
        SeoMetadata.BuildLlmsTxt(DefaultProductionPublicBaseUrl, posts, tags, series, authors),
        "text/markdown; charset=utf-8");
});

app.MapMethods("/llms-full.txt", getAndHeadMethods, async (
    HttpContext httpContext,
    BlogService blogService) =>
{
    var posts = await blogService.GetLatestAsync(500);

    httpContext.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Text(
        SeoMetadata.BuildLlmsFullTxt(DefaultProductionPublicBaseUrl, posts),
        "text/markdown; charset=utf-8");
});

app.MapMethods("/.well-known/llms-full.txt", getAndHeadMethods, async (
    HttpContext httpContext,
    BlogService blogService) =>
{
    var posts = await blogService.GetLatestAsync(500);

    httpContext.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Text(
        SeoMetadata.BuildLlmsFullTxt(DefaultProductionPublicBaseUrl, posts),
        "text/markdown; charset=utf-8");
});

app.MapMethods("/feed.xml", getAndHeadMethods, async (
    HttpContext httpContext,
    BlogService blogService) =>
{
    var posts = await blogService.GetLatestAsync(500);

    httpContext.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Text(
        SeoMetadata.BuildRssFeedXml(DefaultProductionPublicBaseUrl, posts),
        "application/rss+xml; charset=utf-8");
});

app.MapMethods("/rss.xml", getAndHeadMethods, async (
    HttpContext httpContext,
    BlogService blogService) =>
{
    var posts = await blogService.GetLatestAsync(500);

    httpContext.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Text(
        SeoMetadata.BuildRssFeedXml(DefaultProductionPublicBaseUrl, posts),
        "application/rss+xml; charset=utf-8");
});

app.MapMethods("/atom.xml", getAndHeadMethods, async (
    HttpContext httpContext,
    BlogService blogService) =>
{
    var posts = await blogService.GetLatestAsync(500);

    httpContext.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Text(
        SeoMetadata.BuildAtomFeedXml(DefaultProductionPublicBaseUrl, posts),
        "application/atom+xml; charset=utf-8");
});

app.MapMethods("/feed.json", getAndHeadMethods, async (
    HttpContext httpContext,
    BlogService blogService) =>
{
    var posts = await blogService.GetLatestAsync(500);

    httpContext.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Text(
        SeoMetadata.BuildJsonFeed(DefaultProductionPublicBaseUrl, posts),
        "application/feed+json; charset=utf-8");
});

app.MapMethods("/@{author}/{slug}.md", getAndHeadMethods, async (
    HttpContext httpContext,
    BlogService blogService,
    string author,
    string slug) =>
{
    var post = await blogService.GetBySlugAsync(slug);
    if (post is null || post.IsDraft || !post.Author.Equals(author, StringComparison.OrdinalIgnoreCase))
    {
        return Results.NotFound();
    }

    httpContext.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Text(
        SeoMetadata.BuildPostMarkdown(DefaultProductionPublicBaseUrl, post),
        "text/markdown; charset=utf-8");
});

app.MapMethods("/post/{slug}.md", getAndHeadMethods, async (
    HttpContext httpContext,
    BlogService blogService,
    string slug) =>
{
    var post = await blogService.GetBySlugAsync(slug);
    if (post is null || post.IsDraft)
    {
        return Results.NotFound();
    }

    httpContext.Response.Headers.CacheControl = "public, max-age=600";
    return Results.Text(
        SeoMetadata.BuildPostMarkdown(DefaultProductionPublicBaseUrl, post),
        "text/markdown; charset=utf-8");
});

app.MapMethods("/sitemap.xml", getAndHeadMethods, async (
    BlogService blogService) =>
{
    var posts = await blogService.GetLatestAsync(500);
    var tags = await blogService.GetTagCloudAsync(200);
    var series = await blogService.GetSeriesCloudAsync(200);
    var authors = await blogService.GetAuthorCloudAsync(200);

    var entries = new List<SitemapEntry>
    {
        new("/", DateTime.UtcNow, "daily", 1.0m),
        new("/recent", DateTime.UtcNow, "daily", 0.9m),
        new("/trending", DateTime.UtcNow, "daily", 0.9m),
        new("/recommended", DateTime.UtcNow, "daily", 0.8m),
        new("/post", DateTime.UtcNow, "daily", 0.8m),
        new("/tag", DateTime.UtcNow, "weekly", 0.7m),
        new("/series", DateTime.UtcNow, "weekly", 0.7m),
        new("/writer", DateTime.UtcNow, "weekly", 0.7m)
    };

    entries.AddRange(posts.Select(post => new SitemapEntry(
        $"/@{Uri.EscapeDataString(post.Author)}/{Uri.EscapeDataString(post.Slug)}",
        post.UpdatedAt,
        "weekly",
        0.9m)));
    entries.AddRange(tags.Select(tag => new SitemapEntry(
        $"/tag/{Uri.EscapeDataString(tag.Tag)}",
        DateTime.UtcNow,
        "weekly",
        0.7m)));
    entries.AddRange(series.Select(item => new SitemapEntry(
        $"/series/{Uri.EscapeDataString(item.Series)}",
        DateTime.UtcNow,
        "weekly",
        0.7m)));
    entries.AddRange(authors.Select(author => author.Author).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(author => new SitemapEntry(
            $"/@{Uri.EscapeDataString(author)}",
            DateTime.UtcNow,
            "weekly",
            0.8m)));

    return Results.Text(SeoMetadata.BuildSitemapXml(DefaultProductionPublicBaseUrl, entries), "application/xml; charset=utf-8");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Slogs.Components.Routes).Assembly);

var hasBibleCorpusImport = TryReadBibleCorpusImportArguments(args, out var bibleCorpusImport);
var hasBibleReviewedRelationsImport = TryReadBibleReviewedRelationsImportArguments(args, out var bibleReviewedRelationsImport);
var hasBibleCorpusEvaluation = TryReadBibleCorpusEvaluationArguments(args, out var bibleCorpusEvaluation);
if ((hasBibleCorpusImport ? 1 : 0)
    + (hasBibleReviewedRelationsImport ? 1 : 0)
    + (hasBibleCorpusEvaluation ? 1 : 0) > 1)
{
    throw new InvalidOperationException("성경 본문 적재, 검토 관계 적재, 운영 평가는 한 프로세스에서 동시에 실행할 수 없습니다.");
}
if (hasBibleCorpusImport && bibleCorpusImport.VerifyOnly)
{
    await using var scope = app.Services.CreateAsyncScope();
    var orchestrator = scope.ServiceProvider.GetRequiredService<BibleCorpusImportOrchestrator>();
    var result = await orchestrator.RunAsync(bibleCorpusImport);
    WriteBibleCorpusImportResult(result);
    return;
}
if (hasBibleReviewedRelationsImport && bibleReviewedRelationsImport.VerifyOnly)
{
    await using var scope = app.Services.CreateAsyncScope();
    var orchestrator = scope.ServiceProvider.GetRequiredService<BibleReviewedRelationsImportOrchestrator>();
    var result = await orchestrator.RunAsync(bibleReviewedRelationsImport);
    WriteBibleReviewedRelationsImportResult(result);
    return;
}
if (hasBibleCorpusEvaluation)
{
    await using var scope = app.Services.CreateAsyncScope();
    var runner = scope.ServiceProvider.GetRequiredService<BibleCorpusEvaluationRunner>();
    var results = await runner.RunAsync(bibleCorpusEvaluation);
    var passed = results.Count(value => value.Passed);
    Console.WriteLine(
        $"BIBLE_CORPUS_EVALUATION={(passed == results.Count ? "PASS" : "FAIL")} passed={passed} total={results.Count} output={bibleCorpusEvaluation.OutputPath}");
    if (passed != results.Count)
    {
        Environment.ExitCode = 2;
    }
    return;
}

if (!app.Configuration.GetValue("Slogs:SkipDbInitializer", false))
{
    await SlogsDbInitializer.InitializeAsync(app.Services);
    await OrganizationDbInitializer.InitializeAsync(app.Services);
}

if (hasBibleCorpusImport)
{
    await using var scope = app.Services.CreateAsyncScope();
    var orchestrator = scope.ServiceProvider.GetRequiredService<BibleCorpusImportOrchestrator>();
    var result = await orchestrator.RunAsync(bibleCorpusImport);
    WriteBibleCorpusImportResult(result);
    return;
}
if (hasBibleReviewedRelationsImport)
{
    await using var scope = app.Services.CreateAsyncScope();
    var orchestrator = scope.ServiceProvider.GetRequiredService<BibleReviewedRelationsImportOrchestrator>();
    var result = await orchestrator.RunAsync(bibleReviewedRelationsImport);
    WriteBibleReviewedRelationsImportResult(result);
    return;
}
if (TryReadSemanticImportArguments(args, out var semanticImport))
{
    var result = await LlmWikiSemanticGraphImporter.ImportAsync(
        app.Services,
        semanticImport.ManifestPath,
        semanticImport.CorpusDirectory,
        semanticImport.Version,
        semanticImport.Activate);
    Console.WriteLine(
        $"SEMANTIC_IMPORT=PASS owner={result.OwnerUserName} version={result.Version} entities={result.EntityCount} mentions={result.MentionCount} relations={result.RelationCount} splits={result.SplitProposalCount} activated={result.Activated}");
    return;
}

if (TryReadBgeM3MigrationPhase(args, out var bgeM3MigrationPhase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var migration = scope.ServiceProvider.GetRequiredService<BgeM3ShadowIndexMigration>();
    var result = bgeM3MigrationPhase switch
    {
        "prepare" => await migration.PrepareAsync(),
        "activate" => await migration.ActivateAsync(),
        "validate" => await migration.ValidateActiveAsync(),
        "rollback" => await migration.RollbackAsync(),
        "finalize" => await migration.FinalizeAsync(),
        _ => throw new UnreachableException()
    };
    Console.WriteLine(
        $"BGE_M3_MIGRATION=PASS phase={result.Phase} personal={result.PersonalEntries} organization={result.OrganizationMemories} embedded={result.EmbeddedDocuments} model={result.Model} dimensions={result.Dimensions}");
    return;
}

app.Run();

static string GetFormValue(IFormCollection form, string name)
    => form.TryGetValue(name, out var value) ? value.ToString().Trim() : string.Empty;

static string BuildAuthRedirect(string path, string returnUrl, string error)
    => $"{path}?returnUrl={Uri.EscapeDataString(returnUrl)}&error={Uri.EscapeDataString(error)}";

static string MapRegisterErrorCode(string error)
    => error switch
    {
        "reservedUserName" => "duplicate",
        "duplicate" => "duplicate",
        "profileImageUrlLength" => "profileImageUrlLength",
        "profileImageUrlInvalid" => "profileImageUrlInvalid",
        "profileBioLength" => "profileBioLength",
        _ => "registerFailed"
    };

static bool ShouldExposeLlmsDiscoveryHeaders(HttpRequest request)
{
    if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
    {
        return false;
    }

    return !request.Path.StartsWithSegments("/api")
        && !request.Path.StartsWithSegments("/mcp")
        && !request.Path.StartsWithSegments("/auth")
        && !request.Path.StartsWithSegments("/editor");
}

static async Task<GoogleExternalLoginInfo?> ReadGoogleExternalLoginAsync(HttpContext httpContext)
{
    var authenticateResult = await httpContext.AuthenticateAsync(ExternalLoginScheme);
    if (authenticateResult.Succeeded != true || authenticateResult.Principal is null)
    {
        return null;
    }

    var providerUserId = authenticateResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(providerUserId))
    {
        return null;
    }

    return new GoogleExternalLoginInfo(
        providerUserId.Trim(),
        authenticateResult.Principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant() ?? string.Empty,
        authenticateResult.Principal.FindFirstValue(ClaimTypes.Name)?.Trim() ?? string.Empty,
        authenticateResult.Principal.FindFirstValue(GooglePictureClaim)?.Trim() ?? string.Empty);
}

static string BuildGoogleConfirmPage(
    string returnUrl,
    string candidateUserName,
    string displayName,
    string email,
    string profileImageUrl,
    string? error)
{
    var safeReturnUrl = WebUtility.HtmlEncode(returnUrl);
    var safeCandidateUserName = WebUtility.HtmlEncode(candidateUserName);
    var safeDisplayName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(displayName) ? "Google 사용자" : displayName);
    var safeEmail = WebUtility.HtmlEncode(email);
    var safeProfileImageUrl = WebUtility.HtmlEncode(profileImageUrl);
    var errorMessage = MapGoogleConfirmError(error);
    var errorHtml = string.IsNullOrWhiteSpace(errorMessage)
        ? string.Empty
        : $"""<p class="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-800">{WebUtility.HtmlEncode(errorMessage)}</p>""";
    var imageHtml = string.IsNullOrWhiteSpace(profileImageUrl)
        ? """<div class="grid h-16 w-16 place-items-center rounded-2xl border border-slate-200 bg-slate-100 text-2xl font-black text-slate-700">G</div>"""
        : $"""<img class="h-16 w-16 rounded-2xl border border-slate-200 object-cover" src="{safeProfileImageUrl}" alt="" />""";

    return $$"""
<!DOCTYPE html>
<html lang="ko">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Google 지식 로그 연결 | slogs</title>
    <link rel="stylesheet" href="/css/tailwind.css" />
    <link rel="stylesheet" href="/app.css" />
</head>
<body class="bg-slate-100">
    <main class="mx-auto flex min-h-screen w-full max-w-xl items-center px-4 py-10">
        <form class="grid w-full gap-5 rounded-2xl border border-slate-200 bg-white p-5" method="post" action="/auth/google/confirm">
            <input type="hidden" name="returnUrl" value="{{safeReturnUrl}}" />
            <div class="flex items-center gap-4">
                {{imageHtml}}
                <div>
                    <p class="text-xs font-bold text-slate-500">Google로 지식 로그 이어가기</p>
                    <h1 class="mt-1 text-2xl font-black text-slate-900">슬로거 홈 주소</h1>
                    <p class="mt-1 text-sm font-semibold text-slate-500">{{safeDisplayName}} · {{safeEmail}}</p>
                </div>
            </div>

            <p class="text-sm leading-6 text-slate-600">Google 계정에서 이어질 지식 로그 홈의 주소를 정해 주세요. 이 <strong>@id</strong>는 공개 로그, 게시전 로그, 노트 Vault 내용이 모이는 슬로거 홈에 표시됩니다.</p>

            <label class="grid gap-1 text-sm font-semibold text-slate-700" for="google-user-name">
                슬로거 홈 주소
                <span class="flex items-center rounded-2xl border border-slate-300 bg-white px-3 py-2 text-sm font-normal text-slate-900 focus-within:border-slate-900">
                    <span class="shrink-0 font-bold text-slate-500">@</span>
                    <input id="google-user-name" class="min-w-0 flex-1 border-0 bg-transparent px-1 py-0 text-sm font-semibold text-slate-900 outline-none" name="userName" maxlength="80" autocomplete="username" value="{{safeCandidateUserName}}" />
                </span>
            </label>

            {{errorHtml}}

            <div class="flex flex-wrap gap-2">
                <button class="rounded-full bg-slate-900 px-4 py-2 text-sm font-semibold text-white transition hover:bg-slate-800" type="submit" name="intent" value="confirm">홈 주소 잇기</button>
                <button class="rounded-full border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:bg-slate-100" type="submit" name="intent" value="cancel">연결 취소</button>
            </div>
        </form>
    </main>
</body>
</html>
""";
}

static string MapGoogleConfirmError(string? error)
    => error switch
    {
        "externalUserNameRequired" or "profileUserNameRequired" => "슬로거 홈 주소를 입력해 주세요.",
        "externalUserNameLength" or "profileUserNameLength" => "슬로거 홈 주소는 80자 이하여야 합니다.",
        "externalUserNameInvalid" or "profileUserNameInvalid" => "슬로거 홈 주소는 영문, 숫자, 점, 하이픈, 밑줄만 사용할 수 있고 첫 글자는 영문 또는 숫자여야 합니다.",
        "externalUserNameTaken" or "profileUserNameTaken" => "이미 사용 중인 슬로거 홈 주소입니다.",
        "externalLoginInvalid" => "Google 계정 연결 정보를 읽을 수 없습니다.",
        _ => string.Empty
    };

static bool IsGoogleAuthenticationConfigured(IConfiguration configuration)
    => !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]);

static void ConfigureDataProtection(
    IServiceCollection services,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    var keysPath = configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(keysPath))
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("DataProtection:KeysPath is required outside Development.");
        }

        keysPath = Path.Combine(environment.ContentRootPath, "App_Data", "data-protection");
    }

    if (!Path.IsPathFullyQualified(keysPath))
    {
        throw new InvalidOperationException("DataProtection:KeysPath must be an absolute path.");
    }

    Directory.CreateDirectory(keysPath);
    var dataProtection = services
        .AddDataProtection()
        .SetApplicationName("Slogs")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

    if (environment.IsDevelopment())
    {
        return;
    }

    var certificatePath = configuration["DataProtection:CertificatePath"];
    var certificatePassword = configuration["DataProtection:CertificatePassword"];
    if (string.IsNullOrWhiteSpace(certificatePath)
        || string.IsNullOrWhiteSpace(certificatePassword))
    {
        throw new InvalidOperationException(
            "DataProtection certificate path and password are required outside Development.");
    }

    if (!Path.IsPathFullyQualified(certificatePath) || !File.Exists(certificatePath))
    {
        throw new InvalidOperationException(
            "DataProtection:CertificatePath must reference an existing absolute PFX path.");
    }

    var certificate = X509CertificateLoader.LoadPkcs12FromFile(
        certificatePath,
        certificatePassword,
        X509KeyStorageFlags.EphemeralKeySet);
    dataProtection.ProtectKeysWithCertificate(certificate);
}

static string NormalizeLocalReturnUrl(string? returnUrl, string fallback)
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

static string GetRequestBaseUri(HttpRequest request)
{
    var scheme = IsHttpScheme(request.Scheme) ? request.Scheme : Uri.UriSchemeHttp;
    var host = request.Host.HasValue ? request.Host.ToUriComponent() : "localhost";
    var pathBase = request.PathBase.HasValue ? request.PathBase.ToUriComponent().TrimEnd('/') : string.Empty;
    return $"{scheme}://{host}{pathBase}/";
}

static Uri? GetConfiguredPublicBaseUri(IConfiguration configuration)
{
    var value = configuration["Slogs:PublicBaseUrl"];
    if (string.IsNullOrWhiteSpace(value))
    {
        value = Environment.GetEnvironmentVariable("Slogs__PublicBaseUrl");
    }

    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        || !IsHttpScheme(uri.Scheme)
        || string.IsNullOrWhiteSpace(uri.Host))
    {
        throw new InvalidOperationException("Slogs:PublicBaseUrl must be an absolute http or https URL.");
    }

    var builder = new UriBuilder(uri)
    {
        Query = string.Empty,
        Fragment = string.Empty,
        Path = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : $"{uri.AbsolutePath.TrimEnd('/')}/"
    };

    return builder.Uri;
}

static bool IsHttpScheme(string? scheme)
    => string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

static HostString ToHostString(Uri uri)
    => uri.IsDefaultPort ? new HostString(uri.Host) : new HostString(uri.Host, uri.Port);

static PathString ToPathBase(Uri uri)
{
    var path = uri.AbsolutePath.TrimEnd('/');
    return string.IsNullOrEmpty(path) ? PathString.Empty : PathString.FromUriComponent(path);
}

static bool TryGetBearerToken(HttpRequest request, out string token)
{
    token = string.Empty;
    var authorization = request.Headers.Authorization.ToString();
    const string bearerPrefix = "Bearer ";
    if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
    {
        token = authorization[bearerPrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(token);
    }

    return false;
}

static bool TryGetRequiredBearerScope(PathString path, out string requiredScope)
{
    if (path.StartsWithSegments("/mcp"))
    {
        requiredScope = SlogsTokenScopes.Mcp;
        return true;
    }

    if (path.StartsWithSegments("/api/obsidian"))
    {
        requiredScope = SlogsTokenScopes.ObsidianSync;
        return true;
    }

    requiredScope = string.Empty;
    return false;
}

static bool TryReadSemanticImportArguments(string[] arguments, out SemanticImportArguments result)
{
    result = default!;
    var importIndex = Array.IndexOf(arguments, "--llm-wiki-semantic-import");
    if (importIndex < 0)
    {
        return false;
    }

    static string RequiredValue(string[] values, string name)
    {
        var index = Array.IndexOf(values, name);
        if (index < 0 || index + 1 >= values.Length || values[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name} requires a value.");
        }
        return values[index + 1];
    }

    if (importIndex + 1 >= arguments.Length || arguments[importIndex + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("--llm-wiki-semantic-import requires a manifest path.");
    }
    result = new(
        arguments[importIndex + 1],
        RequiredValue(arguments, "--semantic-corpus"),
        RequiredValue(arguments, "--semantic-version"),
        arguments.Contains("--activate-semantic-graph", StringComparer.Ordinal));
    return true;
}

static bool TryReadBgeM3MigrationPhase(string[] arguments, out string phase)
{
    phase = string.Empty;
    var index = Array.IndexOf(arguments, "--bge-m3-migration");
    if (index < 0)
    {
        return false;
    }
    if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "--bge-m3-migration requires one of: prepare, activate, validate, rollback, finalize.");
    }
    phase = arguments[index + 1].ToLowerInvariant();
    if (phase is not ("prepare" or "activate" or "validate" or "rollback" or "finalize"))
    {
        throw new InvalidOperationException(
            $"Unsupported BGE-M3 migration phase '{phase}'. Expected prepare, activate, validate, rollback, or finalize.");
    }
    return true;
}

static bool TryReadBibleCorpusImportArguments(string[] arguments, out BibleCorpusImportOptions result)
{
    result = default!;
    var importIndex = Array.IndexOf(arguments, "--bible-corpus-import");
    if (importIndex < 0)
    {
        return false;
    }

    static string RequiredValue(string[] values, string name)
    {
        var index = Array.IndexOf(values, name);
        if (index < 0 || index + 1 >= values.Length || values[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name} requires a value.");
        }
        return values[index + 1];
    }

    static string OptionalValue(string[] values, string name, string fallback)
    {
        var index = Array.IndexOf(values, name);
        return index < 0 ? fallback : RequiredValue(values, name);
    }

    if (importIndex + 1 >= arguments.Length || arguments[importIndex + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("--bible-corpus-import requires a package directory.");
    }

    result = new BibleCorpusImportOptions(
        arguments[importIndex + 1],
        RequiredValue(arguments, "--bible-import-checkpoints"),
        RequiredValue(arguments, "--bible-owner"),
        OptionalValue(arguments, "--bible-import-layer", "all"),
        arguments.Contains("--bible-verify-only", StringComparer.Ordinal));
    return true;
}

static void WriteBibleCorpusImportResult(BibleCorpusImportSummary result)
{
    foreach (var layer in result.Layers)
    {
        Console.WriteLine(
            $"BIBLE_CORPUS_LAYER=PASS collection={layer.CollectionId} version={layer.Version} visibility={layer.Visibility} batches={layer.Batches} documents={layer.Documents} chunks={layer.Chunks} entities={layer.Entities} relations={layer.Relations} plan={layer.PlanHash} state={layer.State}");
    }
    Console.WriteLine(
        $"BIBLE_CORPUS_IMPORT=PASS package={result.PackageId} version={result.PackageVersion} hash={result.PackageHash} verifyOnly={result.VerifyOnly.ToString().ToLowerInvariant()} layers={result.Layers.Count}");
}

static bool TryReadBibleReviewedRelationsImportArguments(
    string[] arguments,
    out BibleReviewedRelationsImportOptions result)
{
    result = default!;
    var importIndex = Array.IndexOf(arguments, "--bible-reviewed-relations-import");
    if (importIndex < 0)
    {
        return false;
    }
    static string RequiredValue(string[] values, string name)
    {
        var index = Array.IndexOf(values, name);
        if (index < 0 || index + 1 >= values.Length || values[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name} requires a value.");
        }
        return values[index + 1];
    }
    if (importIndex + 1 >= arguments.Length || arguments[importIndex + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("--bible-reviewed-relations-import requires a package directory.");
    }
    result = new BibleReviewedRelationsImportOptions(
        arguments[importIndex + 1],
        RequiredValue(arguments, "--bible-review-checkpoints"),
        RequiredValue(arguments, "--bible-owner"),
        arguments.Contains("--bible-review-verify-only", StringComparer.Ordinal));
    return true;
}

static bool TryReadBibleCorpusEvaluationArguments(
    string[] arguments,
    out BibleCorpusEvaluationOptions result)
{
    result = default!;
    var evaluationIndex = Array.IndexOf(arguments, "--bible-corpus-evaluate");
    if (evaluationIndex < 0)
    {
        return false;
    }

    static string RequiredValue(string[] values, string name)
    {
        var index = Array.IndexOf(values, name);
        if (index < 0 || index + 1 >= values.Length || values[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name} requires a value.");
        }
        return values[index + 1];
    }

    static int OptionalInt(string[] values, string name, int fallback)
    {
        var index = Array.IndexOf(values, name);
        if (index < 0)
        {
            return fallback;
        }
        var raw = RequiredValue(values, name);
        return int.TryParse(raw, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{name} requires an integer value.");
    }

    if (evaluationIndex + 1 >= arguments.Length || arguments[evaluationIndex + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("--bible-corpus-evaluate requires an evaluation JSON path.");
    }

    result = new BibleCorpusEvaluationOptions(
        arguments[evaluationIndex + 1],
        RequiredValue(arguments, "--bible-evaluation-output"),
        RequiredValue(arguments, "--bible-owner"),
        OptionalInt(arguments, "--bible-evaluation-limit", 10),
        OptionalInt(arguments, "--bible-evaluation-hops", 3));
    return true;
}

static void WriteBibleReviewedRelationsImportResult(BibleReviewedRelationsImportSummary result)
{
    var layer = result.Layer;
    Console.WriteLine(
        $"BIBLE_REVIEWED_RELATIONS_LAYER=PASS collection={layer.CollectionId} version={layer.Version} visibility={layer.Visibility} batches={layer.Batches} documents={layer.Documents} chunks={layer.Chunks} entities={layer.Entities} relations={layer.Relations} plan={layer.PlanHash} state={layer.State}");
    Console.WriteLine(
        $"BIBLE_REVIEWED_RELATIONS_IMPORT=PASS package={result.PackageId} version={result.PackageVersion} hash={result.PackageHash} verifyOnly={result.VerifyOnly.ToString().ToLowerInvariant()}");
}

public sealed record GoogleExternalLoginInfo(string ProviderUserId, string Email, string DisplayName, string ProfileImageUrl);
public sealed record SemanticImportArguments(string ManifestPath, string CorpusDirectory, string Version, bool Activate);
