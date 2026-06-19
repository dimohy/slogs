using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Slogs.Components;
using Slogs.Data;
using System.Net;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
const string GooglePictureClaim = "urn:google:picture";
const string ExternalLoginScheme = "slogs.external";

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
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
builder.Services.AddScoped<BlogService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<LlmWikiService>();
builder.Services.AddScoped<ISlogsApiBackend, ServerSlogsApiBackend>();
builder.Services.AddScoped<SlogsAuthState>();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<LlmWikiMcpTools>()
    .WithTools<SlogsPostMcpTools>();
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

var app = builder.Build();

app.UseForwardedHeaders();

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
    if (httpContext.Request.Path.StartsWithSegments("/mcp")
        && httpContext.User.Identity?.IsAuthenticated != true
        && TryGetBearerToken(httpContext.Request, out var bearerToken))
    {
        var llmWikiService = httpContext.RequestServices.GetRequiredService<LlmWikiService>();
        var tokenUser = await llmWikiService.AuthenticateMcpTokenAsync(bearerToken, httpContext.RequestAborted);
        if (tokenUser is not null)
        {
            httpContext.User = SlogsAuthentication.CreatePrincipal(tokenUser);
        }
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

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        SlogsAuthentication.CreatePrincipal(user));

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
        var user = await authService.RegisterAsync(userName, displayName, password);
        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SlogsAuthentication.CreatePrincipal(user));

        return Results.Redirect(returnUrl);
    }
    catch (InvalidOperationException)
    {
        return Results.Redirect(BuildAuthRedirect("/register", returnUrl, "duplicate"));
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
        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SlogsAuthentication.CreatePrincipal(existingUser));
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
        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SlogsAuthentication.CreatePrincipal(user));
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

app.MapPost("/editor/images", async (HttpContext httpContext, IWebHostEnvironment environment) =>
{
    const long maxImageBytes = 5 * 1024 * 1024;

    if (httpContext.User.Identity?.IsAuthenticated != true)
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

    var extension = GetSafeImageExtension(file.FileName, file.ContentType);
    if (extension is null)
    {
        return Results.BadRequest(new ApiErrorResponse("PNG, JPG, GIF, WebP 이미지만 업로드할 수 있습니다."));
    }

    if (file.Length <= 0 || file.Length > maxImageBytes)
    {
        return Results.BadRequest(new ApiErrorResponse("이미지는 5MB 이하만 업로드할 수 있습니다."));
    }

    var webRoot = environment.WebRootPath;
    if (string.IsNullOrWhiteSpace(webRoot))
    {
        webRoot = Path.Combine(environment.ContentRootPath, "wwwroot");
    }

    var uploadRoot = Path.Combine(webRoot, "uploads");
    Directory.CreateDirectory(uploadRoot);

    var baseName = SanitizeFileBaseName(Path.GetFileNameWithoutExtension(file.FileName));
    var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{baseName}-{Guid.NewGuid():N}{extension}";
    var targetPath = Path.Combine(uploadRoot, fileName);

    await using (var source = file.OpenReadStream())
    await using (var target = File.Create(targetPath))
    {
        await source.CopyToAsync(target);
    }

    return Results.Ok(new EditorImageResponse(
        $"/uploads/{fileName}",
        string.IsNullOrWhiteSpace(baseName) ? "image" : baseName));
}).DisableAntiforgery();

app.MapGet("/robots.txt", (HttpContext httpContext) =>
{
    var baseUri = GetRequestBaseUri(httpContext.Request);
    return Results.Text(SeoMetadata.BuildRobotsTxt(baseUri), "text/plain; charset=utf-8");
});

app.MapGet("/sitemap.xml", async (HttpContext httpContext, BlogService blogService, AuthService authService) =>
{
    var baseUri = GetRequestBaseUri(httpContext.Request);
    var posts = await blogService.GetLatestAsync(500);
    var tags = await blogService.GetTagCloudAsync(200);
    var series = await blogService.GetSeriesCloudAsync(200);
    var authors = await blogService.GetAuthorCloudAsync(200);
    var knownUsers = authService.GetUserNames();

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
    entries.AddRange(authors.Select(author => author.Author).Concat(knownUsers).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(author => new SitemapEntry(
            $"/@{Uri.EscapeDataString(author)}",
            DateTime.UtcNow,
            "weekly",
            0.8m)));

    return Results.Text(SeoMetadata.BuildSitemapXml(baseUri, entries), "application/xml; charset=utf-8");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Slogs.Components.Routes).Assembly);

