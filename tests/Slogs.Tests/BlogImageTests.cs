using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BlogImageTests
{
    [Fact]
    public async Task CreatePostTracksRepresentativeUploadImageOutsideMarkdownBody()
    {
        await using var fixture = await BlogImageFixture.CreateAsync();
        const string thumbnailUrl = "/uploads/cover.png";

        var post = await fixture.Blog.CreatePostAsync(
            "Post with cover",
            "alice",
            "Summary",
            "Body without Markdown images.",
            "guide",
            null,
            thumbnailUrl,
            isDraft: true,
            slug: "post-with-cover");

        Assert.Equal(thumbnailUrl, post.ThumbnailUrl);
        Assert.Equal(1, await fixture.CountPostImagesAsync(post.Id, thumbnailUrl));
    }

    private sealed class BlogImageFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider services;

        private BlogImageFixture(SqliteConnection connection, ServiceProvider services)
        {
            this.connection = connection;
            this.services = services;
            Blog = services.GetRequiredService<BlogService>();
        }

        public BlogService Blog { get; }

        public static async Task<BlogImageFixture> CreateAsync()
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

            return new BlogImageFixture(connection, provider);
        }

        public async Task<int> CountPostImagesAsync(Guid postId, string url)
        {
            await using var db = await services.GetRequiredService<IDbContextFactory<SlogsDbContext>>().CreateDbContextAsync();
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM "PostImages"
                WHERE "PostId" = @postId
                    AND "Url" = @url;
                """;

            var postIdParameter = command.CreateParameter();
            postIdParameter.ParameterName = "postId";
            postIdParameter.Value = postId;
            command.Parameters.Add(postIdParameter);

            var urlParameter = command.CreateParameter();
            urlParameter.ParameterName = "url";
            urlParameter.Value = url;
            command.Parameters.Add(urlParameter);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
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
