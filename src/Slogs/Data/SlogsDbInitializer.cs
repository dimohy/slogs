using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public static class SlogsDbInitializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await db.Database.EnsureCreatedAsync();
        await EnsureSchemaAsync(db);
        await SeedUsersAsync(db);
        await EnsureUserProfileDefaultsAsync(db);
        await SeedPostsAsync(db);
        await EnsurePostThumbnailDefaultsAsync(db);
    }

    private static async Task EnsureSchemaAsync(SlogsDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Posts\" ADD COLUMN IF NOT EXISTS \"ThumbnailUrl\" character varying(500) NOT NULL DEFAULT '';");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"ProfileImageUrl\" character varying(500) NOT NULL DEFAULT '';");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"Bio\" character varying(280) NOT NULL DEFAULT '';");
    }

    private static async Task SeedUsersAsync(SlogsDbContext db)
    {
        if (await db.Users.AnyAsync())
        {
            return;
        }

        var users = new[]
        {
            ("admin", "관리자", "admin"),
            ("guest", "손님", "guest"),
            ("devin", "devin", "devin"),
            ("junho", "junho", "junho"),
            ("mina", "mina", "mina"),
            ("alex", "alex", "alex"),
            ("jane", "jane", "jane"),
            ("kevin", "kevin", "kevin"),
            ("rose", "rose", "rose"),
            ("nate", "nate", "nate"),
            ("lee", "lee", "lee"),
            ("sora", "sora", "sora"),
            ("hyun", "hyun", "hyun")
        };

        foreach (var (userName, displayName, password) in users)
        {
            db.Users.Add(new UserRecord
            {
                UserName = userName,
                DisplayName = displayName,
                Password = password,
                ProfileImageUrl = string.Empty,
                Bio = GetDefaultBio(userName),
                RegisteredAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureUserProfileDefaultsAsync(SlogsDbContext db)
    {
        var users = await db.Users.ToListAsync();
        var changed = false;

        foreach (var user in users)
        {
            if (IsLegacyDefaultProfileImageUrl(user.ProfileImageUrl))
            {
                user.ProfileImageUrl = string.Empty;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(user.Bio))
            {
                user.Bio = GetDefaultBio(user.UserName);
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedPostsAsync(SlogsDbContext db)
    {
        if (await db.Posts.AnyAsync())
        {
            return;
        }

        var now = DateTime.UtcNow;
        var firstPost = new PostRecord
        {
            Title = "Blazor로 만드는 Markdown 블로그 구조",
            Author = "devin",
            Summary = "서버 렌더링과 인터랙티브 기능을 결합한 Blazor 앱에서 글 목록/상세/태그/작성자 필터를 구성하는 방법입니다.",
            Body = "# Blazor 서버 앱 클론\n\n프로젝트를 시작하면 먼저 라우팅부터 잡고, 데이터 모델을 설계한 뒤 화면별 컴포넌트를 배치하면 됩니다.",
            ThumbnailUrl = GetDefaultThumbnailUrl("blazor-markdown-blog"),
            PublishedAt = now.AddDays(-4),
            UpdatedAt = now,
            Slug = "blazor-markdown-blog",
            TagsJson = ToJson(["blazor", "dotnet", "csharp"]),
            SeriesJson = ToJson(["블로그 시리즈"]),
            LikedByJson = ToJson(["admin"]),
            ReadTimeMinutes = 6
        };

        firstPost.Comments.AddRange([
            CreateComment("guest", "좋은 포스트네요. 라우팅 설계가 가장 먼저라고 동의합니다.", now.AddDays(-3).AddHours(-10)),
            CreateComment("mina", "샘플 데이터로 동작을 확인하기 좋은 예시입니다.", now.AddDays(-3).AddHours(-8)),
            CreateComment("junho", "컴포넌트 분리는 서비스 레이어 먼저 뽑는 게 맞아요.", now.AddDays(-3).AddHours(-6)),
            CreateComment("devin", "실시간 상호작용까지 고려하면 체감이 더 좋아집니다.", now.AddDays(-3).AddHours(-4)),
            CreateComment("alex", "글 제목이 잘 보이도록 헤더 고정도 좋은 패턴 같아요.", now.AddDays(-3).AddHours(-2)),
            CreateComment("jane", "댓글 페이지네이션이 필요한 구간이 생길 것 같아요.", now.AddDays(-2).AddHours(-10)),
            CreateComment("kevin", "태그 라우팅 동작은 실제 서비스에서 중요합니다.", now.AddDays(-2).AddHours(-8)),
            CreateComment("rose", "좋은 정렬 기준을 같이 고민하면 유저 피드백이 더 좋아져요.", now.AddDays(-2).AddHours(-6)),
            CreateComment("nate", "문서 정리 방식이 깔끔해서 이해가 빠르네요.", now.AddDays(-2).AddHours(-4)),
            CreateComment("lee", "댓글의 답글 기능도 넣으면 더 풍부해질 듯합니다.", now.AddDays(-2).AddHours(-2)),
            CreateComment("sora", "실전에서 캐시 전략만 보완하면 충분히 배포 가능한 수준입니다.", now.AddDays(-1).AddHours(-10)),
            CreateComment("hyun", "좋은 글 감사합니다. 바로 따라 해보겠습니다.", now.AddDays(-1).AddHours(-8))
        ]);

        db.Posts.AddRange(
            firstPost,
            new PostRecord
            {
                Title = "C# 14의 최신 패턴으로 컴포넌트 정리하기",
                Author = "junho",
                Summary = "최신 C# 문법을 이용해 서비스와 라우팅 코드를 간결하게 유지하는 기법을 정리합니다.",
                Body = "# 최신 C#로 정리\n\n초기화 구문, 패턴 매칭, 컬렉션 표기법을 활용해 코드량을 줄이고 가독성을 높일 수 있습니다.",
                ThumbnailUrl = GetDefaultThumbnailUrl("modern-csharp-component-patterns"),
                PublishedAt = now.AddDays(-2),
                UpdatedAt = now,
                Slug = "modern-csharp-component-patterns",
                TagsJson = ToJson(["csharp", "programming", "architecture"]),
                SeriesJson = ToJson(["아키텍처 노트"]),
                LikedByJson = ToJson(["guest", "admin"]),
                ReadTimeMinutes = 9
            },
            new PostRecord
            {
                Title = "slogs 검색 UX를 더 직관적으로 만들기",
                Author = "mina",
                Summary = "검색 패널, 사이드바 태그, 연관 글 추천을 한 번에 정리한 slogs UX 설계 노트입니다.",
                Body = "# 검색 UX\n\n검색은 짧고 명확한 단서(작성자, 제목, 태그)로 필터링할 수 있어야 사용자 편의성이 높습니다.",
                ThumbnailUrl = GetDefaultThumbnailUrl("ux-search-in-slogs"),
                PublishedAt = now.AddDays(-1),
                UpdatedAt = now,
                Slug = "ux-search-in-slogs",
                TagsJson = ToJson(["ux", "design", "search"]),
                SeriesJson = ToJson(["UX 실험실"]),
                ReadTimeMinutes = 7,
                Comments =
                [
                    CreateComment("devin", "좋은 정리입니다. 태그 UX를 강조한 구조가 좋네요.", now.AddHours(-5))
                ]
            });

        await db.SaveChangesAsync();
    }

    private static async Task EnsurePostThumbnailDefaultsAsync(SlogsDbContext db)
    {
        var posts = await db.Posts.ToListAsync();
        var changed = false;

        foreach (var post in posts)
        {
            if (!string.IsNullOrWhiteSpace(post.ThumbnailUrl))
            {
                continue;
            }

            post.ThumbnailUrl = GetDefaultThumbnailUrl(post.Slug);
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static CommentRecord CreateComment(string author, string content, DateTime createdAt)
    {
        return new CommentRecord
        {
            Author = author,
            AuthorNormalized = NormalizeUser(author),
            Content = content,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private static string ToJson(IEnumerable<string> values)
    {
        return JsonSerializer.Serialize(values.ToArray(), GetJsonTypeInfo<string[]>());
    }

    private static JsonTypeInfo<T> GetJsonTypeInfo<T>()
    {
        return (JsonTypeInfo<T>?)SlogsJsonSerializerContext.Default.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"JSON metadata for {typeof(T).FullName} is not registered.");
    }

    private static string GetDefaultBio(string userName)
    {
        return NormalizeUser(userName) switch
        {
            "admin" => "slogs 운영 기준과 샘플 콘텐츠 품질을 점검합니다.",
            "devin" => "Blazor와 .NET으로 읽기 좋은 개발 글을 정리합니다.",
            "junho" => "C# 언어 기능과 아키텍처 패턴을 기록합니다.",
            "mina" => "검색, 탐색, 글쓰기 UX를 실험하고 공유합니다.",
            _ => "slogs에서 개발 경험과 학습 기록을 공유합니다."
        };
    }

    private static bool IsLegacyDefaultProfileImageUrl(string? profileImageUrl)
        => !string.IsNullOrWhiteSpace(profileImageUrl)
            && profileImageUrl.StartsWith("https://api.dicebear.com/9.x/initials/svg", StringComparison.OrdinalIgnoreCase);

    private static string GetDefaultThumbnailUrl(string slug)
    {
        return NormalizeUser(slug) switch
        {
            "blazor-markdown-blog" => "https://images.unsplash.com/photo-1516321318423-f06f85e504b3?auto=format&fit=crop&w=900&q=80",
            "modern-csharp-component-patterns" => "https://images.unsplash.com/photo-1555066931-4365d14bab8c?auto=format&fit=crop&w=900&q=80",
            "ux-search-in-slogs" => "https://images.unsplash.com/photo-1559028012-481c04fa702d?auto=format&fit=crop&w=900&q=80",
            _ => "https://images.unsplash.com/photo-1498050108023-c5249f4df085?auto=format&fit=crop&w=900&q=80"
        };
    }

    private static string NormalizeUser(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
