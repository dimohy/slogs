using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class OrganizationPlatformTests
{
    [Fact]
    public async Task KnowledgeCorpusPrincipalResolverUsesOnlyActiveOrganizationMemberships()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        var adminActor = await fixture.CorpusPrincipals.ResolveAsync(new AuthUser { UserName = "admin" });
        Assert.Equal(OrganizationRoles.Admin, adminActor.OrganizationRoles[fixture.OrganizationId.ToString("D")]);

        var outsiderActor = await fixture.CorpusPrincipals.ResolveAsync(new AuthUser { UserName = "outsider" });
        Assert.Empty(outsiderActor.OrganizationRoles);
    }

    [Fact]
    public async Task OrganizationServicePrincipalCannotEnterPersonalLlmWikiTools()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "service:collector"),
            new Claim(ClaimTypes.Name, "collector"),
            new Claim(OrganizationClaimTypes.ActorKind, OrganizationActorKinds.Service),
            new Claim(OrganizationClaimTypes.TokenScope, OrganizationTokenScopes.Read)
        ], "organization-service-test"));
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        var tools = new LlmWikiMcpTools(accessor, null!, null!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(tools.GetInstructions);
        Assert.Contains("org_wiki_*", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovalIsRoleBoundAndOnlyApprovedMemoryIsRecalledInANewConversation()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        var proposal = await fixture.Wiki.ProposeAsync(
            fixture.OrganizationId,
            CreateAsDispatchProposal(),
            Principal("expert"));

        var beforeApproval = await fixture.Wiki.RecallAsync(
            fixture.OrganizationId,
            "오류 화면 출동",
            OrganizationMemoryScopes.Organization,
            null,
            5,
            Principal("newhire"));
        Assert.Empty(beforeApproval);

        await Assert.ThrowsAsync<OrganizationAccessDeniedException>(() => fixture.Wiki.ApproveAsync(
            fixture.OrganizationId,
            proposal.Id,
            new("신입의 승인 시도는 차단되어야 합니다."),
            Principal("newhire")));

        var approved = await fixture.Wiki.ApproveAsync(
            fixture.OrganizationId,
            proposal.Id,
            new("팀 확인 순서로 승인합니다."),
            Principal("approver"));
        Assert.Equal(OrganizationMemoryStates.Active, approved.State);
        Assert.Equal("approver", approved.ApprovedBy);

        var newConversationRecall = await fixture.Wiki.RecallAsync(
            fixture.OrganizationId,
            "새빛병원 오류 화면 없이 바로 출동",
            OrganizationMemoryScopes.Organization,
            null,
            5,
            Principal("newhire"));
        var recalled = Assert.Single(newConversationRecall);
        Assert.Equal(approved.Id, recalled.Id);
        Assert.Equal(approved.Content, recalled.Content);
        Assert.Equal(approved.ApprovedBy, recalled.ApprovedBy);

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = Principal("newhire") }
        };
        var tools = new OrganizationWikiMcpTools(accessor, fixture.Wiki, null!);
        var mcpJson = await tools.RecallAsync(
            fixture.OrganizationId,
            "새빛병원 오류 화면 없이 바로 출동",
            OrganizationMemoryScopes.Organization,
            null,
            1);
        using var mcpDocument = JsonDocument.Parse(mcpJson);
        var mcpMemory = Assert.Single(mcpDocument.RootElement.EnumerateArray());
        Assert.Equal(approved.Content, mcpMemory.GetProperty("content").GetString());
    }

    [Fact]
    public async Task OrganizationIsolationFailsClosedEvenForAnExistingMemberInAnotherTenant()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        var otherOrganization = Guid.NewGuid();
        await using (var db = await fixture.CreateOrganizationDbAsync())
        {
            db.Organizations.Add(new OrganizationRecord
            {
                Id = otherOrganization,
                Slug = "other-org",
                DisplayName = "다른 조직",
                EnvironmentLabel = "검증 환경"
            });
            db.OrganizationMemberships.Add(new OrganizationMembershipRecord
            {
                OrganizationId = otherOrganization,
                UserName = "outsider",
                Role = OrganizationRoles.Owner,
                DisplayRole = "소유자",
                Status = OrganizationMemberStatuses.Active,
                InvitedBy = "system"
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<OrganizationAccessDeniedException>(() => fixture.Wiki.CategoriesAsync(
            fixture.OrganizationId,
            Principal("outsider")));

        var mismatchedTokenPrincipal = Principal(
            "expert",
            organizationId: otherOrganization,
            tokenScopes: [OrganizationTokenScopes.Read]);
        await Assert.ThrowsAsync<OrganizationAccessDeniedException>(() => fixture.Wiki.CategoriesAsync(
            fixture.OrganizationId,
            mismatchedTokenPrincipal));
    }

    [Fact]
    public async Task ChangedSourceCreatesConflictAndPendingConflictBlocksApproval()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        var sourcePrincipal = Principal("admin");
        var first = await fixture.Wiki.IngestSourceAsync(
            fixture.OrganizationId,
            new(
                "공식 자료",
                "https://example.com/company",
                "official-homepage",
                OrganizationSourceGrades.Official,
                new string('a', 64),
                DateTime.UtcNow,
                "첫 수집",
                null),
            sourcePrincipal);
        var unchanged = await fixture.Wiki.IngestSourceAsync(
            fixture.OrganizationId,
            new(
                "공식 자료",
                "https://example.com/company",
                "official-homepage",
                OrganizationSourceGrades.Official,
                new string('a', 64),
                DateTime.UtcNow.AddHours(1),
                "동일 수집",
                null),
            sourcePrincipal);
        Assert.Equal(first.Id, unchanged.Id);

        var changed = await fixture.Wiki.IngestSourceAsync(
            fixture.OrganizationId,
            new(
                "공식 자료 변경",
                "https://example.com/company",
                "official-homepage",
                OrganizationSourceGrades.Official,
                new string('b', 64),
                DateTime.UtcNow.AddHours(2),
                "변경 수집",
                null),
            sourcePrincipal);
        Assert.NotEqual(first.Id, changed.Id);
        var sourceConflicts = await fixture.Wiki.ListConflictsAsync(
            fixture.OrganizationId,
            true,
            Principal("approver"));
        Assert.Single(sourceConflicts);

        var proposal = await fixture.Wiki.ProposeAsync(
            fixture.OrganizationId,
            CreateAsDispatchProposal(),
            Principal("expert"));
        await fixture.Wiki.CreateConflictAsync(
            fixture.OrganizationId,
            new("dispatch.order", "즉시 출동", "오류 화면 우선", proposal.Id, null, null, null),
            Principal("admin"));
        await Assert.ThrowsAsync<OrganizationConflictException>(() => fixture.Wiki.ApproveAsync(
            fixture.OrganizationId,
            proposal.Id,
            new("충돌 미해결 상태"),
            Principal("approver")));
    }

    [Fact]
    public async Task MetricsSuppressSmallTeamsAndExposeOnlyAggregateValues()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        var owner = Principal("owner");
        var department = await fixture.Directory.CreateUnitAsync(
            fixture.OrganizationId,
            new("서비스부", OrganizationUnitKinds.Department, null),
            owner);
        var team = await fixture.Directory.CreateUnitAsync(
            fixture.OrganizationId,
            new("A/S팀", OrganizationUnitKinds.Team, department.Id),
            owner);
        var now = DateTime.UtcNow;
        for (var index = 1; index <= 4; index++)
        {
            await fixture.Metrics.RecordAsync(
                fixture.OrganizationId,
                new($"employee-{index}", "time_saved_minutes", 10, team.Id, true, now),
                Principal("admin"));
        }

        for (var index = 5; index <= 6; index++)
        {
            await fixture.Metrics.RecordAsync(
                fixture.OrganizationId,
                new($"employee-{index}", "time_saved_minutes", 10, department.Id, true, now),
                Principal("admin"));
        }

        var summary = await fixture.Metrics.SummarizeAsync(
            fixture.OrganizationId,
            now.AddMinutes(-1),
            now.AddMinutes(1),
            owner);
        var teamSummary = Assert.Single(summary, x => x.UnitId == team.Id);
        Assert.True(teamSummary.IsSuppressed);
        Assert.Equal(0, teamSummary.Value);
        var departmentSummary = Assert.Single(summary, x => x.UnitId == department.Id);
        Assert.False(departmentSummary.IsSuppressed);
        Assert.True(departmentSummary.RolledUpToParent);
        Assert.Equal(60, departmentSummary.Value);
        Assert.Equal(6, departmentSummary.CohortSize);
        Assert.All(summary, item => Assert.True(item.IsDemoAssumption));
    }

    [Fact]
    public async Task GuidedSessionRequiresOwnerAndSyntheticRoleAccount()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        await Assert.ThrowsAsync<OrganizationAccessDeniedException>(() => fixture.Directory.StartGuidedSessionAsync(
            fixture.OrganizationId,
            new("expert", 30),
            Principal("admin")));

        var session = await fixture.Directory.StartGuidedSessionAsync(
            fixture.OrganizationId,
            new("expert", 30),
            Principal("owner"));
        Assert.Equal("owner", session.PresenterUserName);
        Assert.Equal("expert", session.ActiveRoleUserName);

        await Assert.ThrowsAsync<OrganizationValidationException>(() => fixture.Directory.SwitchGuidedSessionAsync(
            fixture.OrganizationId,
            session.Id,
            new("regular"),
            Principal("owner")));

        var switched = await fixture.Directory.SwitchGuidedSessionAsync(
            fixture.OrganizationId,
            session.Id,
            new("newhire"),
            Principal("owner"));
        Assert.Equal("newhire", switched.ActiveRoleUserName);
    }

    [Fact]
    public async Task GuidedAccessUsesConnectedClientCredentialsAndInvalidatesThePreviousRoleToken()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        const string clientId = "insightloop-guided-test";
        const string clientSecret = "local-contract-secret";
        await fixture.RegisterGuidedClientAsync(clientId, clientSecret);

        var started = await fixture.GuidedAccess.StartAsync(new(clientId, clientSecret, "expert", 30));
        Assert.StartsWith(OrganizationGuidedAccessService.TokenPrefix, started.AccessToken, StringComparison.Ordinal);
        var expert = await fixture.GuidedAccess.AuthenticateAsync(started.AccessToken);
        Assert.NotNull(expert);
        Assert.Equal("expert", expert.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(OrganizationActorKinds.GuidedRole, expert.FindFirstValue(OrganizationClaimTypes.ActorKind));
        Assert.Contains(expert.FindAll(OrganizationClaimTypes.TokenScope), claim => claim.Value == OrganizationTokenScopes.Propose);
        Assert.DoesNotContain(expert.FindAll(OrganizationClaimTypes.TokenScope), claim => claim.Value == OrganizationTokenScopes.Approve);

        var switched = await fixture.GuidedAccess.SwitchAsync(new(clientId, clientSecret, started.Session.Id, "approver"));
        Assert.Null(await fixture.GuidedAccess.AuthenticateAsync(started.AccessToken));
        var approver = await fixture.GuidedAccess.AuthenticateAsync(switched.AccessToken);
        Assert.NotNull(approver);
        Assert.Equal("approver", approver.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Contains(approver.FindAll(OrganizationClaimTypes.TokenScope), claim => claim.Value == OrganizationTokenScopes.Approve);

        await Assert.ThrowsAsync<OrganizationAccessDeniedException>(() =>
            fixture.GuidedAccess.StartAsync(new(clientId, "wrong-secret", "expert", 30)));
    }

    [Fact]
    public async Task GuidedAccessKeepsIndependentSessionsForTheSameConnectedClient()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        const string clientId = "insightloop-multi-session-test";
        const string clientSecret = "local-contract-secret";
        await fixture.RegisterGuidedClientAsync(clientId, clientSecret);

        var first = await fixture.GuidedAccess.StartAsync(new(clientId, clientSecret, "expert", 30));
        var second = await fixture.GuidedAccess.StartAsync(new(clientId, clientSecret, "newhire", 30));

        Assert.NotEqual(first.Session.Id, second.Session.Id);
        Assert.NotNull(await fixture.GuidedAccess.AuthenticateAsync(first.AccessToken));
        Assert.NotNull(await fixture.GuidedAccess.AuthenticateAsync(second.AccessToken));

        var switchedFirst = await fixture.GuidedAccess.SwitchAsync(new(clientId, clientSecret, first.Session.Id, "approver"));
        Assert.Null(await fixture.GuidedAccess.AuthenticateAsync(first.AccessToken));
        Assert.Equal("approver", (await fixture.GuidedAccess.AuthenticateAsync(switchedFirst.AccessToken))?.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("newhire", (await fixture.GuidedAccess.AuthenticateAsync(second.AccessToken))?.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task ServiceTokenCannotEscalateBeyondCreatorAndNeverReturnsStoredSecret()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        await Assert.ThrowsAsync<OrganizationAccessDeniedException>(() => fixture.Tokens.CreateAsync(
            fixture.OrganizationId,
            new("bad-token", [OrganizationTokenScopes.OidcManage], null),
            Principal("admin")));

        var created = await fixture.Tokens.CreateAsync(
            fixture.OrganizationId,
            new("collector", [OrganizationTokenScopes.Read, OrganizationTokenScopes.SourcesManage], DateTime.UtcNow.AddHours(1)),
            Principal("admin"));
        Assert.StartsWith(OrganizationTokenService.ServiceTokenPrefix, created.Token, StringComparison.Ordinal);
        var authenticated = await fixture.Tokens.AuthenticateAsync(created.Token);
        Assert.NotNull(authenticated);
        Assert.Equal(OrganizationActorKinds.Service, authenticated.FindFirstValue(OrganizationClaimTypes.ActorKind));
        var listed = Assert.Single(await fixture.Tokens.ListAsync(fixture.OrganizationId, Principal("admin")));
        Assert.Equal(created.TokenPrefix, listed.TokenPrefix);
        Assert.DoesNotContain(created.Token, System.Text.Json.JsonSerializer.Serialize(listed), StringComparison.Ordinal);
    }

    private static OrganizationMemoryDraftRequest CreateAsDispatchProposal()
        => new(
            "A/S 출동 전 확인 순서",
            "바로 출동하지 않고 필수 사실을 먼저 확인합니다.",
            "오류 화면, 마지막 정상 시점, 재현 여부, 장비 자체 문제와 연결 프로그램 문제를 먼저 구분합니다.",
            "바로 출동을 결정하지 말고 오류 화면과 마지막 정상 시점을 먼저 확인해야 합니다.",
            ["A/S", "출동", "암묵지"],
            "service/as/dispatch",
            OrganizationMemoryScopes.Organization,
            null,
            null,
            "숙련자의 교정을 조직 후보로 제안합니다.");

    private static ClaimsPrincipal Principal(
        string userName,
        Guid? organizationId = null,
        IReadOnlyList<string>? tokenScopes = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userName),
            new(ClaimTypes.Name, userName)
        };
        if (organizationId is not null)
        {
            claims.Add(new(OrganizationClaimTypes.OrganizationId, organizationId.Value.ToString()));
        }

        if (tokenScopes is not null)
        {
            claims.AddRange(tokenScopes.Select(x => new Claim(OrganizationClaimTypes.TokenScope, x)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private sealed class OrganizationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection organizationConnection;
        private readonly SqliteConnection slogsConnection;
        private readonly ServiceProvider services;

        private OrganizationFixture(
            SqliteConnection organizationConnection,
            SqliteConnection slogsConnection,
            ServiceProvider services,
            Guid organizationId)
        {
            this.organizationConnection = organizationConnection;
            this.slogsConnection = slogsConnection;
            this.services = services;
            OrganizationId = organizationId;
            Directory = services.GetRequiredService<OrganizationDirectoryService>();
            Wiki = services.GetRequiredService<OrganizationWikiService>();
            Metrics = services.GetRequiredService<OrganizationMetricsService>();
            Tokens = services.GetRequiredService<OrganizationTokenService>();
            GuidedAccess = services.GetRequiredService<OrganizationGuidedAccessService>();
            CorpusPrincipals = services.GetRequiredService<KnowledgeCorpusPrincipalResolver>();
        }

        public Guid OrganizationId { get; }
        public OrganizationDirectoryService Directory { get; }
        public OrganizationWikiService Wiki { get; }
        public OrganizationMetricsService Metrics { get; }
        public OrganizationTokenService Tokens { get; }
        public OrganizationGuidedAccessService GuidedAccess { get; }
        public KnowledgeCorpusPrincipalResolver CorpusPrincipals { get; }

        public static async Task<OrganizationFixture> CreateAsync()
        {
            var organizationConnection = new SqliteConnection("Data Source=:memory:");
            var slogsConnection = new SqliteConnection("Data Source=:memory:");
            await organizationConnection.OpenAsync();
            await slogsConnection.OpenAsync();
            var services = new ServiceCollection();
            services.AddDbContextFactory<OrganizationDbContext>(options =>
            {
                options.UseSqlite(organizationConnection);
                options.UseOpenIddict();
            });
            services.AddDbContextFactory<SlogsDbContext>(options => options.UseSqlite(slogsConnection));
            services.AddScoped<OrganizationActorResolver>();
            services.AddScoped<KnowledgeCorpusPrincipalResolver>();
            services.AddSingleton<IOrganizationSemanticIndex, TestOrganizationSemanticIndex>();
            services.AddScoped<OrganizationDirectoryService>();
            services.AddScoped<OrganizationWikiService>();
            services.AddScoped<OrganizationMetricsService>();
            services.AddScoped<OrganizationTokenService>();
            services.AddDataProtection();
            services.AddOpenIddict()
                .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<OrganizationDbContext>());
            services.AddScoped<OrganizationOidcClientService>();
            services.AddScoped<OrganizationGuidedAccessService>();
            var provider = services.BuildServiceProvider();

            await using (var slogsDb = await provider.GetRequiredService<IDbContextFactory<SlogsDbContext>>().CreateDbContextAsync())
            {
                await slogsDb.Database.EnsureCreatedAsync();
                foreach (var userName in new[] { "owner", "admin", "approver", "expert", "newhire", "regular", "outsider" })
                {
                    slogsDb.Users.Add(new UserRecord
                    {
                        UserName = userName,
                        DisplayName = userName,
                        Password = "test",
                        RegisteredAt = DateTime.UtcNow
                    });
                }

                await slogsDb.SaveChangesAsync();
            }

            var organizationId = Guid.NewGuid();
            await using (var organizationDb = await provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>().CreateDbContextAsync())
            {
                await organizationDb.Database.EnsureCreatedAsync();
                organizationDb.Organizations.Add(new OrganizationRecord
                {
                    Id = organizationId,
                    Slug = "fixture-org",
                    DisplayName = "Fixture Organization",
                    EnvironmentLabel = "검증 환경",
                    MinimumAggregateCohort = 5
                });
                organizationDb.OrganizationMemberships.AddRange(
                    Membership(organizationId, "owner", OrganizationRoles.Owner, true),
                    Membership(organizationId, "admin", OrganizationRoles.Admin),
                    Membership(organizationId, "approver", OrganizationRoles.Approver, true),
                    Membership(organizationId, "expert", OrganizationRoles.Member, true),
                    Membership(organizationId, "newhire", OrganizationRoles.Member, true),
                    Membership(organizationId, "regular", OrganizationRoles.Member));
                await organizationDb.SaveChangesAsync();
            }

            return new(organizationConnection, slogsConnection, provider, organizationId);
        }

        public async Task RegisterGuidedClientAsync(string clientId, string clientSecret)
        {
            var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                DisplayName = "InsightLoop guided test"
            };
            var application = await manager.CreateAsync(descriptor);
            var applicationId = await manager.GetIdAsync(application)
                ?? throw new InvalidOperationException("Test OpenIddict application id was not created.");
            await using var db = await CreateOrganizationDbAsync();
            db.OrganizationOidcClients.Add(new OrganizationOidcClientRecord
            {
                OrganizationId = OrganizationId,
                ApplicationId = applicationId,
                ClientId = clientId,
                DisplayName = "InsightLoop guided test",
                RedirectUrisJson = "[]",
                ScopesJson = System.Text.Json.JsonSerializer.Serialize(new[] { OrganizationTokenScopes.GuidedSession }),
                CreatedBy = "owner"
            });
            await db.SaveChangesAsync();
        }

        public Task<OrganizationDbContext> CreateOrganizationDbAsync()
            => services.GetRequiredService<IDbContextFactory<OrganizationDbContext>>().CreateDbContextAsync();

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await organizationConnection.DisposeAsync();
            await slogsConnection.DisposeAsync();
        }

        private static OrganizationMembershipRecord Membership(
            Guid organizationId,
            string userName,
            string role,
            bool synthetic = false)
            => new()
            {
                OrganizationId = organizationId,
                UserName = userName,
                Role = role,
                DisplayRole = role,
                Status = OrganizationMemberStatuses.Active,
                IsSyntheticAccount = synthetic,
                InvitedBy = "system"
            };
    }

    private sealed class TestOrganizationSemanticIndex : IOrganizationSemanticIndex
    {
        private readonly Dictionary<Guid, string> documents = [];

        public Task IndexAsync(OrganizationMemoryRecord memory, CancellationToken cancellationToken = default)
        {
            documents[memory.Id] = string.Join(' ', memory.Title, memory.Summary, memory.SourcePrompt, memory.Content);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<Guid, double>> ScoreAsync(
            Guid organizationId,
            string query,
            IReadOnlyList<Guid> candidateIds,
            int limit,
            CancellationToken cancellationToken = default)
        {
            var scores = candidateIds
                .Where(documents.ContainsKey)
                .Take(limit)
                .ToDictionary(
                    id => id,
                    id => query.Contains("방문", StringComparison.Ordinal)
                        && documents[id].Contains("출동", StringComparison.Ordinal)
                            ? 0.92
                            : 0.75);
            return Task.FromResult<IReadOnlyDictionary<Guid, double>>(scores);
        }
    }
}
