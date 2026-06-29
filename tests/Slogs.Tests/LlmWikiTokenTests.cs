using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class LlmWikiTokenTests
{
    [Fact]
    public async Task GetTokensExcludesRevokedTokens()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var active = await fixture.LlmWiki.CreateTokenAsync("alice", "Active token", [SlogsTokenScopes.Mcp]);
        var revoked = await fixture.LlmWiki.CreateTokenAsync("alice", "Revoked token", [SlogsTokenScopes.Mcp]);

        Assert.Equal(2, (await fixture.LlmWiki.GetTokensAsync("alice")).Count);

        Assert.True(await fixture.LlmWiki.RevokeTokenAsync("alice", revoked.Id));
        var tokens = await fixture.LlmWiki.GetTokensAsync("alice");
        var token = Assert.Single(tokens);

        Assert.Equal(active.Id, token.Id);
        Assert.False(token.IsRevoked);
        Assert.DoesNotContain(tokens, x => x.Id == revoked.Id);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider services;

        private TestFixture(SqliteConnection connection, ServiceProvider services)
        {
            this.connection = connection;
            this.services = services;
            LlmWiki = services.GetRequiredService<LlmWikiService>();
        }

        public LlmWikiService LlmWiki { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContextFactory<SlogsDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton<EmbeddingGemmaService>(provider => new EmbeddingGemmaService(
                new HttpClient(),
                provider.GetRequiredService<IConfiguration>()));
            services.AddScoped<LlmWikiService>();
            var provider = services.BuildServiceProvider();

            await using var db = await provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserRecord
            {
                UserName = "alice",
                DisplayName = "Alice",
                Password = "pw"
            });
            await db.SaveChangesAsync();

            return new TestFixture(connection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
            await services.DisposeAsync();
        }
    }
}
