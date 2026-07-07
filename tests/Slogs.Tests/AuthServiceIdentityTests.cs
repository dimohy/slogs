using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class AuthServiceIdentityTests
{
    [Fact]
    public async Task ConfirmedExternalLoginStartsWithKnowledgeLogHomeBio()
    {
        await using var fixture = await AuthServiceFixture.CreateAsync();

        var user = await fixture.Auth.CreateConfirmedExternalLoginAsync(
            "google",
            "google-user-1",
            "googleflow@example.com",
            "Google Flow",
            profileImageUrl: string.Empty,
            requestedUserName: "googleflow");

        Assert.Equal("Google로 이어진 지식 로그 홈입니다.", user.Bio);

        await using var db = await fixture.CreateDbContextAsync();
        var storedUser = await db.Users.SingleAsync(x => x.UserName == "googleflow");
        Assert.Equal("Google로 이어진 지식 로그 홈입니다.", storedUser.Bio);
        Assert.DoesNotContain("계정으로 가입한 슬로거", storedUser.Bio);
    }

    private sealed class AuthServiceFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider services;

        private AuthServiceFixture(SqliteConnection connection, ServiceProvider services)
        {
            this.connection = connection;
            this.services = services;
            Auth = services.GetRequiredService<AuthService>();
        }

        public AuthService Auth { get; }

        public static async Task<AuthServiceFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContextFactory<SlogsDbContext>(options => options.UseSqlite(connection));
            services.AddHttpContextAccessor();
            services.AddSingleton<IWebHostEnvironment>(_ => new TestWebHostEnvironment());
            services.AddScoped<ObsidianStorageQuotaService>();
            services.AddScoped<AuthService>();

            var provider = services.BuildServiceProvider();
            await using var db = await provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();

            return new AuthServiceFixture(connection, provider);
        }

        public Task<SlogsDbContext> CreateDbContextAsync()
            => services.GetRequiredService<IDbContextFactory<SlogsDbContext>>().CreateDbContextAsync();

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await connection.DisposeAsync();
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