if (!app.Configuration.GetValue("Slogs:SkipDbInitializer", false))
{
    await SlogsDbInitializer.InitializeAsync(app.Services);
}

app.Run();

static string GetFormValue(IFormCollection form, string name)
    => form.TryGetValue(name, out var value) ? value.ToString().Trim() : string.Empty;

static string BuildAuthRedirect(string path, string returnUrl, string error)
    => $"{path}?returnUrl={Uri.EscapeDataString(returnUrl)}&error={Uri.EscapeDataString(error)}";

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
    <title>Google 계정 연결 확인 | slogs</title>
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
                    <p class="text-xs font-bold uppercase text-slate-500">Google 계정 연결</p>
                    <h1 class="mt-1 text-2xl font-black text-slate-900">공개 주소 확인</h1>
                    <p class="mt-1 text-sm font-semibold text-slate-500">{{safeDisplayName}} · {{safeEmail}}</p>
                </div>
            </div>

            <p class="text-sm leading-6 text-slate-600">Slogs에서 사용할 공개 주소를 확인해 주세요. 이 주소는 글과 프로필에 <strong>@id</strong> 형태로 표시되며, 계정 생성 후에는 설정에서 변경할 수 없습니다.</p>

            <label class="grid gap-1 text-sm font-semibold text-slate-700" for="google-user-name">
                공개 주소
                <span class="flex items-center rounded-2xl border border-slate-300 bg-white px-3 py-2 text-sm font-normal text-slate-900 focus-within:border-slate-900">
                    <span class="shrink-0 font-bold text-slate-500">@</span>
                    <input id="google-user-name" class="min-w-0 flex-1 border-0 bg-transparent px-1 py-0 text-sm font-semibold text-slate-900 outline-none" name="userName" maxlength="80" autocomplete="username" value="{{safeCandidateUserName}}" />
                </span>
            </label>

            {{errorHtml}}

            <div class="flex flex-wrap gap-2">
                <button class="rounded-full bg-slate-900 px-4 py-2 text-sm font-semibold text-white transition hover:bg-slate-800" type="submit" name="intent" value="confirm">확인</button>
                <button class="rounded-full border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:bg-slate-100" type="submit" name="intent" value="cancel">취소</button>
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
        "externalUserNameRequired" or "profileUserNameRequired" => "공개 주소를 입력해 주세요.",
        "externalUserNameLength" or "profileUserNameLength" => "공개 주소는 80자 이하여야 합니다.",
        "externalUserNameInvalid" or "profileUserNameInvalid" => "공개 주소는 영문, 숫자, 점, 하이픈, 밑줄만 사용할 수 있고 첫 글자는 영문 또는 숫자여야 합니다.",
        "externalUserNameTaken" or "profileUserNameTaken" => "이미 사용 중인 공개 주소입니다.",
        "externalLoginInvalid" => "Google 계정 정보를 확인할 수 없습니다.",
        _ => string.Empty
    };

static bool IsGoogleAuthenticationConfigured(IConfiguration configuration)
    => !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]);

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
    var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(scheme))
    {
        scheme = request.Scheme;
    }

    var host = request.Headers["X-Forwarded-Host"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(host))
    {
        host = request.Host.Value;
    }

    var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
    return $"{scheme}://{host}{pathBase}/";
}

static string? GetSafeImageExtension(string fileName, string? contentType)
{
    var extension = Path.GetExtension(fileName).ToLowerInvariant();
    return contentType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => extension == ".jpeg" ? ".jpeg" : ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        _ => extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" ? extension : null
    };
}

static string SanitizeFileBaseName(string value)
{
    var source = string.IsNullOrWhiteSpace(value) ? "image" : value.Trim();
    Span<char> buffer = stackalloc char[Math.Min(source.Length, 32)];
    var length = 0;

    foreach (var character in source)
    {
        if (length >= buffer.Length)
        {
            break;
        }

        if (char.IsAsciiLetterOrDigit(character))
        {
            buffer[length++] = char.ToLowerInvariant(character);
        }
        else if (character is '-' or '_')
        {
            buffer[length++] = character;
        }
    }

    return length == 0 ? "image" : new string(buffer[..length]);
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

public sealed record GoogleExternalLoginInfo(string ProviderUserId, string Email, string DisplayName, string ProfileImageUrl);
