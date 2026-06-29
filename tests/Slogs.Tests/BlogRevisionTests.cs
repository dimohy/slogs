using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BlogRevisionTests
{
    [Fact]
    public async Task RevisionListReturnsSummariesAndDetailReturnsDiff()
    {
        await using var fixture = await BlogRevisionFixture.CreateAsync();
        var post = await fixture.Blog.CreatePostAsync(
            "Revision title",
            "alice",
            "Initial summary",
            "line one\nline two",
            "guide",
            null,
            slug: "revision-test");

        await fixture.Blog.UpdatePostAsync(
            post.Slug,
            "alice",
            "Revision title",
            "Updated summary",
            "line one\nline three",
            "guide,drive",
            null);

        var revisions = await fixture.Blog.GetPostRevisionsAsync(post.Slug, "alice");
        Assert.Equal(2, revisions.Count);
        Assert.All(revisions, revision => Assert.IsType<PostRevisionSummaryResponse>(revision));
        Assert.Contains("초기 게시", revisions[0].ChangedFields);
        Assert.Contains("요약", revisions[1].ChangedFields);
        Assert.Contains("본문", revisions[1].ChangedFields);
        Assert.Contains("태그", revisions[1].ChangedFields);

        var detail = await fixture.Blog.GetPostRevisionAsync(post.Slug, 2, "alice");
        Assert.NotNull(detail);
        Assert.Equal("line one\nline three", detail.Body);

        var bodyDiff = Assert.Single(detail.Diffs, x => x.Field == "body");
        Assert.Contains(bodyDiff.Lines, line => line.Kind == "removed" && line.Text == "line two");
        Assert.Contains(bodyDiff.Lines, line => line.Kind == "added" && line.Text == "line three");
    }

    private sealed class BlogRevisionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider services;

        private BlogRevisionFixture(SqliteConnection connection, ServiceProvider services)
        {
            this.connection = connection;
            this.services = services;
            Blog = services.GetRequiredService<BlogService>();
        }

        public BlogService Blog { get; }

        public static async Task<BlogRevisionFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddSingleton<IWebHostEnvironment>(_ => new TestWebHostEnvironment());
            services.AddDbContextFactory<SlogsDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<EditorImageStorage>();
            services.AddScoped<PostImageService>();
            services.AddScoped<BlogService>();
            var provider = services.BuildServiceProvider();

            await using var db = await provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            await EnsurePostImagesTableAsync(db);

            return new BlogRevisionFixture(connection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static async Task EnsurePostImagesTableAsync(SlogsDbContext db)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "PostImages" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "OwnerUserName" TEXT NOT NULL,
                    "PostId" TEXT NULL,
                    "Url" TEXT NOT NULL,
                    "FileName" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "LastReferencedAt" TEXT NULL
                );
                """);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Slogs.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public string EnvironmentName { get; set; } = Environments.Development;

        public string WebRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
