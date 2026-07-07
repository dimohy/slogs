using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class SeedIdentityTests
{
    [Fact]
    public async Task FirstRunSeedLogsUseKnowledgeLogIdentityWording()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        await InvokeInitializerAsync("SeedUsersAsync", fixture.Db);
        await InvokeInitializerAsync("SeedPostsAsync", fixture.Db);
        await InvokeInitializerAsync("EnsureSeedIdentityDefaultsAsync", fixture.Db);
        await InvokeInitializerAsync("EnsurePostRevisionBaselinesAsync", fixture.Db);

        var posts = await fixture.Db.Posts
            .Include(x => x.Comments)
            .Include(x => x.Revisions)
            .OrderBy(x => x.PublishedAt)
            .ToListAsync();

        Assert.Equal(3, posts.Count);
        Assert.Contains(posts, post => post.Slug == "blazor-markdown-knowledge-log" && post.Title.Contains("지식 로그"));
        Assert.Contains(posts, post => post.Slug == "modern-csharp-component-patterns" && post.Title.Contains("작업 판단 로그"));
        Assert.Contains(posts, post => post.Slug == "recall-ux-in-slogs" && post.Title.Contains("회상 UX"));

        var visibleSeedText = string.Join(
            "\n",
            posts.SelectMany(post => new[]
                {
                    post.Title,
                    post.Summary,
                    post.Body,
                    post.TagsJson,
                    post.SeriesJson
                }
                .Concat(post.Comments.Select(comment => comment.Content))
                .Concat(post.Revisions.SelectMany(revision => new[]
                {
                    revision.Title,
                    revision.Summary,
                    revision.Body,
                    revision.TagsJson,
                    revision.SeriesJson
                }))));

        Assert.Contains("지식 로그", visibleSeedText);
        Assert.Contains("작업 판단 로그", visibleSeedText);
        Assert.Contains("검증 흔적", visibleSeedText);
        Assert.Contains("회상 UX", visibleSeedText);
        Assert.Contains("대화 흔적", visibleSeedText);
        Assert.DoesNotContain("블로그", visibleSeedText);
        Assert.DoesNotContain("포스트", visibleSeedText);
        Assert.DoesNotContain("댓글", visibleSeedText);
        Assert.DoesNotContain("검색 UX", visibleSeedText);
    }

    [Fact]
    public async Task SeedIdentityRepairUpdatesLegacyCsharpSampleLog()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        await InvokeInitializerAsync("SeedUsersAsync", fixture.Db);

        var now = DateTime.UtcNow;
        var legacyPost = new PostRecord
        {
            Title = "C# 14의 최신 패턴으로 컴포넌트 정리하기",
            Author = "junho",
            Summary = "최신 C# 문법을 이용해 서비스와 라우팅 코드를 간결하게 유지하는 기법을 정리합니다.",
            Body = "# 최신 C#로 정리\n\n초기화 구문, 패턴 매칭, 컬렉션 표기법을 활용해 코드량을 줄이고 가독성을 높일 수 있습니다.",
            ThumbnailUrl = string.Empty,
            PublishedAt = now.AddDays(-2),
            UpdatedAt = now,
            Slug = "modern-csharp-component-patterns",
            TagsJson = "[\"csharp\",\"programming\",\"architecture\"]",
            SeriesJson = "[\"아키텍처 노트\"]",
            ReadTimeMinutes = 9
        };
        legacyPost.Revisions.Add(new PostRevisionRecord
        {
            RevisionNumber = 1,
            Title = legacyPost.Title,
            Summary = legacyPost.Summary,
            Body = legacyPost.Body,
            ThumbnailUrl = legacyPost.ThumbnailUrl,
            TagsJson = legacyPost.TagsJson,
            SeriesJson = legacyPost.SeriesJson,
            CreatedAt = legacyPost.PublishedAt,
            Author = legacyPost.Author
        });
        fixture.Db.Posts.Add(legacyPost);
        await fixture.Db.SaveChangesAsync();

        await InvokeInitializerAsync("EnsureSeedIdentityDefaultsAsync", fixture.Db);

        var repairedPost = await fixture.Db.Posts
            .Include(x => x.Revisions)
            .SingleAsync(x => x.Slug == "modern-csharp-component-patterns");
        var repairedText = string.Join(
            "\n",
            new[]
            {
                repairedPost.Title,
                repairedPost.Summary,
                repairedPost.Body
            }
            .Concat(repairedPost.Revisions.SelectMany(revision => new[]
            {
                revision.Title,
                revision.Summary,
                revision.Body
            })));

        Assert.Contains("작업 판단 로그", repairedText);
        Assert.Contains("검증 흔적", repairedText);
        Assert.Contains("리비전 단서", repairedText);
        Assert.DoesNotContain("컴포넌트 정리하기", repairedText);
        Assert.DoesNotContain("간결하게 유지하는 기법", repairedText);
        Assert.DoesNotContain("# 최신 C#로 정리", repairedText);
    }

    private static async Task InvokeInitializerAsync(string methodName, SlogsDbContext db)
    {
        var method = typeof(SlogsDbInitializer).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(SlogsDbInitializer), methodName);

        if (method.Invoke(null, [db]) is not Task task)
        {
            throw new InvalidOperationException($"{methodName} did not return a Task.");
        }

        await task;
    }

    private sealed class SeedFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private SeedFixture(SqliteConnection connection, SlogsDbContext db)
        {
            this.connection = connection;
            Db = db;
        }

        public SlogsDbContext Db { get; }

        public static async Task<SeedFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<SlogsDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new SlogsDbContext(options);
            await db.Database.EnsureCreatedAsync();

            return new SeedFixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
