using Microsoft.EntityFrameworkCore;
using Slogs.Components;
using Slogs.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddHttpContextAccessor();
var connectionString = builder.Configuration.GetConnectionString("SlogsDatabase")
    ?? "Host=localhost;Port=54329;Database=slogs;Username=slogs;Password=slogs_dev_password";
builder.Services.AddDbContextFactory<SlogsDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<BlogService>();
builder.Services.AddScoped<AuthService>();

var app = builder.Build();

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
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

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
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();

await SlogsDbInitializer.InitializeAsync(app.Services);

app.Run();
