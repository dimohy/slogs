using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Slogs.Components;
using Slogs.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "slogs.auth";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/login";
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
var connectionString = builder.Configuration.GetConnectionString("SlogsDatabase")
    ?? "Host=localhost;Port=54329;Database=slogs;Username=slogs;Password=slogs_dev_password";
builder.Services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<BlogService>();
builder.Services.AddScoped<AuthService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

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
        return Results.BadRequest(new { error = "이미지 파일을 찾을 수 없습니다." });
    }

    var form = await httpContext.Request.ReadFormAsync();
    var file = form.Files.GetFile("image");
    if (file is null)
    {
        return Results.BadRequest(new { error = "이미지 파일을 찾을 수 없습니다." });
    }

    var extension = GetSafeImageExtension(file.FileName, file.ContentType);
    if (extension is null)
    {
        return Results.BadRequest(new { error = "PNG, JPG, GIF, WebP 이미지만 업로드할 수 있습니다." });
    }

    if (file.Length <= 0 || file.Length > maxImageBytes)
    {
        return Results.BadRequest(new { error = "이미지는 5MB 이하만 업로드할 수 있습니다." });
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

    return Results.Ok(new
    {
        url = $"/uploads/{fileName}",
        altText = string.IsNullOrWhiteSpace(baseName) ? "image" : baseName
    });
}).DisableAntiforgery();

app.MapGet("/robots.txt", (HttpContext httpContext) =>
{
    var baseUri = SeoMetadata.RequestBaseUri(httpContext.Request);
    return Results.Text(SeoMetadata.BuildRobotsTxt(baseUri), "text/plain; charset=utf-8");
});

app.MapGet("/sitemap.xml", async (HttpContext httpContext, BlogService blogService, AuthService authService) =>
{
    var baseUri = SeoMetadata.RequestBaseUri(httpContext.Request);
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
    .AddInteractiveServerRenderMode();

await SlogsDbInitializer.InitializeAsync(app.Services);

app.Run();

static string GetFormValue(IFormCollection form, string name)
    => form.TryGetValue(name, out var value) ? value.ToString().Trim() : string.Empty;

static string BuildAuthRedirect(string path, string returnUrl, string error)
    => $"{path}?returnUrl={Uri.EscapeDataString(returnUrl)}&error={Uri.EscapeDataString(error)}";

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
