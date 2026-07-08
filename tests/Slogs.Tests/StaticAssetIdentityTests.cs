using System.Text.Json;
using System.Reflection;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class StaticAssetIdentityTests
{
    [Fact]
    public void WebManifestDescriptionUsesKnowledgeLogWording()
    {
        var manifestPath = FindRepoFile("src", "Slogs", "wwwroot", "site.webmanifest");
        var manifest = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            File.ReadAllText(manifestPath));

        Assert.NotNull(manifest);
        Assert.True(manifest!.TryGetValue("description", out var descriptionValue));
        var description = descriptionValue?.ToString() ?? string.Empty;

        Assert.Contains("지식 로그 플랫폼", description);
        Assert.DoesNotContain("개발 블로그 서비스", description);
        Assert.DoesNotContain("글쓰기", description);
    }

    [Fact]
    public void FirstScreenHeaderUsesMeaningRecallAndFlowToolLabels()
    {
        var mainLayout = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Layout", "MainLayout.razor"));
        var navMenu = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Layout", "NavMenu.razor"));
        var reconnectModal = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Layout", "ReconnectModal.razor"));

        Assert.Contains("aria-label=\"slogs 지식 로그 홈\"", mainLayout);
        Assert.Contains("placeholder=\"의미 회상\"", mainLayout);
        Assert.Contains("aria-label=\"전역 의미 회상어 입력\"", mainLayout);
        Assert.Contains("title=\"의미 회상\"", mainLayout);
        Assert.Contains("aria-label=\"지식 로그 도구\"", mainLayout);
        Assert.Contains("href=\"/me\">내 지식 로그", mainLayout);
        Assert.Contains("aria-label=\"지식 로그 흐름 메뉴 열기\"", mainLayout);
        Assert.Contains("aria-label=\"지식 로그 흐름 메뉴 닫기\"", mainLayout);
        Assert.Contains(">흐름 메뉴</span>", mainLayout);
        Assert.Contains("aria-label=\"지식 로그 흐름 메뉴\"", navMenu);
        Assert.Contains(">공개 흐름</div>", navMenu);
        Assert.Contains("Match=\"NavLinkMatch.All\">대표 단서 흐름</NavLink>", navMenu);
        Assert.Contains("href=\"/tag\" Match=\"NavLinkMatch.All\">전체 단서 흐름</NavLink>", navMenu);
        Assert.Contains("href=\"/writer\" Match=\"NavLinkMatch.All\">슬로거 홈 흐름</NavLink>", navMenu);
        Assert.Contains("href=\"/series\" Match=\"NavLinkMatch.All\">로그 시리즈 흐름</NavLink>", navMenu);
        Assert.Contains("반응 단서 흐름", navMenu);
        Assert.Contains("반응 로그 시리즈 흐름", navMenu);
        Assert.Contains("내 단서 흐름", navMenu);
        Assert.Contains("내 로그 시리즈 흐름", navMenu);
        Assert.Contains("공개 단서 흐름이 준비 중입니다.", navMenu);
        Assert.Contains("공개 로그 시리즈 흐름이 아직 없습니다.", navMenu);
        Assert.Contains("내 단서 흐름이 아직 없습니다.", navMenu);
        Assert.Contains("내 로그 시리즈 흐름이 아직 없습니다.", navMenu);
        Assert.Contains("운영 흐름 모드", mainLayout);
        Assert.Contains("시작 슬로거 @", mainLayout);
        Assert.Contains("운영 흐름으로 전환", mainLayout);
        Assert.Contains("슬로거 흐름으로 돌아가기", mainLayout);
        Assert.Contains("지식 로그 흐름이 잠시 끊겼습니다.", mainLayout);
        Assert.Contains("흐름 다시 잇기", mainLayout);
        Assert.Contains("흐름 알림 닫기", mainLayout);
        Assert.Contains("흐름 연결됨", reconnectModal);
        Assert.Contains("흐름 다시 잇는 중", reconnectModal);
        Assert.Contains("흐름 연결 실패", reconnectModal);
        Assert.Contains("흐름 일시 중지", reconnectModal);
        Assert.Contains("흐름 복구 실패", reconnectModal);
        Assert.Contains("흐름 재개", reconnectModal);
        Assert.Contains(">운영 흐름</div>", navMenu);
        Assert.DoesNotContain("href=\"/me\">내 로그", mainLayout);
        Assert.DoesNotContain("placeholder=\"회상어\"", mainLayout);
        Assert.DoesNotContain("aria-label=\"전역 회상어 입력\"", mainLayout);
        Assert.DoesNotContain("aria-label=\"header actions\"", mainLayout);
        Assert.DoesNotContain("title=\"메뉴\"", mainLayout);
        Assert.DoesNotContain("aria-label=\"메뉴 열기\"", mainLayout);
        Assert.DoesNotContain("aria-label=\"모바일 메뉴\"", mainLayout);
        Assert.DoesNotContain("aria-label=\"main menu\"", navMenu);
        Assert.DoesNotContain(">추천 단서</NavLink>", navMenu);
        Assert.DoesNotContain(">전체 단서</NavLink>", navMenu);
        Assert.DoesNotContain(">슬로거</NavLink>", navMenu);
        Assert.DoesNotContain(">로그 시리즈</NavLink>", navMenu);
        Assert.DoesNotContain("인기 단서", navMenu);
        Assert.DoesNotContain("인기 로그 시리즈", navMenu);
        Assert.DoesNotContain("내 단서가 없습니다.", navMenu);
        Assert.DoesNotContain("단서가 준비 중입니다.", navMenu);
        Assert.DoesNotContain("내 로그 시리즈가 없습니다.", navMenu);
        Assert.DoesNotContain("로그 시리즈가 없습니다.", navMenu);
        Assert.DoesNotContain("관리자 모드", mainLayout);
        Assert.DoesNotContain("원래 사용자 @", mainLayout);
        Assert.DoesNotContain("어드민 전환", mainLayout);
        Assert.DoesNotContain("일반 모드로 전환", mainLayout);
        Assert.DoesNotContain("오류가 발생했습니다.", mainLayout);
        Assert.DoesNotContain(">다시 시도</a>", mainLayout);
        Assert.DoesNotContain(">연결됨</span>", reconnectModal);
        Assert.DoesNotContain(">연결 중</span>", reconnectModal);
        Assert.DoesNotContain("재연결 중", reconnectModal);
        Assert.DoesNotContain(">연결 실패</span>", reconnectModal);
        Assert.DoesNotContain(">일시 중지됨</span>", reconnectModal);
        Assert.DoesNotContain(">복구 실패</span>", reconnectModal);
        Assert.DoesNotContain(">재시도", reconnectModal);
        Assert.DoesNotContain(">재개", reconnectModal);
        Assert.DoesNotContain(">어드민</div>", navMenu);
    }

    [Fact]
    public void GlobalMeaningRecallAcceptsKoreanFlowPrefixes()
    {
        var mainLayout = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Layout", "MainLayout.razor"));
        var homePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Home.razor"));

        foreach (var source in new[] { mainLayout, homePage })
        {
            Assert.Contains("[\"tag:\", \"단서:\"]", source);
            Assert.Contains("[\"writer:\", \"슬로거:\"]", source);
            Assert.Contains("[\"series:\", \"시리즈:\", \"로그 시리즈:\"]", source);
            Assert.Contains("TryReadRecallPrefix", source);
        }

        Assert.Contains("IsRecallPrefixOnly(trimmed, [\"tag:\", \"단서:\", \"writer:\", \"슬로거:\", \"series:\", \"시리즈:\", \"로그 시리즈:\"])", homePage);
        Assert.Contains("return $\"/tag/{Uri.EscapeDataString(tagValue)}\";", mainLayout);
        Assert.Contains("return $\"/@{Uri.EscapeDataString(writerValue)}\";", mainLayout);
        Assert.Contains("return $\"/series/{Uri.EscapeDataString(seriesValue)}\";", mainLayout);
        Assert.Contains("($\"단서: {tagValue}\", () => ApiClient.GetByTagAsync(tagValue))", homePage);
        Assert.Contains("($\"슬로거: {writerValue}\", () => ApiClient.GetByAuthorAsync(writerValue))", homePage);
        Assert.Contains("($\"로그 시리즈: {seriesValue}\", () => ApiClient.GetBySeriesAsync(seriesValue))", homePage);
    }

    [Fact]
    public void SeedDefaultBiosUseKnowledgeLogWording()
    {
        var getDefaultBio = typeof(SlogsDbInitializer).GetMethod(
            "GetDefaultBio",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(getDefaultBio);

        var bios = new[] { "devin", "junho", "mina", "new-user" }
            .Select(userName => (string)getDefaultBio!.Invoke(null, [userName])!)
            .ToArray();

        foreach (var bio in bios)
        {
            Assert.Contains("로그", bio);
            Assert.DoesNotContain("개발 글", bio);
            Assert.DoesNotContain("글쓰기", bio);
            Assert.DoesNotContain("검색, 탐색", bio);
        }
    }

    [Fact]
    public void ExternalLoginDefaultBiosUseSloggerHomeFlowWording()
    {
        var authService = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "AuthService.cs"));

        Assert.Contains("로 이어진 지식 로그 홈입니다.", authService);
        Assert.Contains("외부 로그인으로 이어진 지식 로그 홈입니다.", authService);
        Assert.DoesNotContain("계정으로 가입한 슬로거입니다.", authService);
        Assert.DoesNotContain("외부 로그인 계정으로 가입한 슬로거입니다.", authService);
    }

    [Fact]
    public void SeedDefaultConversationCopyUsesTraceFlowWording()
    {
        var initializer = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "SlogsDbInitializer.cs"));

        Assert.Contains("대화 흔적을 이어 남기는 흐름도 넣으면 더 풍부해질 듯합니다.", initializer);
        Assert.Contains("IsLegacyReplyFeatureComment", initializer);
        Assert.DoesNotContain("대화 흔적의 답글", initializer);
        Assert.DoesNotContain("답글 기능", initializer);
        Assert.DoesNotContain("댓글의 답글", initializer);
    }

    [Fact]
    public void UserFacingFailureMessagesUseLogAndRecallWording()
    {
        var apiClient = File.ReadAllText(FindRepoFile("src", "Slogs.Shared", "Data", "SlogsApiClient.cs"));
        var postMcpTools = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "SlogsPostMcpTools.cs"));
        var llmWikiMcpTools = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "LlmWikiMcpTools.cs"));
        var llmWikiService = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "LlmWikiService.cs"));

        Assert.DoesNotContain("게시글 생성", apiClient);
        Assert.Contains("로그 생성에 실패했습니다.", apiClient);

        Assert.DoesNotContain("Slogs 글", postMcpTools);
        Assert.Contains("Slogs 로그 업데이트에 실패했습니다.", postMcpTools);
        Assert.Contains("Slogs 로그 지우기에 실패했습니다.", postMcpTools);
        Assert.Contains("Slogs 로그 slug가 필요합니다.", postMcpTools);
        Assert.Contains("수정할 수 있는 Slogs 로그를 찾지 못했습니다.", postMcpTools);
        Assert.Contains("Saves a Markdown Slogs log before public sharing", postMcpTools);
        Assert.Contains("Pre-publish logs are visible only to the owner", postMcpTools);
        Assert.Contains("Share a Markdown Slogs log publicly", postMcpTools);
        Assert.Contains("Remove an owned Slogs log flow by slug.", postMcpTools);
        Assert.Contains("Owned Slogs log slug to remove.", postMcpTools);
        Assert.Contains("confirm public sharing before calling this", postMcpTools);
        Assert.Contains("Read an owned or public Slogs log by slug", postMcpTools);
        Assert.Contains("# Slogs Log Saved Before Public Sharing", postMcpTools);
        Assert.Contains("# Slogs Log Shared Publicly", postMcpTools);
        Assert.Contains("# Slogs Log Flow Removed", postMcpTools);
        Assert.Contains("- Removed slug:", postMcpTools);
        Assert.Contains("The log is a public Slogs log, not an LLM Wiki entry.", postMcpTools);
        Assert.Contains("Slogs log MCP call", postMcpTools);
        Assert.Contains("Slogs 공개 공유에는 로그 제목이 필요합니다.", postMcpTools);
        Assert.Contains("Slogs 공개 공유에는 로그 Markdown 본문이 필요합니다.", postMcpTools);
        Assert.Contains("Slogs 게시전 기억 저장에는 제목이나 Markdown 본문 중 하나가 필요합니다.", postMcpTools);
        Assert.Contains("Status: {(post.IsDraft ? \"Before public sharing\" : \"Publicly shared\")}", postMcpTools);
        Assert.Contains("Former status: {(post.IsDraft ? \"Before public sharing\" : \"Publicly shared\")}", postMcpTools);
        Assert.DoesNotContain("Slogs 게시전 저장", postMcpTools);
        Assert.DoesNotContain("Slogs post", postMcpTools);
        Assert.DoesNotContain("Slogs posts", postMcpTools);
        Assert.DoesNotContain("site post", postMcpTools);
        Assert.DoesNotContain("# Slogs Post", postMcpTools);
        Assert.DoesNotContain("publish publicly", postMcpTools);
        Assert.DoesNotContain("publicly publish", postMcpTools);
        Assert.DoesNotContain("Slogs post MCP call", postMcpTools);
        Assert.DoesNotContain("Slogs 게시에는 제목", postMcpTools);
        Assert.DoesNotContain("Slogs 게시에는 Markdown", postMcpTools);
        Assert.DoesNotContain("Slogs 로그 삭제에 실패했습니다.", postMcpTools);
        Assert.DoesNotContain("Delete an owned Slogs log by slug.", postMcpTools);
        Assert.DoesNotContain("Owned Slogs log slug to delete.", postMcpTools);
        Assert.DoesNotContain("# Slogs Log Deleted", postMcpTools);
        Assert.DoesNotContain("- Deleted slug:", postMcpTools);
        Assert.DoesNotContain("Status: {(post.IsDraft ? \"Pre-publish\" : \"Published\")}", postMcpTools);
        Assert.DoesNotContain("Former status: {(post.IsDraft ? \"Pre-publish\" : \"Publicly shared\")}", postMcpTools);

        Assert.DoesNotContain("검색어가 필요", llmWikiService);
        Assert.Contains("회상어가 필요합니다.", llmWikiService);

        Assert.Contains("공개 기억 회상에는 @dimohy 같은 대상 슬로거 @name이 필요합니다.", llmWikiMcpTools);
        Assert.DoesNotContain("Public LLM Wiki 조회에는", llmWikiMcpTools);
        Assert.Contains("public memory recall tool", llmWikiService);
        Assert.Contains("public memory recall tools", llmWikiService);
        Assert.Contains("public Slogs LLM Wiki memory", llmWikiService);
        Assert.Contains("broad recall-candidate selection", llmWikiMcpTools);
        Assert.Contains("public memory context", llmWikiMcpTools);
        Assert.Contains("Public Memory Recall Candidates", llmWikiMcpTools);
        Assert.Contains("Public Memory Flow", llmWikiMcpTools);
        Assert.Contains("Public Memory Recall", llmWikiMcpTools);
        Assert.Contains("No matching public memory entries", llmWikiMcpTools);
        Assert.DoesNotContain("public lookup tool", llmWikiService);
        Assert.DoesNotContain("public lookup tools", llmWikiService);
        Assert.DoesNotContain("public Slogs LLM Wiki information", llmWikiService);
        Assert.DoesNotContain("broad lookup", llmWikiMcpTools);
        Assert.DoesNotContain("public information", llmWikiMcpTools);
        Assert.DoesNotContain("Public LLM Wiki Search", llmWikiMcpTools);
        Assert.DoesNotContain("Public LLM Wiki Entries", llmWikiMcpTools);
        Assert.DoesNotContain("Public LLM Wiki Recall", llmWikiMcpTools);
        Assert.DoesNotContain("public compact context", llmWikiMcpTools);
    }

    [Fact]
    public void AdminUserFiltersUseClueLanguageInsteadOfGenericSearch()
    {
        var adminUsersPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "AdminUsers.razor"));
        var navMenu = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Layout", "NavMenu.razor"));

        Assert.Contains("<PageTitle>운영 슬로거 흐름 | slogs</PageTitle>", adminUsersPage);
        Assert.Contains(">운영 슬로거 흐름</h1>", adminUsersPage);
        Assert.Contains("aria-label=\"운영 슬로거 흐름 보기\"", adminUsersPage);
        Assert.Contains("운영 흐름 권한이 필요합니다.", adminUsersPage);
        Assert.Contains(">슬로거 홈 흐름</a>", adminUsersPage);
        Assert.Contains(">기억 회상 지표</a>", adminUsersPage);
        Assert.Contains(">노트 Vault 흐름</a>", adminUsersPage);
        Assert.Contains("aria-label=\"슬로거 홈 흐름 요약\"", adminUsersPage);
        Assert.Contains(">등록 슬로거</div>", adminUsersPage);
        Assert.Contains(">로그 흐름</div>", adminUsersPage);
        Assert.Contains(">공개 공유 노드</div>", adminUsersPage);
        Assert.Contains(">@@name 정리 후보</div>", adminUsersPage);
        Assert.Contains("placeholder=\"슬로거 단서\"", adminUsersPage);
        Assert.Contains("aria-label=\"슬로거 단서 입력\"", adminUsersPage);
        Assert.Contains("placeholder=\"LLM Wiki 슬로거 단서\"", adminUsersPage);
        Assert.Contains("aria-label=\"LLM Wiki 슬로거 단서 입력\"", adminUsersPage);
        Assert.Contains("placeholder=\"노트 Vault 슬로거 단서\"", adminUsersPage);
        Assert.Contains("aria-label=\"노트 Vault 슬로거 단서 입력\"", adminUsersPage);
        Assert.Contains("기억 슬로거만", adminUsersPage);
        Assert.Contains("노트 Vault 슬로거만", adminUsersPage);
        Assert.Contains("aria-label=\"슬로거 홈 흐름 정렬\"", adminUsersPage);
        Assert.Contains("<option value=\"registered\">처음 연결순</option>", adminUsersPage);
        Assert.Contains("<option value=\"profile\">홈 갱신순</option>", adminUsersPage);
        Assert.Contains("<option value=\"posts\">로그 흐름순</option>", adminUsersPage);
        Assert.Contains("<option value=\"name\">@@name순</option>", adminUsersPage);
        Assert.Contains("명 홈 흐름 표시", adminUsersPage);
        Assert.Contains(">처음 연결</th>", adminUsersPage);
        Assert.Contains(">홈 갱신</th>", adminUsersPage);
        Assert.Contains(">로그 흐름</th>", adminUsersPage);
        Assert.Contains(">공개 홈</th>", adminUsersPage);
        Assert.Contains(">@@name 흐름</th>", adminUsersPage);
        Assert.Contains("예약 @@name", adminUsersPage);
        Assert.Contains("이어 볼 슬로거 홈 흐름이 없습니다.", adminUsersPage);
        Assert.Contains(">홈 열기</a>", adminUsersPage);
        Assert.Contains("@@name 정리 후 해당 슬로거는 다시 로그인해야 합니다.", adminUsersPage);
        Assert.Contains("예약 @name은 변경하거나 대상으로 사용할 수 없습니다.", adminUsersPage);
        Assert.Contains("\"정리 중\" : \"정리\"", adminUsersPage);
        Assert.Contains("aria-label=\"LLM Wiki 기억 요약\"", adminUsersPage);
        Assert.Contains("slogs 슬로거, 기억 회상, 노트 Vault 흐름을 함께 따라갑니다.", adminUsersPage);
        Assert.Contains("슬로거 홈과 공개 공유 노드, 게시전 기억이 어떻게 이어지는지 함께 따라갑니다.", adminUsersPage);
        Assert.Contains("비공개 기억, 회상 접근, Agent 연결 품질 신호를 함께 살핍니다.", adminUsersPage);
        Assert.Contains("로컬 노트 Vault, 노트 원문, 연결 흔적 흐름을 함께 따라갑니다.", adminUsersPage);
        Assert.Contains(">기억 노드</div>", adminUsersPage);
        Assert.Contains(">기억 흐름</div>", adminUsersPage);
        Assert.Contains(">7일 기억 흐름</div>", adminUsersPage);
        Assert.Contains(">30일 기억 흐름</div>", adminUsersPage);
        Assert.Contains("aria-label=\"Agent 회상 품질 지표\"", adminUsersPage);
        Assert.Contains(">LLM Wiki 회상 품질</h2>", adminUsersPage);
        Assert.Contains("이후 감사 흐름 기준", adminUsersPage);
        Assert.Contains("최근 Agent 접근", adminUsersPage);
        Assert.Contains(">30일 Agent 접근</div>", adminUsersPage);
        Assert.Contains(">후보 회상</div>", adminUsersPage);
        Assert.Contains("후보 회상 접근", adminUsersPage);
        Assert.Contains(">유효 회상률</div>", adminUsersPage);
        Assert.Contains("빈 회상", adminUsersPage);
        Assert.Contains(">회상 속도</div>", adminUsersPage);
        Assert.Contains(">반복 회상률</div>", adminUsersPage);
        Assert.Contains("느린 회상", adminUsersPage);
        Assert.Contains(">기억 변경</div>", adminUsersPage);
        Assert.Contains(">회상 도구</th>", adminUsersPage);
        Assert.Contains(">접근</th>", adminUsersPage);
        Assert.Contains(">유효</th>", adminUsersPage);
        Assert.Contains(">최근 접근</th>", adminUsersPage);
        Assert.Contains("최근 Agent 회상 감사 흐름이 없습니다.", adminUsersPage);
        Assert.Contains(">슬로거</div>", navMenu);
        Assert.Contains(">슬로거 홈 흐름</a>", navMenu);
        Assert.Contains(">기억 회상</a>", navMenu);
        Assert.Contains(">노트 Vault 흐름</a>", navMenu);
        Assert.Contains("<option value=\"entries\">기억 노드순</option>", adminUsersPage);
        Assert.Contains("<option value=\"accesses\">회상 접근순</option>", adminUsersPage);
        Assert.Contains("<option value=\"tokens\">Agent 연결순</option>", adminUsersPage);
        Assert.Contains("<option value=\"activity\">최근 기억순</option>", adminUsersPage);
        Assert.Contains("aria-label=\"LLM Wiki 기억 정렬\"", adminUsersPage);
        Assert.Contains("명 기억 흐름 표시", adminUsersPage);
        Assert.Contains(">기억 노드</th>", adminUsersPage);
        Assert.Contains(">근거 흐름</th>", adminUsersPage);
        Assert.Contains(">기억 흐름</th>", adminUsersPage);
        Assert.Contains("남김 {user.LlmWikiRememberCount:N0} / 병합 {user.LlmWikiMergeCount:N0} / 갱신 {user.LlmWikiUpdateCount:N0}", adminUsersPage);
        Assert.Contains(">7일 기억 흐름</th>", adminUsersPage);
        Assert.Contains(">30일 기억 흐름</th>", adminUsersPage);
        Assert.Contains(">회상 접근</th>", adminUsersPage);
        Assert.Contains(">Agent 연결</th>", adminUsersPage);
        Assert.Contains(">최근 기억</th>", adminUsersPage);
        Assert.Contains(">최근 회상</th>", adminUsersPage);
        Assert.Contains("이어 볼 LLM Wiki 기억 슬로거가 없습니다.", adminUsersPage);
        Assert.Contains("공개 공유 {user.PublishedPostCount:N0} / 게시전 기억 {user.DraftPostCount:N0}", adminUsersPage);

        Assert.DoesNotContain("placeholder=\"사용자 검색\"", adminUsersPage);
        Assert.DoesNotContain("사용자 검색어 입력", adminUsersPage);
        Assert.DoesNotContain("LLM Wiki 사용자 검색", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync 사용자 검색", adminUsersPage);
        Assert.DoesNotContain("어드민 사용자", adminUsersPage);
        Assert.DoesNotContain("어드민 슬로거 흐름", adminUsersPage);
        Assert.DoesNotContain("어드민 권한이 필요합니다.", adminUsersPage);
        Assert.DoesNotContain(">사용자 관리</a>", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"사용자 요약\"", adminUsersPage);
        Assert.DoesNotContain(">가입 사용자</div>", adminUsersPage);
        Assert.DoesNotContain(">공개 로그</div>", adminUsersPage);
        Assert.DoesNotContain("placeholder=\"사용자 단서\"", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"사용자 단서 입력\"", adminUsersPage);
        Assert.DoesNotContain("placeholder=\"LLM Wiki 사용자 단서\"", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"LLM Wiki 사용자 단서 입력\"", adminUsersPage);
        Assert.DoesNotContain("placeholder=\"Obsidian Sync 사용자 단서\"", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"Obsidian Sync 사용자 단서 입력\"", adminUsersPage);
        Assert.DoesNotContain("placeholder=\"Obsidian Sync 슬로거 단서\"", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"Obsidian Sync 슬로거 단서 입력\"", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"LLM Wiki 정렬\"", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"슬로거 정렬\"", adminUsersPage);
        Assert.DoesNotContain("LLM Wiki 사용자만", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 사용자만", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 슬로거만", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"LLM Wiki 요약\"", adminUsersPage);
        Assert.DoesNotContain("사용자 기본 정보와 사용자 관련 관리 기능을 확인합니다.", adminUsersPage);
        Assert.DoesNotContain("슬로거 홈, 공개 로그, 게시전 로그 흐름 신호를 확인합니다.", adminUsersPage);
        Assert.DoesNotContain("slogs 슬로거, 기억 회상, 노트 Vault 흐름을 확인합니다.", adminUsersPage);
        Assert.DoesNotContain("슬로거 홈과 공개/게시전 로그가 어떻게 이어지는지 확인합니다.", adminUsersPage);
        Assert.DoesNotContain("슬로거 홈과 공개/게시전 로그가 어떻게 이어지는지 함께 따라갑니다.", adminUsersPage);
        Assert.DoesNotContain(">사용자 관리</a>", navMenu);
        Assert.DoesNotContain(">슬로거 관리</a>", adminUsersPage);
        Assert.DoesNotContain(">슬로거 관리</a>", navMenu);
        Assert.DoesNotContain(">LLM Wiki 통계</a>", adminUsersPage);
        Assert.DoesNotContain(">Obsidian Sync</a>", adminUsersPage);
        Assert.DoesNotContain("LLM Wiki 사용량과 MCP 품질 지표를 확인합니다.", adminUsersPage);
        Assert.DoesNotContain("비공개 기억, 회상 접근, Agent 연결 품질 신호를 확인합니다.", adminUsersPage);
        Assert.DoesNotContain("MCP 호출 품질 신호", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"MCP 품질 지표\"", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"MCP 회상 품질 지표\"", adminUsersPage);
        Assert.DoesNotContain(">LLM Wiki MCP 품질</h2>", adminUsersPage);
        Assert.DoesNotContain("감사 로그 기준", adminUsersPage);
        Assert.DoesNotContain(">Recall/Search</div>", adminUsersPage);
        Assert.DoesNotContain("최근 호출", adminUsersPage);
        Assert.DoesNotContain(">30일 호출</div>", adminUsersPage);
        Assert.DoesNotContain("후보 탐색 호출", adminUsersPage);
        Assert.DoesNotContain(">도구</th>", adminUsersPage);
        Assert.DoesNotContain(">유효 결과율</div>", adminUsersPage);
        Assert.DoesNotContain("빈 결과", adminUsersPage);
        Assert.DoesNotContain(">응답 속도</div>", adminUsersPage);
        Assert.DoesNotContain(">재조회율</div>", adminUsersPage);
        Assert.DoesNotContain(">기록 변경</div>", adminUsersPage);
        Assert.DoesNotContain(">호출</th>", adminUsersPage);
        Assert.DoesNotContain(">성공</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 호출</th>", adminUsersPage);
        Assert.DoesNotContain("최근 MCP 감사 로그가 없습니다.", adminUsersPage);
        Assert.DoesNotContain("최근 MCP 회상 감사 로그가 없습니다.", adminUsersPage);
        Assert.DoesNotContain("MCP 토큰순", adminUsersPage);
        Assert.DoesNotContain(">MCP 토큰</th>", adminUsersPage);
        Assert.DoesNotContain("저장 {user.LlmWikiRememberCount:N0} / 병합 {user.LlmWikiMergeCount:N0} / 수정 {user.LlmWikiUpdateCount:N0}", adminUsersPage);
        Assert.DoesNotContain("게시전 로그 관리 신호", adminUsersPage);
        Assert.DoesNotContain("공개 로그 {user.PublishedPostCount:N0} / 게시전 로그 {user.DraftPostCount:N0}", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync 노트 Vault, 노트 원문, 연결 기기 흐름을 확인합니다.", adminUsersPage);
        Assert.DoesNotContain(">LLM Wiki</a>", navMenu);
        Assert.DoesNotContain(">Obsidian Sync</a>", navMenu);
        Assert.DoesNotContain("공개 {user.PublishedPostCount:N0} / 초안 {user.DraftPostCount:N0}", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"슬로거 요약\"", adminUsersPage);
        Assert.DoesNotContain(">전체 로그</div>", adminUsersPage);
        Assert.DoesNotContain(">이름 변경 가능</div>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"registered\">가입일순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"profile\">프로필 수정순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"posts\">로그 많은순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"name\">아이디순</option>", adminUsersPage);
        Assert.DoesNotContain("N0}명 표시", adminUsersPage);
        Assert.DoesNotContain(">가입</th>", adminUsersPage);
        Assert.DoesNotContain(">프로필 수정</th>", adminUsersPage);
        Assert.DoesNotContain(">로그</th>", adminUsersPage);
        Assert.DoesNotContain(">공개 페이지</th>", adminUsersPage);
        Assert.DoesNotContain(">홈 흐름</th>", adminUsersPage);
        Assert.DoesNotContain("표시할 슬로거가 없습니다.", adminUsersPage);
        Assert.DoesNotContain(">열기</a>", adminUsersPage);
        Assert.DoesNotContain("\"변경 중\" : \"변경\"", adminUsersPage);
        Assert.DoesNotContain("변경 후 해당 사용자는 다시 로그인해야 합니다.", adminUsersPage);
        Assert.DoesNotContain("예약 계정", adminUsersPage);
        Assert.DoesNotContain("예약 계정 이름은 변경하거나 대상으로 사용할 수 없습니다.", adminUsersPage);
        Assert.DoesNotContain("font-bold uppercase text-slate-500\">등록 슬로거", adminUsersPage);
        Assert.DoesNotContain(">엔트리순</option>", adminUsersPage);
        Assert.DoesNotContain("기억 엔트리순", adminUsersPage);
        Assert.DoesNotContain("<option value=\"activity\">최근 활동순</option>", adminUsersPage);
        Assert.DoesNotContain(">조회순</option>", adminUsersPage);
        Assert.DoesNotContain("표시할 LLM Wiki 사용자가 없습니다.", adminUsersPage);
        Assert.DoesNotContain(">AGENT 연결</th>", adminUsersPage);
        Assert.DoesNotContain(">엔트리</div>", adminUsersPage);
        Assert.DoesNotContain(">기억 엔트리</div>", adminUsersPage);
        Assert.DoesNotContain(">활동</div>", adminUsersPage);
        Assert.DoesNotContain(">기억 활동</div>", adminUsersPage);
        Assert.DoesNotContain(">7일 활동</div>", adminUsersPage);
        Assert.DoesNotContain(">7일 기억</div>", adminUsersPage);
        Assert.DoesNotContain(">30일 활동</div>", adminUsersPage);
        Assert.DoesNotContain(">30일 기억</div>", adminUsersPage);
        Assert.DoesNotContain(">엔트리</th>", adminUsersPage);
        Assert.DoesNotContain(">기억 엔트리</th>", adminUsersPage);
        Assert.DoesNotContain(">소스</th>", adminUsersPage);
        Assert.DoesNotContain(">근거 소스</th>", adminUsersPage);
        Assert.DoesNotContain(">활동</th>", adminUsersPage);
        Assert.DoesNotContain(">기억 활동</th>", adminUsersPage);
        Assert.DoesNotContain(">7일 기억</th>", adminUsersPage);
        Assert.DoesNotContain(">30일 기억</th>", adminUsersPage);
        Assert.DoesNotContain(">조회</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 활동</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 조회</th>", adminUsersPage);
    }

    [Fact]
    public void LlmWikiRecallCardsUseRecallAccessAndMemoryFlowWording()
    {
        var llmWikiSearchPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWikiSearch.razor"));

        Assert.Contains(">기억 범주</p>", llmWikiSearchPage);
        Assert.Contains("기억 범주로 좁힌 뒤 아래 기억 흐름에서 이어 쓸 기억을 선택합니다.", llmWikiSearchPage);
        Assert.Contains("기억 범주가 없습니다.", llmWikiSearchPage);
        Assert.Contains("전체 기억", llmWikiSearchPage);
        Assert.Contains("모든 기억 범주", llmWikiSearchPage);
        Assert.Contains("이어 쓸 기억 흐름", llmWikiSearchPage);
        Assert.Contains("data-llm-wiki-recall-flowline=\"true\"", llmWikiSearchPage);
        Assert.Contains("aria-label=\"LLM Wiki 기억 회상 흐름 신호\"", llmWikiSearchPage);
        Assert.Contains("현재 기억 회상 흐름", llmWikiSearchPage);
        Assert.Contains("GetRecallFlowTitle()", llmWikiSearchPage);
        Assert.Contains("GetRecallFlowDescription()", llmWikiSearchPage);
        Assert.Contains("FormatVisibleMemoryNodeCount()", llmWikiSearchPage);
        Assert.Contains("GetRecallScopeSignal()", llmWikiSearchPage);
        Assert.Contains("GetDraftBridgeSignal()", llmWikiSearchPage);
        Assert.Contains("검토 후 공개 공유 흐름", llmWikiSearchPage);
        Assert.Contains("회상 기억 노드", llmWikiSearchPage);
        Assert.Contains("게시전 기억 연결", llmWikiSearchPage);
        Assert.Contains("게시전 기억 연결 대기", llmWikiSearchPage);
        Assert.Contains("범주+의미 회상", llmWikiSearchPage);
        Assert.Contains("개 기억 회상 중", llmWikiSearchPage);
        Assert.Contains("회 회상 접근", llmWikiSearchPage);
        Assert.Contains("공개 기억", llmWikiSearchPage);
        Assert.Contains("비공개 기억", llmWikiSearchPage);
        Assert.Contains("다음 기억 흐름을 불러오는 중...", llmWikiSearchPage);
        Assert.Contains("이어 쓸 기억 흐름을 모두 불러왔습니다.", llmWikiSearchPage);
        Assert.Contains("선택한 기억 범주에 이어 쓸 기억이 없습니다.", llmWikiSearchPage);
        Assert.Contains("아직 이어 쓸 비공개 기억이 없습니다.", llmWikiSearchPage);
        Assert.Contains("의미 회상에 이어진 기억이 없습니다.", llmWikiSearchPage);

        Assert.DoesNotContain(">카테고리</p>", llmWikiSearchPage);
        Assert.DoesNotContain("카테고리로 좁힌", llmWikiSearchPage);
        Assert.DoesNotContain("카테고리가 없습니다.", llmWikiSearchPage);
        Assert.DoesNotContain("모든 카테고리", llmWikiSearchPage);
        Assert.DoesNotContain("카드 그리드", llmWikiSearchPage);
        Assert.DoesNotContain("기억 카드", llmWikiSearchPage);
        Assert.DoesNotContain("다음 기억을 불러오는 중...", llmWikiSearchPage);
        Assert.DoesNotContain("더 이상 이어 볼 기억이 없습니다.", llmWikiSearchPage);
        Assert.DoesNotContain("개 표시 중", llmWikiSearchPage);
        Assert.DoesNotContain("회 열람", llmWikiSearchPage);
        Assert.DoesNotContain("공개 Wiki", llmWikiSearchPage);
        Assert.DoesNotContain("다음 Wiki를 불러오는 중", llmWikiSearchPage);
        Assert.DoesNotContain("더 이상 표시할 Wiki", llmWikiSearchPage);
        Assert.DoesNotContain("표시할 LLM Wiki", llmWikiSearchPage);
        Assert.DoesNotContain("선택한 기억 범주에 이어 볼 기억이 없습니다.", llmWikiSearchPage);
        Assert.DoesNotContain("저장된 비공개 기억이 없습니다.", llmWikiSearchPage);
        Assert.DoesNotContain("회상된 기억이 없습니다.", llmWikiSearchPage);
        Assert.DoesNotContain("검색 결과", llmWikiSearchPage);
    }

    [Fact]
    public void LlmWikiMemoryToLogBridgeUsesPrePublishMemoryWording()
    {
        var llmWikiGuidePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWiki.razor"));
        var llmWikiSearchPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWikiSearch.razor"));

        Assert.Contains("개인 LLM Wiki 기억을 회상하고 Slogs 게시전 기억으로 이어 씁니다.", llmWikiSearchPage);
        Assert.Contains("소유자 전용 게시전 기억으로 이어 씁니다.", llmWikiSearchPage);
        Assert.Contains("비공개 기억을 바로 공개하지 않고 소유자 전용 게시전 기억으로 옮긴 뒤", llmWikiSearchPage);
        Assert.Contains("게시전 기억으로 이어쓰기", llmWikiSearchPage);
        Assert.Contains("게시전 기억 여는 중...", llmWikiSearchPage);
        Assert.Contains("data-llm-wiki-draft-action-boundary=\"true\"", llmWikiSearchPage);
        Assert.Contains("비공개 기억 -> 소유자 전용 게시전 기억 -> 검토 후 공개 공유", llmWikiSearchPage);
        Assert.Contains("소유자 전용 게시전 기억", llmWikiSearchPage);
        Assert.Contains("공개 공유 후에는 슬로거 홈, 공개 공유 노드, 대화 흔적, 리비전 흐름으로 이어집니다.", llmWikiSearchPage);
        Assert.Contains("이 게시전 기억은 Slogs LLM Wiki에서 이어온 소유자 전용 흐름입니다.", llmWikiSearchPage);
        Assert.Contains("## 게시전 공유 경계", llmWikiSearchPage);
        Assert.Contains("현재 단계: 비공개 기억 -> 소유자 전용 게시전 기억", llmWikiSearchPage);
        Assert.Contains("다음 단계: 민감한 단서 정리 -> 검토 후 공개 공유", llmWikiSearchPage);
        Assert.Contains("공개 공유 후 흐름: 슬로거 홈 -> 공개 공유 노드 -> 대화 흔적 -> 리비전 흐름", llmWikiSearchPage);
        Assert.Contains("## 공개 공유 노드로 정리할 흐름", llmWikiSearchPage);
        Assert.Contains("이 게시전 기억은 공개 공유 전까지 소유자에게만 보입니다.", llmWikiSearchPage);
        Assert.Contains("이 로그에서 이어질 작업이나 다시 따라갈 지점을 남깁니다.", llmWikiSearchPage);
        Assert.Contains("소유자 전용 게시전 기억으로 이어집니다.", llmWikiGuidePage);
        Assert.Contains("<span>게시전 기억</span>", llmWikiGuidePage);

        Assert.DoesNotContain("개인 LLM Wiki 기억을 회상하고 Slogs 게시전 로그로 이어 씁니다.", llmWikiSearchPage);
        Assert.DoesNotContain("소유자 전용 게시전 로그로 이어 씁니다.", llmWikiSearchPage);
        Assert.DoesNotContain("비공개 기억을 바로 공개하지 않고 소유자 전용 게시전 로그로 옮긴 뒤", llmWikiSearchPage);
        Assert.DoesNotContain("게시전 로그로 이어쓰기", llmWikiSearchPage);
        Assert.DoesNotContain("게시전 로그 여는 중...", llmWikiSearchPage);
        Assert.DoesNotContain("비공개 기억 -> 소유자 전용 게시전 로그 -> 검토 후 공개 공유", llmWikiSearchPage);
        Assert.DoesNotContain("이 게시전 로그는 Slogs LLM Wiki", llmWikiSearchPage);
        Assert.DoesNotContain("현재 단계: 비공개 기억 -> 소유자 전용 게시전 로그", llmWikiSearchPage);
        Assert.DoesNotContain("Slogs LLM Wiki가 비공개 기억을 Agent 회상과 소유자 전용 게시전 로그로 이어 두는 연결면입니다.", llmWikiGuidePage);
        Assert.DoesNotContain("비공개 기억을 Agent 회상과 Slogs 게시전 로그로 이어 두는 연결면입니다.", llmWikiGuidePage);
        Assert.DoesNotContain("<span>게시전 로그</span>", llmWikiGuidePage);
        Assert.DoesNotContain("Slogs 로그 초안으로 이어 씁니다.", llmWikiSearchPage);
        Assert.DoesNotContain("게시전 로그 초안", llmWikiSearchPage);
        Assert.DoesNotContain("초안 생성 중...", llmWikiSearchPage);
        Assert.DoesNotContain("로그 초안으로 이어쓰기", llmWikiSearchPage);
        Assert.DoesNotContain("이 초안은 Slogs LLM Wiki", llmWikiSearchPage);
        Assert.DoesNotContain("소유자 전용 로그 초안", llmWikiGuidePage);
        Assert.DoesNotContain("<span>로그 초안</span>", llmWikiGuidePage);
        Assert.DoesNotContain("즉시 공개", llmWikiSearchPage);
        Assert.DoesNotContain("이 로그에서 이어질 작업이나 다시 확인할 지점을 남깁니다.", llmWikiSearchPage);
        Assert.DoesNotContain("검토 후 공개 로그 흐름", llmWikiSearchPage);
        Assert.DoesNotContain("공개 공유 후에는 슬로거 홈, 공개 로그 흐름, 대화 흔적, 리비전 흐름으로 이어집니다.", llmWikiSearchPage);
        Assert.DoesNotContain("공개 공유 후 흐름: 슬로거 홈 -> 공개 로그 흐름 -> 대화 흔적 -> 리비전 흐름", llmWikiSearchPage);
        Assert.DoesNotContain("## 공개 로그로 정리할 흐름", llmWikiSearchPage);
    }

    [Fact]
    public void LlmWikiDetailModalFramesRawProvenanceAsMemoryEvidenceFlow()
    {
        var llmWikiSearchPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWikiSearch.razor"));

        Assert.Contains("기억을 만든 요청 흐름", llmWikiSearchPage);
        Assert.Contains("정리된 기억 내용", llmWikiSearchPage);
        Assert.Contains("기억 근거 흐름", llmWikiSearchPage);
        Assert.Contains("기억으로 남기거나 병합/갱신할 때 남긴 근거를 공개 공유 전 감사 흐름으로 다시 따라갑니다.", llmWikiSearchPage);
        Assert.Contains("기억 남김 근거", llmWikiSearchPage);
        Assert.Contains("기억 병합 근거", llmWikiSearchPage);
        Assert.Contains("기억 갱신 근거", llmWikiSearchPage);
        Assert.Contains("기억 포착 근거", llmWikiSearchPage);
        Assert.Contains("근거 제목", llmWikiSearchPage);
        Assert.Contains("근거 단서", llmWikiSearchPage);
        Assert.Contains("기억 범주", llmWikiSearchPage);
        Assert.Contains("근거 요청 흐름", llmWikiSearchPage);
        Assert.Contains("근거 내용", llmWikiSearchPage);

        Assert.DoesNotContain(">원천 기록</p>", llmWikiSearchPage);
        Assert.DoesNotContain(">정리된 내용</p>", llmWikiSearchPage);
        Assert.DoesNotContain(">title</span>", llmWikiSearchPage);
        Assert.DoesNotContain(">tags</span>", llmWikiSearchPage);
        Assert.DoesNotContain(">categoryPath</span>", llmWikiSearchPage);
        Assert.DoesNotContain("저장, 병합, 갱신 때 남긴 원문 근거를 공개 공유 전 감사 흐름으로 확인합니다.", llmWikiSearchPage);
        Assert.DoesNotContain("저장, 병합, 갱신 때 남긴 기억 근거", llmWikiSearchPage);
        Assert.DoesNotContain("기억 저장 근거", llmWikiSearchPage);
    }

    [Fact]
    public void LlmWikiUsageGuideFramesToolNamesAsRecallFlow()
    {
        var llmWikiGuidePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWiki.razor"));
        var navMenu = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Layout", "NavMenu.razor"));

        Assert.Contains("LLM Wiki 기억 연결", llmWikiGuidePage);
        Assert.Contains("Slogs LLM Wiki가 비공개 기억을 Agent 회상과 소유자 전용 게시전 기억으로 이어 두는 연결면입니다.", llmWikiGuidePage);
        Assert.Contains("비공개 기억을 Agent 회상과 Slogs 게시전 기억으로 이어 두는 연결면입니다.", llmWikiGuidePage);
        Assert.Contains("기억에서 공개 공유로", llmWikiGuidePage);
        Assert.Contains("LLM Wiki는 공개 공유 노드 뒤의 기억 계층입니다", llmWikiGuidePage);
        Assert.Contains(">비공개 기억</div>", navMenu);
        Assert.Contains("기억 연결 가이드", navMenu);
        Assert.Contains("<span>기억 남김</span>", llmWikiGuidePage);
        Assert.Contains("하나의 기억 흐름으로 남깁니다.", llmWikiGuidePage);
        Assert.Contains("768차원 기억 벡터로 남고", llmWikiGuidePage);
        Assert.Contains("graph node 관계 신호로 이어집니다.", llmWikiGuidePage);
        Assert.Contains("Slogs LLM Wiki에서 먼저 관련 기억을 회상합니다.", llmWikiGuidePage);
        Assert.Contains("search</code> 도구는 회상 후보 흐름을 압축해 보여 주고", llmWikiGuidePage);
        Assert.Contains("recall</code> 도구는 답변/구현에 바로 적용할 기억 맥락으로 이어 줍니다.", llmWikiGuidePage);
        Assert.Contains("MCP 회상 응답의 Retrieval Diagnostics로 결과 수, limit, categoryPath, minRelevancePercent, elapsedMs를 살펴 회상 품질을 조율합니다.", llmWikiGuidePage);
        Assert.Contains("암묵지 기억 후보를 조용히 점검합니다.", llmWikiGuidePage);
        Assert.Contains("기억으로 남기기 전에는 관련 기억을 먼저 회상하고", llmWikiGuidePage);
        Assert.Contains("categoryPath</code>를 정해 기억으로 남깁니다.", llmWikiGuidePage);
        Assert.Contains("기억으로 남기지 않습니다.", llmWikiGuidePage);
        Assert.Contains("Agent 회상 연결 키 입력과 전역/프로젝트/현재 세션 범위 선택을 안내하고", llmWikiGuidePage);
        Assert.Contains("처음 설치할 때만 Agent가 회상 연결 키를 요청하고", llmWikiGuidePage);
        Assert.Contains("최초 설치 시 Agent 회상 연결 키와 적용 범위를 묻도록 안내합니다.", llmWikiGuidePage);
        Assert.Contains("도구 노출 점검으로 먼저 지연 로딩 여부를 살핍니다.", llmWikiGuidePage);
        Assert.Contains("Agent의 도구 노출 점검으로 먼저 지연 로딩을 살피도록 안내합니다.", llmWikiGuidePage);
        Assert.Contains("search</code>로 작은 회상 후보 흐름을 잡습니다.", llmWikiGuidePage);
        Assert.Contains("답변이나 구현에 바로 적용할 기억 맥락은 낮은 limit의", llmWikiGuidePage);
        Assert.Contains("다시 회상합니다.", llmWikiGuidePage);

        Assert.DoesNotContain("LLM Wiki 사용법", navMenu);
        Assert.DoesNotContain("님의 기억을 회상하고 Slogs 로그로 이어 쓰는 방법입니다.", llmWikiGuidePage);
        Assert.DoesNotContain("Slogs LLM Wiki를 먼저 조회합니다.", llmWikiGuidePage);
        Assert.DoesNotContain("MCP 응답의 Retrieval Diagnostics", llmWikiGuidePage);
        Assert.DoesNotContain("MCP 회상 응답의 Retrieval Diagnostics로 결과 수, limit, categoryPath, minRelevancePercent, elapsedMs를 확인해 회상 품질을 평가합니다.", llmWikiGuidePage);
        Assert.DoesNotContain("저장 전에는 관련 기억을 먼저 찾고", llmWikiGuidePage);
        Assert.DoesNotContain("암묵지 저장 후보", llmWikiGuidePage);
        Assert.DoesNotContain("저장 전에는 관련 기억을 먼저 회상하고", llmWikiGuidePage);
        Assert.DoesNotContain("categoryPath</code>를 정하고 저장합니다.", llmWikiGuidePage);
        Assert.DoesNotContain("저장하지 않습니다.", llmWikiGuidePage);
        Assert.DoesNotContain("Slogs LLM Wiki가 비공개 기억을 Agent 회상과 소유자 전용 게시전 로그로 이어 두는 연결면입니다.", llmWikiGuidePage);
        Assert.DoesNotContain("비공개 기억을 Agent 회상과 Slogs 게시전 로그로 이어 두는 연결면입니다.", llmWikiGuidePage);
        Assert.DoesNotContain("Slogs LLM Wiki가 비공개 기억을 Agent 회상과 게시전 로그로 이어 쓰는 흐름을 확인합니다.", llmWikiGuidePage);
        Assert.DoesNotContain("기억에서 로그로", llmWikiGuidePage);
        Assert.DoesNotContain("LLM Wiki는 공개 로그 뒤의 기억 계층입니다", llmWikiGuidePage);
        Assert.DoesNotContain("Slogs MCP 키 입력과 전역/프로젝트/현재 세션 범위 선택을 안내하고", llmWikiGuidePage);
        Assert.DoesNotContain("처음 설치할 때만 Agent가 Slogs MCP 키를 요청하고", llmWikiGuidePage);
        Assert.DoesNotContain("최초 설치 시 MCP 키와 적용 범위를 묻도록 안내합니다.", llmWikiGuidePage);
        Assert.DoesNotContain("도구 노출 확인으로 먼저 사용 가능 여부를 확인합니다.", llmWikiGuidePage);
        Assert.DoesNotContain("도구 노출 확인 기능으로 먼저 지연 로딩을 시도", llmWikiGuidePage);
        Assert.DoesNotContain("다시 조회합니다.", llmWikiGuidePage);
        Assert.DoesNotContain("회상 후보 요약 목록", llmWikiGuidePage);
        Assert.DoesNotContain("초기 목록", llmWikiGuidePage);
        Assert.DoesNotContain("작은 회상 후보 목록", llmWikiGuidePage);
        Assert.DoesNotContain("<span>기억 저장</span>", llmWikiGuidePage);
        Assert.DoesNotContain("Wiki 항목으로 저장합니다.", llmWikiGuidePage);
        Assert.DoesNotContain("embedding으로 저장", llmWikiGuidePage);
        Assert.DoesNotContain("graph node로 저장됩니다.", llmWikiGuidePage);
        Assert.DoesNotContain("압축 컨텍스트로 구분합니다.", llmWikiGuidePage);
    }

    [Fact]
    public void SettingsPageFramesConnectionLayerAsKnowledgeLogFlow()
    {
        var settingsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Settings.razor"));
        var settingsComponent = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "LlmWikiMcpSettings.razor"));
        var profileSettingsForm = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "ProfileSettingsForm.razor"));

        Assert.Contains("지식 로그 연결", settingsPage);
        Assert.Contains("슬로거 홈 정체성, Agent 회상, LLM Wiki 기억, Obsidian 노트 Vault, 공개 공유 흐름을 한 연결 계층으로 이어 둡니다.", settingsPage);
        Assert.Contains("슬로거 홈 정체성, Agent 회상, 기억, 로컬 노트, 공개 공유 흐름을 한 연결 계층으로 이어 둡니다.", settingsPage);
        Assert.Contains("기억과 노트가 공개 공유로 이어지는 경로", settingsPage);
        Assert.Contains("Agent는 비공개 기억을 회상하고, Obsidian은 로컬 노트를 원격 노트 Vault에 남기며", settingsPage);
        Assert.Contains("data-settings-connection-status", settingsPage);
        Assert.Contains("노트 Vault 단서", settingsPage);
        Assert.Contains("소유자 전용 게시전 기억", settingsPage);
        Assert.Contains("비공개 기억을 회상해 소유자 전용 게시전 기억으로 이어 둡니다.", settingsPage);
        Assert.Contains("판단 기준과 작업 맥락을 찾아 게시전 기억으로 이어 씁니다.", settingsPage);
        Assert.Contains("frontmatter 단서를 게시전 기억과 LLM Wiki 기억으로 이어 둡니다.", settingsPage);
        Assert.Contains("게시전 기억 정리", settingsPage);
        Assert.Contains("검토된 기억과 노트를 게시전 기억으로 정리하고 필요할 때 공개 공유합니다.", settingsPage);
        Assert.Contains("이 슬로거의 비공개 기억을 회상하고", settingsComponent);
        Assert.Contains("검토 가능한 소유자 전용 게시전 기억을 만들고", settingsComponent);
        Assert.Contains("data-agent-mcp-flow-status", settingsComponent);
        Assert.Contains("<span>소유자 전용 게시전 기억</span>", settingsComponent);
        Assert.Contains("frontmatter를 통해 게시전 기억, 공개 공유, LLM Wiki 기억으로 이어질 수 있습니다.", settingsComponent);
        Assert.Contains("<span>게시전 기억</span>", settingsComponent);
        Assert.Contains("공개 공유는 명시 후", settingsComponent);
        Assert.Contains("Slogs MCP 연결 주소", settingsComponent);
        Assert.Contains("Agent 회상 권한 헤더", settingsComponent);
        Assert.Contains("Agent 연결 설정 예시", settingsComponent);
        Assert.Contains("Agent는 Agent 회상 권한으로 이 슬로거의 비공개 기억을 회상하고", settingsComponent);
        Assert.Contains("노트 Vault 플러그인 ID", settingsComponent);
        Assert.Contains("Slogs Drive 설치 흐름", settingsComponent);
        Assert.Contains("Slogs Drive 실행 흐름", settingsComponent);
        Assert.Contains("Slogs Drive 설치 흐름이 복사되었습니다.", settingsComponent);
        Assert.Contains("Slogs Drive 실행 흐름이 복사되었습니다.", settingsComponent);
        Assert.Contains("로컬 Markdown 노트는 노트 Vault 권한으로 원격 노트 Vault에 남고", settingsComponent);
        Assert.Contains("data-obsidian-flow-status", settingsComponent);
        Assert.Contains("frontmatter 단서", settingsComponent);
        Assert.Contains("연결 권한 만들기", settingsComponent);
        Assert.Contains("새 연결 권한", settingsComponent);
        Assert.Contains("권한 복사", settingsComponent);
        Assert.Contains("Agent 회상 권한", settingsComponent);
        Assert.Contains("노트 Vault 권한", settingsComponent);
        Assert.Contains("권한 끊기", settingsComponent);
        Assert.Contains("최근 연결", settingsComponent);
        Assert.Contains("슬로거 홈 정체성", profileSettingsForm);
        Assert.Contains("공개 지식 로그 홈에 보일 이름, 이미지, 짧은 흐름 소개를 정리합니다.", profileSettingsForm);
        Assert.Contains("슬로거 홈 &#64;주소", profileSettingsForm);
        Assert.Contains("슬로거 홈 이미지 URL", profileSettingsForm);
        Assert.Contains("홈 소개", profileSettingsForm);
        Assert.Contains("홈 정체성 저장", profileSettingsForm);
        Assert.Contains("슬로거 홈 정체성이 저장되었습니다.", profileSettingsForm);

        Assert.DoesNotContain("공개 로그 연결을 설정합니다.", settingsPage);
        Assert.DoesNotContain("공개 로그 흐름을 관리합니다.", settingsPage);
        Assert.DoesNotContain("슬로거 홈 정체성, Agent 회상, LLM Wiki 기억, Obsidian 노트 Vault, 공개 로그 흐름을 한 연결 계층으로 이어 둡니다.", settingsPage);
        Assert.DoesNotContain("슬로거 홈 정체성, Agent 회상, 기억, 로컬 노트, 공개 로그 흐름을 한 연결 계층으로 이어 둡니다.", settingsPage);
        Assert.DoesNotContain("Slogs 계정", settingsPage);
        Assert.DoesNotContain("프로필, Agent", settingsPage);
        Assert.DoesNotContain("기억과 노트가 로그로 이어지는 경로", settingsPage);
        Assert.DoesNotContain(">Slogs 로그</span>", settingsPage);
        Assert.DoesNotContain("검토된 기억과 노트를 리비전으로 공개 공유합니다.", settingsPage);
        Assert.DoesNotContain("판단 기준과 작업 맥락을 찾습니다.", settingsPage);
        Assert.DoesNotContain("로컬 Markdown을 원격 노트 Vault에 남깁니다.", settingsPage);
        Assert.DoesNotContain("게시전 검토 후 리비전으로 공유합니다.", settingsPage);
        Assert.DoesNotContain("이 계정의 비공개 기억", settingsComponent);
        Assert.DoesNotContain("소유자 전용 게시전 로그", settingsPage);
        Assert.DoesNotContain("비공개 기억을 회상해 소유자 전용 게시전 로그로 이어 둡니다.", settingsPage);
        Assert.DoesNotContain("판단 기준과 작업 맥락을 찾아 게시전 로그로 이어 씁니다.", settingsPage);
        Assert.DoesNotContain("frontmatter 단서를 게시전 로그와 LLM Wiki 기억으로 이어 둡니다.", settingsPage);
        Assert.DoesNotContain("검토 가능한 소유자 전용 게시전 로그를 만들고", settingsComponent);
        Assert.DoesNotContain("frontmatter를 통해 게시전 로그, 공개 공유, LLM Wiki 기억으로 이어질 수 있습니다.", settingsComponent);
        Assert.DoesNotContain("<span>게시전 로그</span>", settingsComponent);
        Assert.DoesNotContain("게시전 로그 초안", settingsPage);
        Assert.DoesNotContain("게시전 로그 초안", settingsComponent);
        Assert.DoesNotContain("프로필 설정", profileSettingsForm);
        Assert.DoesNotContain("프로필 저장", profileSettingsForm);
        Assert.DoesNotContain("프로필 저장에 실패했습니다.", profileSettingsForm);
        Assert.DoesNotContain("프로필 이미지 URL", profileSettingsForm);
        Assert.DoesNotContain(">공개 주소", profileSettingsForm);
        Assert.DoesNotContain(">Endpoint</p>", settingsComponent);
        Assert.DoesNotContain(">Authorization Header</p>", settingsComponent);
        Assert.DoesNotContain(">Client Config Example</p>", settingsComponent);
        Assert.DoesNotContain(">Plugin ID</p>", settingsComponent);
        Assert.DoesNotContain(">Drive install</p>", settingsComponent);
        Assert.DoesNotContain(">Drive run</p>", settingsComponent);
        Assert.DoesNotContain("Drive 설치 명령", settingsComponent);
        Assert.DoesNotContain("Drive 실행 명령", settingsComponent);
        Assert.DoesNotContain(">폐기</button>", settingsComponent);
        Assert.DoesNotContain("마지막 사용", settingsComponent);
        Assert.DoesNotContain("`mcp` scope 토큰", settingsComponent);
        Assert.DoesNotContain("`obsidian.sync` scope 토큰", settingsComponent);
        Assert.DoesNotContain("연결 토큰 생성", settingsComponent);
        Assert.DoesNotContain("새 연결 토큰", settingsComponent);
        Assert.DoesNotContain(">복사</button>", settingsComponent);
    }

    [Fact]
    public void ObsidianVaultCardsUseNoteFlowAndConnectedDeviceWording()
    {
        var settingsComponent = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "LlmWikiMcpSettings.razor"));

        Assert.Contains("노트 흐름 v{vault.CurrentVersion}", settingsComponent);
        Assert.Contains("이어진 노트 @status.ActiveFileCount", settingsComponent);
        Assert.Contains("지운 노트 흔적 @status.DeletedFileCount", settingsComponent);
        Assert.Contains("Vault 지우기", settingsComponent);
        Assert.Contains("노트 원문, 지운 노트 흔적, 연결 흔적, 노트 버전 흐름", settingsComponent);
        Assert.Contains("지우려면", settingsComponent);
        Assert.Contains("완전히 지우기", settingsComponent);
        Assert.Contains("지우는 중...", settingsComponent);
        Assert.Contains("연결 흔적 {client.ClientKind}", settingsComponent);
        Assert.Contains("노트 흐름 v{client.LastSeenVersion}", settingsComponent);

        Assert.DoesNotContain(">v@vault.CurrentVersion ·", settingsComponent);
        Assert.DoesNotContain("노트 흐름 v@vault.CurrentVersion", settingsComponent);
        Assert.DoesNotContain(">활성 @status.ActiveFileCount", settingsComponent);
        Assert.DoesNotContain("활성 노트 @status.ActiveFileCount", settingsComponent);
        Assert.DoesNotContain(">삭제 기록 @status.DeletedFileCount", settingsComponent);
        Assert.DoesNotContain("삭제 흔적 @status.DeletedFileCount", settingsComponent);
        Assert.DoesNotContain("파일, 삭제 기록, 클라이언트 상태, 버전 이력", settingsComponent);
        Assert.DoesNotContain("노트 원문, 삭제 흔적, 연결 기기 상태, 노트 버전 이력", settingsComponent);
        Assert.DoesNotContain("@client.ClientKind · v@client.LastSeenVersion", settingsComponent);
        Assert.DoesNotContain("노트 흐름 v@client.LastSeenVersion", settingsComponent);
        Assert.DoesNotContain(">삭제</button>", settingsComponent);
        Assert.DoesNotContain("삭제하려면", settingsComponent);
        Assert.DoesNotContain("완전 삭제", settingsComponent);
        Assert.DoesNotContain("삭제 중...", settingsComponent);
    }

    [Fact]
    public void AdminObsidianMetricsUseNoteVaultAndConnectionTraceWording()
    {
        var adminUsersPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "AdminUsers.razor"));

        Assert.Contains("노트 Vault 흐름 요약", adminUsersPage);
        Assert.Contains("노트 Vault 슬로거", adminUsersPage);
        Assert.Contains("노트 Vault", adminUsersPage);
        Assert.Contains("이어진 노트", adminUsersPage);
        Assert.Contains("지운 노트 흔적", adminUsersPage);
        Assert.Contains("연결 흔적", adminUsersPage);
        Assert.Contains("노트 용량 흐름", adminUsersPage);
        Assert.Contains("aria-label=\"노트 Vault 용량 흐름\"", adminUsersPage);
        Assert.Contains(">노트 Vault 용량 흐름</h2>", adminUsersPage);
        Assert.Contains("슬로거 홈당 {FormatBytes(usage.ObsidianPerAccountStorageLimitBytes)} · 전체 Vault 흐름 {FormatBytes(usage.ObsidianTotalStorageCapacityBytes)}", adminUsersPage);
        Assert.Contains(">노트 Vault 흐름 한도 GiB</label>", adminUsersPage);
        Assert.Contains("aria-label=\"노트 Vault 흐름 한도 GiB\"", adminUsersPage);
        Assert.Contains("흐름 한도 반영 중", adminUsersPage);
        Assert.Contains("흐름 한도 반영", adminUsersPage);
        Assert.Contains("노트 Vault 흐름 한도를 {FormatBytes(capacityBytes)}로 반영했습니다.", adminUsersPage);
        Assert.Contains("반영된 노트 Vault 흐름 한도 값이 올바르지 않습니다.", adminUsersPage);
        Assert.Contains("노트 Vault 흐름 한도 반영에 실패했습니다.", adminUsersPage);
        Assert.Contains(">Vault 사용</div>", adminUsersPage);
        Assert.Contains(">Vault 여유</div>", adminUsersPage);
        Assert.Contains(">물리 여유</div>", adminUsersPage);
        Assert.Contains(">전체 흐름 사용률</div>", adminUsersPage);
        Assert.Contains("노트 Vault 슬로거만", adminUsersPage);
        Assert.Contains("placeholder=\"노트 Vault 슬로거 단서\"", adminUsersPage);
        Assert.Contains("aria-label=\"노트 Vault 슬로거 단서 입력\"", adminUsersPage);
        Assert.Contains("aria-label=\"노트 Vault 흐름 정렬\"", adminUsersPage);
        Assert.Contains("<option value=\"updated\">최근 노트 흐름순</option>", adminUsersPage);
        Assert.Contains("<option value=\"vaults\">노트 Vault순</option>", adminUsersPage);
        Assert.Contains("<option value=\"files\">노트 원문순</option>", adminUsersPage);
        Assert.Contains("<option value=\"clients\">연결 흔적순</option>", adminUsersPage);
        Assert.Contains("<option value=\"size\">노트 용량순</option>", adminUsersPage);
        Assert.Contains("<option value=\"name\">@@name순</option>", adminUsersPage);
        Assert.Contains("명 노트 흐름 표시", adminUsersPage);
        Assert.Contains(">노트 원문</th>", adminUsersPage);
        Assert.Contains(">노트 흐름</th>", adminUsersPage);
        Assert.Contains(">Vault 흐름 한도</th>", adminUsersPage);
        Assert.Contains(">Vault 여유</th>", adminUsersPage);
        Assert.Contains(">최근 Vault 흐름</th>", adminUsersPage);
        Assert.Contains(">최근 연결 흔적</th>", adminUsersPage);
        Assert.Contains("이어 볼 노트 Vault 슬로거가 없습니다.", adminUsersPage);
        Assert.Contains("로컬 노트 Vault, 노트 원문, 연결 흔적 흐름을 함께 따라갑니다.", adminUsersPage);

        Assert.DoesNotContain(">Sync 사용자</div>", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 사용자", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 사용자만", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 슬로거", adminUsersPage);
        Assert.DoesNotContain(">Vault</div>", adminUsersPage);
        Assert.DoesNotContain("계정당 {FormatBytes(usage.ObsidianPerAccountStorageLimitBytes)}", adminUsersPage);
        Assert.DoesNotContain(">활성 파일</div>", adminUsersPage);
        Assert.DoesNotContain(">활성 노트</div>", adminUsersPage);
        Assert.DoesNotContain(">삭제 기록</div>", adminUsersPage);
        Assert.DoesNotContain(">삭제 흔적</div>", adminUsersPage);
        Assert.DoesNotContain(">클라이언트</div>", adminUsersPage);
        Assert.DoesNotContain(">연결 기기</div>", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync 노트 흐름 요약", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync 노트 Vault 용량 한도", adminUsersPage);
        Assert.DoesNotContain("노트 Vault 흐름 한도를 {FormatBytes(capacityBytes)}로 저장했습니다.", adminUsersPage);
        Assert.DoesNotContain("저장된 노트 Vault 용량 설정 값이 올바르지 않습니다.", adminUsersPage);
        Assert.DoesNotContain("노트 Vault 용량 설정 저장에 실패했습니다.", adminUsersPage);
        Assert.DoesNotContain("@(isStorageSettingsBusy ? \"저장 중\" : \"저장\")", adminUsersPage);
        Assert.DoesNotContain(">스토리지 한도</h2>", adminUsersPage);
        Assert.DoesNotContain(">전체 한도 GiB</label>", adminUsersPage);
        Assert.DoesNotContain(">전체 Vault 한도 GiB</label>", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"노트 Vault 전체 용량 한도 GiB\"", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"Obsidian Sync 전체 스토리지 한도 GiB\"", adminUsersPage);
        Assert.DoesNotContain(">사용량</div>", adminUsersPage);
        Assert.DoesNotContain(">한도 남은 용량</div>", adminUsersPage);
        Assert.DoesNotContain(">물리 남은 용량</div>", adminUsersPage);
        Assert.DoesNotContain(">전체 사용률</div>", adminUsersPage);
        Assert.DoesNotContain("placeholder=\"Obsidian Sync 슬로거 단서\"", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"Obsidian Sync 슬로거 단서 입력\"", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"Obsidian Sync 정렬\"", adminUsersPage);
        Assert.DoesNotContain("<option value=\"updated\">최근 동기화순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"vaults\">Vault순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"files\">파일순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"clients\">클라이언트순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"clients\">연결 기기순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"size\">용량순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"name\">아이디순</option>", adminUsersPage);
        Assert.DoesNotContain(">Vault</th>", adminUsersPage);
        Assert.DoesNotContain(">파일</th>", adminUsersPage);
        Assert.DoesNotContain(">활성</th>", adminUsersPage);
        Assert.DoesNotContain(">활성 노트</th>", adminUsersPage);
        Assert.DoesNotContain(">삭제</th>", adminUsersPage);
        Assert.DoesNotContain(">삭제 흔적</th>", adminUsersPage);
        Assert.DoesNotContain(">클라이언트</th>", adminUsersPage);
        Assert.DoesNotContain(">연결 기기</th>", adminUsersPage);
        Assert.DoesNotContain(">Version</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 Vault 변경</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 노트 Vault</th>", adminUsersPage);
        Assert.DoesNotContain(">Vault 한도</th>", adminUsersPage);
        Assert.DoesNotContain("전체 노트 Vault 한도", adminUsersPage);
        Assert.DoesNotContain(">최근 클라이언트</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 연결 기기</th>", adminUsersPage);
        Assert.DoesNotContain("표시할 Obsidian Sync 사용자가 없습니다.", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync vault, 파일, 클라이언트 현황", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync 노트 Vault, 노트 원문, 연결 기기 흐름을 확인합니다.", adminUsersPage);
        Assert.DoesNotContain("로컬 노트 Vault, 노트 원문, 연결 기기 흐름을 확인합니다.", adminUsersPage);
        Assert.DoesNotContain("로컬 노트 Vault, 노트 원문, 연결 흔적 흐름을 확인합니다.", adminUsersPage);
    }

    [Fact]
    public void AuthoringAddressDefaultsUseLogAndClueWording()
    {
        var writePost = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WritePost.razor"));
        var editPost = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "EditPost.razor"));

        Assert.Equal("log", SlugGenerator.Normalize(null));
        Assert.Equal("log", SlugGenerator.Normalize(string.Empty));
        Assert.Equal("log", SlugGenerator.Normalize("---"));

        foreach (var authoringPage in new[] { writePost, editPost })
        {
            Assert.Contains("공유 주소 단서", authoringPage);
            Assert.Contains("제목 단서로 주소 잇기", authoringPage);
            Assert.DoesNotContain("공유 주소 slug", authoringPage);
            Assert.DoesNotContain("제목으로 주소 추천", authoringPage);
            Assert.DoesNotContain("제목으로 단서 추천", authoringPage);
        }
    }

    [Fact]
    public void AuthoringDraftFlowUsesPrePublishAndShareLanguage()
    {
        var writePost = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WritePost.razor"));
        var editPost = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "EditPost.razor"));
        var representativeImageField = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "RepresentativeImageField.razor"));

        Assert.Contains("<PageTitle>게시전 기억 남기기</PageTitle>", writePost);
        Assert.Contains("Title=\"게시전 기억 남기기 | slogs\"", writePost);
        Assert.Contains("생각, 작업, 판단, 검증 흐름을 게시전 기억으로 남기고 필요할 때 공개 공유합니다.", writePost);
        Assert.Contains(">게시전 기억 남기기</h1>", writePost);
        Assert.Contains("게시전 기억 저장", writePost);
        Assert.Contains("게시전 기억으로 남길 로그 제목이나 본문을 입력해 주세요.", writePost);
        Assert.Contains("공개 공유에는 로그 제목과 본문 흐름이 필요합니다.", writePost);
        Assert.Contains("공개 공유 중...", writePost);
        Assert.Contains("공개 공유", writePost);
        Assert.Contains("SaveDraft", writePost);
        Assert.Contains("SaveAsync(isDraft: true)", writePost);
        Assert.Contains("SaveAsync(isDraft: false)", writePost);
        Assert.Contains("isDraft: isDraft", writePost);

        Assert.Contains("로그 흐름 정리", editPost);
        Assert.Contains("slogs 로그 흐름을 게시전 기억 정리와 리비전 공유로 이어갑니다.", editPost);
        Assert.Contains("게시전 기억 정리", editPost);
        Assert.Contains("리비전 공유 정리", editPost);
        Assert.Contains(">게시전 기억</span>", editPost);
        Assert.Contains("게시전 기억 저장", editPost);
        Assert.Contains("정리할 수 있는 로그 흐름 노드를 찾지 못했습니다.", editPost);
        Assert.Contains("이 로그 흐름을 정리할 수 있는 연결 권한이 없습니다.", editPost);
        Assert.Contains("공개 공유에는 로그 제목과 본문 흐름이 필요합니다.", editPost);
        Assert.Contains("리비전 공유에는 로그 제목과 본문 흐름이 필요합니다.", editPost);
        Assert.Contains("게시전 기억을 정리할 연결 권한이 없거나 로그 흐름이 존재하지 않습니다.", editPost);
        Assert.Contains("리비전을 공유할 연결 권한이 없거나 로그 흐름이 존재하지 않습니다.", editPost);
        Assert.Contains("공개 공유 중...", editPost);
        Assert.Contains("공개 공유", editPost);
        Assert.Contains("리비전 공유", editPost);
        Assert.Contains("SaveAsync(isDraft: true)", editPost);
        Assert.Contains("SaveAsync(isDraft: false)", editPost);
        Assert.Contains("post.IsDraft ? CurrentSlug : null", editPost);
        Assert.Contains("post.IsDraft ? $\"/edit/{Uri.EscapeDataString(post.Slug)}\" : GetPostUrl(post)", editPost);

        Assert.Contains("업로드한 이미지는 로그 본문 Markdown에도 포함되어야 로그 흐름에 남습니다.", representativeImageField);
        Assert.Contains("로그 흐름에 남기려면 본문 Markdown에도 이 경로를 넣어 주세요.", representativeImageField);
        Assert.DoesNotContain("업로드한 이미지는 로그 본문 Markdown에도 포함되어야 저장됩니다.", representativeImageField);
        Assert.DoesNotContain("저장하려면 로그 본문 Markdown에도 이 경로를 넣어 주세요.", representativeImageField);

        foreach (var authoringPage in new[] { writePost, editPost })
        {
            Assert.DoesNotContain("임시저장", authoringPage);
            Assert.DoesNotContain("게시하기", authoringPage);
            Assert.DoesNotContain(">게시전</span>", authoringPage);
            Assert.DoesNotContain(">게시후</span>", authoringPage);
            Assert.DoesNotContain("게시전 저장", authoringPage);
            Assert.DoesNotContain("게시전 로그 수정", authoringPage);
            Assert.DoesNotContain("로그 수정 | slogs", authoringPage);
            Assert.DoesNotContain("slogs에 새 로그를 남깁니다.", authoringPage);
            Assert.DoesNotContain(">새 로그 남기기</h1>", authoringPage);
            Assert.DoesNotContain("게시전 기억이나 새 리비전", authoringPage);
            Assert.DoesNotContain(">리비전 정리</h1>", authoringPage);
            Assert.DoesNotContain("수정할 로그", authoringPage);
            Assert.DoesNotContain("정리할 로그 흐름을 찾지 못했습니다.", authoringPage);
            Assert.DoesNotContain(">권한이 없습니다.</p>", authoringPage);
            Assert.DoesNotContain("게시전 로그 저장에 실패", authoringPage);
            Assert.DoesNotContain("? \"공유 중...\" : \"공개 공유\"", authoringPage);
            Assert.DoesNotContain("로그 제목과 본문은 필수입니다.", authoringPage);
            Assert.DoesNotContain("수정 권한이 없거나 로그가 존재하지 않습니다.", authoringPage);
            Assert.DoesNotContain("리비전을 공유할 권한이 없거나 로그가 존재하지 않습니다.", authoringPage);
            Assert.DoesNotContain("포스트", authoringPage);
        }
    }

    [Fact]
    public void HeaderRecallCssKeepsMediumAndSmallWidthsOnOneRow()
    {
        var appCss = File.ReadAllText(FindRepoFile("src", "Slogs", "wwwroot", "app.css"));
        var mainLayout = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Layout", "MainLayout.razor"));

        Assert.Contains("class=\"slogs-header-tools\"", mainLayout);
        Assert.Contains("class=\"slogs-account-menu relative\"", mainLayout);
        Assert.Contains(".slogs-app-header__inner {\n    display: flex;", appCss);
        Assert.Contains("flex-wrap: nowrap;", appCss);
        Assert.Contains("justify-content: space-between;", appCss);
        Assert.Contains("flex: 0 1 auto;", appCss);
        Assert.Contains(".slogs-header-tools {\n    display: flex;", appCss);
        Assert.Contains("flex: 1 1 auto;", appCss);
        Assert.Contains("width: auto;", appCss);
        Assert.Contains("align-self: center;", appCss);
        Assert.Contains("justify-self: end;", appCss);
        Assert.Contains(".slogs-header-tools", appCss);
        Assert.Contains("display: flex;", appCss);
        Assert.Contains("flex-wrap: nowrap;", appCss);
        Assert.Contains("max-width: 100%;", appCss);
        Assert.Contains(".slogs-account-menu > summary", appCss);
        Assert.Contains("max-width: min(17rem, 34vw);", appCss);
        Assert.Contains("overflow: hidden;", appCss);
        Assert.Contains("min-width: 0;", appCss);
        Assert.Contains("max-width: 4.85rem;", appCss);
        Assert.Contains("max-width: 4.25rem;", appCss);
        Assert.Contains("max-width: min(68rem, 100%);", appCss);
        Assert.Contains("flex: 1 1 clamp(24rem, 44vw, 58rem);", appCss);
        Assert.Contains("min-width: min(22rem, 42vw);", appCss);
        Assert.Contains("flex-basis: clamp(20rem, 42vw, 52rem);", appCss);
        Assert.Contains("min-width: min(18rem, 36vw);", appCss);
        Assert.Contains("@media (max-width: 1500px)", appCss);
        Assert.Contains("max-width: min(8rem, 22vw);", appCss);
        Assert.Contains("@media (max-width: 1180px) {\n    .slogs-brand__tagline {\n        display: none;\n    }\n}", appCss);
        Assert.Contains("@media (max-width: 900px)", appCss);
        Assert.Contains(".slogs-brand__tagline {\n        display: none;\n    }", appCss);
        Assert.Contains("flex-basis: auto;", appCss);
        Assert.Contains("min-width: 8rem;", appCss);
        Assert.Contains("min-width: 7rem;", appCss);
        Assert.Contains("min-width: 6rem;", appCss);
        Assert.DoesNotContain("grid-template-areas: \"brand tools\";", appCss);
        Assert.DoesNotContain("grid-template-areas: \"brand recall actions\";", appCss);
        Assert.DoesNotContain("grid-template-columns: minmax(0, max-content) minmax(0, 1fr);", appCss);
        Assert.DoesNotContain("grid-template-columns: minmax(0, max-content) minmax(12rem, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-template-columns: max-content minmax(0, 1fr);", appCss);
        Assert.DoesNotContain("grid-template-columns: max-content minmax(0, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-template-columns: minmax(11rem, max-content) minmax(14rem, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-template-columns: max-content minmax(16rem, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-template-columns: max-content minmax(12rem, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-template-columns: max-content minmax(10rem, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-area: brand;", appCss);
        Assert.DoesNotContain("grid-area: tools;", appCss);
        Assert.DoesNotContain("grid-area: recall;", appCss);
        Assert.DoesNotContain("grid-area: actions;", appCss);
        Assert.DoesNotContain("grid-auto-flow: column;", appCss);
        Assert.DoesNotContain("grid-auto-rows: minmax(0, auto);", appCss);
        Assert.Contains("max-width: 58rem;", appCss);
        Assert.Contains("max-width: 52rem;", appCss);
        Assert.DoesNotContain("@media (max-width: 390px)", appCss);
        Assert.Contains("@media (max-width: 380px)", appCss);
        Assert.Contains("width: 2.2rem;", appCss);
        Assert.Contains("@media (max-width: 360px)", appCss);
        Assert.Contains(".slogs-brand__text {\n        display: none;\n    }", appCss);
        Assert.Contains("@media (max-width: 300px)", appCss);
        Assert.DoesNotContain("@media (max-width: 340px)", appCss);
        Assert.DoesNotContain("grid-template-areas: \"brand actions\" \"recall recall\";", appCss);
        Assert.DoesNotContain("grid-template-areas: \"brand tools\" \"recall recall\";", appCss);
        Assert.DoesNotContain("\"recall recall\";", appCss);
        Assert.DoesNotContain("display: contents;", appCss);
        Assert.Contains("min-width: 5rem;", appCss);
        Assert.Contains("width: 1.75rem;", appCss);
    }

    [Fact]
    public void PersonalWorkspaceCardsUseLogFlowSignals()
    {
        var program = File.ReadAllText(FindRepoFile("src", "Slogs", "Program.cs"));
        var profilePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Profile.razor"));
        var postDetailsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "PostDetails.razor"));

        Assert.Contains("공개 공유 노드, 게시전 기억, 노트 Vault 흐름이 모이는 슬로거 홈", program);
        Assert.DoesNotContain("공개 로그, 게시전 기억, 노트 Vault 흐름이 모이는 슬로거 홈", program);
        Assert.DoesNotContain("글과 프로필", program);

        Assert.Contains("내 지식 로그 흐름", profilePage);
        Assert.Contains("공개 공유 로그와 소유자 전용 게시전 기억 흐름을 이어 봅니다.", profilePage);
        Assert.Contains("생각, 작업, 판단, 검증을 먼저 게시전 기억으로 남긴 뒤 필요할 때 공개 공유하세요.", profilePage);
        Assert.Contains("@($\"{totalCount}개 로그 노드\")", profilePage);
        Assert.Contains(">게시전 기억</span>", profilePage);
        Assert.Contains("흐름 갱신 @FormatDate(post.UpdatedAt)", profilePage);
        Assert.Contains(">공개 공유</span>", profilePage);
        Assert.Contains("게시전 기억 흐름 열기", profilePage);
        Assert.Contains("공개 공유 로그 노드 열기", profilePage);
        Assert.Contains("<PostFlowSignals Post=\"post\" />", profilePage);
        Assert.Contains("게시전 기억 정리", profilePage);
        Assert.Contains("새 리비전 남기기", profilePage);
        Assert.Contains("로그 지우기", profilePage);
        Assert.Contains("로그 지우는 중...", profilePage);
        Assert.Contains("이 로그 흐름을 지울까요?", profilePage);
        Assert.Contains("로그 지우기 진행", profilePage);
        Assert.Contains("지울 로그를 찾을 수 없습니다.", profilePage);
        Assert.Contains("로그를 지우지 못했습니다.", profilePage);
        Assert.Contains("로그가 지워졌습니다.", profilePage);
        Assert.Contains("로그 지우기 요청 중 오류가 발생했습니다", profilePage);
        Assert.Contains("로그 지우기", postDetailsPage);
        Assert.Contains("로그 지우는 중...", postDetailsPage);
        Assert.Contains("이 로그 흐름을 지울까요?", postDetailsPage);
        Assert.Contains("로그 지우기 진행", postDetailsPage);
        Assert.Contains("로그를 지울 권한이 없습니다.", postDetailsPage);
        Assert.Contains("로그를 지우지 못했습니다.", postDetailsPage);
        Assert.Contains("로그 지우기 요청 중 오류가 발생했습니다", postDetailsPage);
        Assert.DoesNotContain("게시전 초안", profilePage);
        Assert.DoesNotContain("\"삭제 중...\" : \"삭제\"", profilePage);
        Assert.DoesNotContain("이 로그를 삭제하시겠습니까?", profilePage);
        Assert.DoesNotContain("삭제 진행", profilePage);
        Assert.DoesNotContain("삭제할 로그를 찾을 수 없습니다.", profilePage);
        Assert.DoesNotContain("삭제에 실패했습니다.", profilePage);
        Assert.DoesNotContain("로그가 삭제되었습니다.", profilePage);
        Assert.DoesNotContain("삭제 요청 중 오류가 발생했습니다", profilePage);
        Assert.DoesNotContain("내 공개 로그", profilePage);
        Assert.DoesNotContain(">게시전</span>", profilePage);
        Assert.DoesNotContain(">게시후</span>", profilePage);
        Assert.DoesNotContain("수정 @FormatDate(post.UpdatedAt)", profilePage);
        Assert.DoesNotContain("게시전 로그로 남긴 뒤", profilePage);
        Assert.DoesNotContain("\"삭제 중...\" : \"삭제\"", postDetailsPage);
        Assert.DoesNotContain("이 로그를 정말 삭제하시겠습니까?", postDetailsPage);
        Assert.DoesNotContain("삭제 진행", postDetailsPage);
        Assert.DoesNotContain("삭제 권한이 없습니다.", postDetailsPage);
        Assert.DoesNotContain("삭제에 실패했습니다.", postDetailsPage);
        Assert.DoesNotContain("삭제 요청 중 오류가 발생했습니다", postDetailsPage);
        Assert.DoesNotContain("SlogsIcon Name=\"heart\"", profilePage);
        Assert.DoesNotContain("SlogsIcon Name=\"message-circle\"", profilePage);
        Assert.DoesNotContain("로그 시리즈: @series", profilePage);
        Assert.DoesNotContain("FormatUserName", profilePage);
    }

    [Fact]
    public void PublicLogViewCountsUseRecallAccessWording()
    {
        var postMetaLine = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "PostMetaLine.razor"));
        var homePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Home.razor"));
        var postDetailsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "PostDetails.razor"));
        var profilePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Profile.razor"));
        var writerPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WriterPage.razor"));

        Assert.Contains("FormatRecallAccessCount(Post.ViewCount)", postMetaLine);
        Assert.Contains("title=\"회상 접근\"", postMetaLine);
        Assert.Contains("회상 접근, 대화 흔적, 공감 신호로 다시 이어진 공개 로그 흐름을 따라갑니다.", homePage);
        Assert.Contains("FormatRecallAccessCount(post.ViewCount)", postDetailsPage);
        Assert.Contains("FormatRecallAccessCount(post.ViewCount)", profilePage);
        Assert.Contains("회상 접근", writerPage);

        Assert.DoesNotContain("<span>@Post.ViewCount</span>", postMetaLine);
        Assert.DoesNotContain("<span>@post.ViewCount</span>", postDetailsPage);
        Assert.DoesNotContain("<span>@post.ViewCount</span>", profilePage);
        Assert.DoesNotContain("조회, 대화 흔적, 공감", homePage);
        Assert.DoesNotContain("회상 접근, 대화 흔적, 공감으로", homePage);
        Assert.DoesNotContain("회상 진입", writerPage);
    }

    [Fact]
    public void PublicLogActionsUseResonanceAndSavedRecallLanguage()
    {
        var postActionBar = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "PostActionBar.razor"));
        var postDetailsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "PostDetails.razor"));

        Assert.Contains("title=\"공감 신호\"", postActionBar);
        Assert.Contains("<span class=\"sr-only\">공감 신호</span>", postActionBar);
        Assert.Contains("공감 신호 남기기, 현재 {Post.LikeCount}개", postActionBar);
        Assert.Contains("공감 신호 해제, 현재 {Post.LikeCount}개", postActionBar);
        Assert.Contains("title=\"저장 회상\"", postActionBar);
        Assert.Contains("<span class=\"sr-only\">저장 회상</span>", postActionBar);
        Assert.Contains("저장 회상에 추가", postActionBar);
        Assert.Contains("저장 회상 해제", postActionBar);

        Assert.Contains("공감 신호를 남기려면 지식 로그 홈으로 돌아가야 합니다.", postDetailsPage);
        Assert.Contains("게시전 기억은 공개 공유 후 공감 신호를 남길 수 있습니다.", postDetailsPage);
        Assert.Contains("공감 신호가 처리 중입니다.", postDetailsPage);
        Assert.Contains("공감 신호 흐름에서 해제되었습니다.", postDetailsPage);
        Assert.Contains("공감 신호 흐름에 추가되었습니다.", postDetailsPage);
        Assert.Contains("저장 회상 흐름을 바꾸려면 지식 로그 홈으로 돌아가야 합니다.", postDetailsPage);
        Assert.Contains("게시전 기억은 공개 공유 후 저장 회상에 추가할 수 있습니다.", postDetailsPage);
        Assert.Contains("저장 회상이 처리 중입니다.", postDetailsPage);
        Assert.Contains("저장 회상 흐름에서 해제되었습니다.", postDetailsPage);
        Assert.Contains("저장 회상 흐름에 추가되었습니다.", postDetailsPage);

        Assert.DoesNotContain("title=\"공감\"", postActionBar);
        Assert.DoesNotContain("<span class=\"sr-only\">공감</span>", postActionBar);
        Assert.DoesNotContain("공감 취소", postActionBar);
        Assert.DoesNotContain("=> IsBookmarked ? \"저장 해제\" : \"저장\"", postActionBar);
        Assert.DoesNotContain("title=\"저장\"", postActionBar);
        Assert.DoesNotContain("<span class=\"sr-only\">저장</span>", postActionBar);
        Assert.DoesNotContain("공감 신호는 로그인 후 남길 수 있습니다.", postDetailsPage);
        Assert.DoesNotContain("공감은 로그인 후 이용 가능합니다.", postDetailsPage);
        Assert.DoesNotContain("공감이 취소되었습니다.", postDetailsPage);
        Assert.DoesNotContain("공감이 추가되었습니다.", postDetailsPage);
        Assert.DoesNotContain("저장 회상은 로그인 후 사용할 수 있습니다.", postDetailsPage);
        Assert.DoesNotContain("저장은 로그인 후 이용 가능합니다.", postDetailsPage);
        Assert.DoesNotContain("게시전 로그는 공개 후 공감 신호를 남길 수 있습니다.", postDetailsPage);
        Assert.DoesNotContain("게시전 로그는 공개 후 저장 회상에 추가할 수 있습니다.", postDetailsPage);
        Assert.DoesNotContain("저장이 해제되었습니다.", postDetailsPage);
        Assert.DoesNotContain("저장되었습니다.", postDetailsPage);
    }

    [Fact]
    public void PublicSloggerDiscoveryUsesLogHomeFlowSignals()
    {
        var writerIndexPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WriterIndex.razor"));

        Assert.Contains("<PageTitle>슬로거 홈 흐름</PageTitle>", writerIndexPage);
        Assert.Contains("Title=\"슬로거 홈 흐름 | slogs\"", writerIndexPage);
        Assert.Contains("로그 홈 흐름을 회상합니다.", writerIndexPage);
        Assert.Contains(">슬로거 홈 흐름</h1>", writerIndexPage);
        Assert.Contains(">로그 흐름순</a>", writerIndexPage);
        Assert.Contains(">@@주소순</a>", writerIndexPage);
        Assert.Contains("@($\"{item.Count}개 로그\")", writerIndexPage);
        Assert.Contains("GetSloggerHomeRecallPath(item)", writerIndexPage);
        Assert.Contains("슬로거 홈 회상 경로: @{item.Author} -> 공개 로그 흐름", writerIndexPage);
        Assert.Contains("모든 슬로거 홈 흐름을 불러왔습니다.", writerIndexPage);
        Assert.Contains("공개 로그 흐름을 남긴 슬로거가 아직 없습니다.", writerIndexPage);
        Assert.Contains("와 이어지는 슬로거 회상 흐름이 없습니다.", writerIndexPage);
        Assert.Contains("aria-label=\"슬로거 홈 흐름 신호\"", writerIndexPage);
        Assert.Contains("현재 홈 흐름", writerIndexPage);
        Assert.Contains("GetFlowStatusTitle()", writerIndexPage);
        Assert.Contains("GetFlowStatusDescription()", writerIndexPage);
        Assert.Contains("FormatSloggerHomeCount(allWriters.Count)", writerIndexPage);
        Assert.Contains("GetFlowScopeLabel()", writerIndexPage);
        Assert.Contains("개 슬로거 홈", writerIndexPage);

        Assert.DoesNotContain("<PageTitle>슬로거</PageTitle>", writerIndexPage);
        Assert.DoesNotContain("찾고 로그 홈으로 이동합니다.", writerIndexPage);
        Assert.DoesNotContain(">로그 수</a>", writerIndexPage);
        Assert.DoesNotContain(">이름순</a>", writerIndexPage);
        Assert.DoesNotContain("(@item.Count)", writerIndexPage);
        Assert.DoesNotContain("모든 슬로거를 불러왔습니다.", writerIndexPage);
        Assert.DoesNotContain("공개 로그를 남긴 슬로거가 아직 없습니다.", writerIndexPage);
        Assert.DoesNotContain("슬로거 회상 결과가 없습니다.", writerIndexPage);
    }

    [Fact]
    public void PublicClueAndSeriesDiscoveryUseFlowSortLabels()
    {
        var tagIndexPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "TagIndex.razor"));
        var seriesIndexPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "SeriesIndex.razor"));
        var tagPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "TagPage.razor"));
        var seriesPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "SeriesPage.razor"));

        Assert.Contains(">반복 단서</h1>", tagIndexPage);
        Assert.Contains("slogs의 반복 단서를 회상하며 이어지는 공개 로그 흐름을 다시 따라갑니다.", tagIndexPage);
        Assert.Contains(">단서명순</a>", tagIndexPage);
        Assert.Contains("모든 단서 흐름을 불러왔습니다.", tagIndexPage);
        Assert.Contains("aria-label=\"단서 흐름 신호\"", tagIndexPage);
        Assert.Contains("현재 단서 흐름", tagIndexPage);
        Assert.Contains("와 이어지는 단서 회상 흐름이 없습니다.", tagIndexPage);
        Assert.Contains("FormatClueCount(allTags.Count)", tagIndexPage);
        Assert.Contains("개 단서", tagIndexPage);
        Assert.Contains("GetClueRecallPath(item)", tagIndexPage);
        Assert.Contains("단서 회상 경로: #{item.Tag} -> 공개 로그 흐름", tagIndexPage);
        Assert.Contains(">로그 시리즈</h1>", seriesIndexPage);
        Assert.Contains("slogs의 로그 시리즈를 회상하며 시간과 의미로 이어진 흐름을 다시 따라갑니다.", seriesIndexPage);
        Assert.Contains(">시리즈명순</a>", seriesIndexPage);
        Assert.Contains("모든 시리즈 흐름을 불러왔습니다.", seriesIndexPage);
        Assert.Contains("aria-label=\"로그 시리즈 흐름 신호\"", seriesIndexPage);
        Assert.Contains("현재 시리즈 흐름", seriesIndexPage);
        Assert.Contains("와 이어지는 로그 시리즈 회상 흐름이 없습니다.", seriesIndexPage);
        Assert.Contains("FormatSeriesCount(allSeries.Count)", seriesIndexPage);
        Assert.Contains("개 로그 시리즈", seriesIndexPage);
        Assert.Contains("GetSeriesRecallPath(item)", seriesIndexPage);
        Assert.Contains("로그 시리즈 회상 경로: {item.Series} -> 공개 로그 흐름", seriesIndexPage);
        Assert.Contains("aria-label=\"단서 상세 흐름 신호\"", tagPage);
        Assert.Contains("#@Tag 단서 회상 흐름", tagPage);
        Assert.Contains("GetFlowStatusDescription()", tagPage);
        Assert.Contains("FormatPublicLogNodeCount(totalCount)", tagPage);
        Assert.Contains("개 공개 로그 노드", tagPage);
        Assert.Contains("아직 #{Tag} 단서로 이어진 공개 로그 흐름이 없습니다.", tagPage);
        Assert.Contains("aria-label=\"로그 시리즈 상세 흐름 신호\"", seriesPage);
        Assert.Contains("@Series 시리즈 회상 흐름", seriesPage);
        Assert.Contains("GetFlowStatusDescription()", seriesPage);
        Assert.Contains("FormatPublicLogNodeCount(totalCount)", seriesPage);
        Assert.Contains("개 공개 로그 노드", seriesPage);
        Assert.Contains("아직 {Series} 시리즈로 이어진 공개 로그 흐름이 없습니다.", seriesPage);

        Assert.DoesNotContain(">이름순</a>", tagIndexPage);
        Assert.DoesNotContain(">이름순</a>", seriesIndexPage);
        Assert.DoesNotContain("모든 단서를 불러왔습니다.", tagIndexPage);
        Assert.DoesNotContain("모든 로그 시리즈를 불러왔습니다.", seriesIndexPage);
        Assert.DoesNotContain("단서 회상 결과가 없습니다.", tagIndexPage);
        Assert.DoesNotContain("로그 시리즈 회상 결과가 없습니다.", seriesIndexPage);
        Assert.DoesNotContain("slogs의 반복 단서를 회상하고 이어지는 공개 로그 흐름을 확인합니다.", tagIndexPage);
        Assert.DoesNotContain("slogs의 로그 시리즈를 회상하고 시간과 의미로 이어진 흐름을 확인합니다.", seriesIndexPage);
        Assert.DoesNotContain("@($\"{totalCount}개 로그가 이 단서로 이어집니다.\")", tagPage);
        Assert.DoesNotContain("@($\"{totalCount}개 로그가 같은 문제의식으로 이어집니다.\")", seriesPage);
    }

    [Fact]
    public void HomeFirstScreenExposesCompactKnowledgeLogFlowStatus()
    {
        var homePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Home.razor"));
        var appCss = File.ReadAllText(FindRepoFile("src", "Slogs", "wwwroot", "app.css"));

        Assert.Contains("slogs-home-flowline", homePage);
        Assert.Contains("aria-label=\"현재 지식 로그 흐름\"", homePage);
        Assert.Contains("현재 흐름", homePage);
        Assert.Contains("GetVisibleFlowTitle()", homePage);
        Assert.Contains("GetVisibleFlowDescription()", homePage);
        Assert.Contains("FormatLogNodeCount(totalCount)", homePage);
        Assert.Contains("GetFlowScopeLabel()", homePage);
        Assert.Contains("개 로그 노드", homePage);
        Assert.Contains(">반응 회상</a>", homePage);
        Assert.Contains("반응으로 이어진 로그", homePage);
        Assert.Contains("회상 접근, 대화 흔적, 공감 신호로 다시 이어진 공개 로그 흐름을 따라갑니다.", homePage);
        Assert.Contains(">의미 회상</a>", homePage);
        Assert.Contains("의미가 이어진 로그", homePage);
        Assert.Contains("회상 접근, 대화 흔적, 공감 신호, 단서, 시리즈가 겹치는 다음 의미 경로를 따라갑니다.", homePage);
        Assert.Contains(">최근 흐름</a>", homePage);
        Assert.Contains("최근 공개 로그 흐름", homePage);
        Assert.Contains("의미 회상", homePage);
        Assert.Contains("사람과 AI가 이어 쓰는 공개 지식 로그 흐름입니다.", homePage);
        Assert.Contains("의미 회상 흐름 | slogs", homePage);
        Assert.Contains("slogs에서 {GetDisplayedQuery()}와 이어지는 의미 회상 흐름을 다시 따라갑니다.", homePage);
        Assert.Contains("와 이어지는 의미 회상 흐름이 없습니다.", homePage);
        Assert.Contains("와 이어지는 회상 흐름이 없습니다.", homePage);

        Assert.Contains(".slogs-home-flowline", appCss);
        Assert.Contains("border-top: 1px solid var(--theme-border);", appCss);
        Assert.Contains("border-bottom: 1px solid var(--theme-border);", appCss);
        Assert.Contains(".slogs-home-flowline__signals", appCss);
        Assert.Contains("@media (max-width: 640px)", appCss);

        Assert.DoesNotContain("slogs-home-hero", homePage);
        Assert.DoesNotContain("마케팅", homePage);
        Assert.DoesNotContain("의미 회상 결과 | slogs", homePage);
        Assert.DoesNotContain("회상 결과가 없습니다.", homePage);
        Assert.DoesNotContain("slogs에서 {GetDisplayedQuery()} 의미 회상 결과를 확인합니다.", homePage);
        Assert.DoesNotContain("추천 회상", homePage);
        Assert.DoesNotContain("의미로 추천된 로그", homePage);
        Assert.DoesNotContain("이어 읽을 로그를 고릅니다.", homePage);
        Assert.DoesNotContain(">반응 로그</a>", homePage);
        Assert.DoesNotContain("반응이 모이는 로그", homePage);
        Assert.DoesNotContain("공개 로그를 먼저 봅니다.", homePage);
        Assert.DoesNotContain(">새 로그</a>", homePage);
        Assert.DoesNotContain("새 로그 스트림", homePage);
    }

    [Fact]
    public void PublicLogNodeCardsExposeRecallPathSignals()
    {
        var postLogCard = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "PostLogCard.razor"));
        var postFlowSignals = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "PostFlowSignals.razor"));
        var postRecallPath = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "PostRecallPath.razor"));
        var appCss = File.ReadAllText(FindRepoFile("src", "Slogs", "wwwroot", "app.css"));

        Assert.Contains("<PostRecallPath Post=\"Post\" />", postLogCard);
        Assert.Contains("DraftActionText { get; set; } = \"게시전 기억 정리\"", postLogCard);
        Assert.DoesNotContain("DraftActionText { get; set; } = \"게시전 로그 수정\"", postLogCard);
        Assert.Contains("Post.IsDraft ? \"게시전 기억\" : \"공개 공유 노드\"", postFlowSignals);
        Assert.Contains("대화 흔적", postFlowSignals);
        Assert.Contains("공감 신호", postFlowSignals);
        Assert.Contains("post-log-card__recall-path", postRecallPath);
        Assert.Contains("로그 회상 경로", postRecallPath);
        Assert.Contains("FormatRecallPath(Post)", postRecallPath);
        Assert.Contains("회상 경로:", postRecallPath);
        Assert.Contains("FormatUserName(post.Author)", postRecallPath);
        Assert.Contains("GetPrimaryClue(post)", postRecallPath);
        Assert.Contains("GetSeriesPath(post)", postRecallPath);
        Assert.Contains("GetConversationSignal(post)", postRecallPath);
        Assert.Contains("{post.CommentCount:N0} 대화 흔적 / {post.LikeCount:N0} 공감 신호", postRecallPath);
        Assert.Contains("단서 미지정", postRecallPath);
        Assert.Contains("단일 로그", postRecallPath);

        Assert.Contains(".post-log-card__recall-path", appCss);
        Assert.Contains("-webkit-line-clamp: 2;", appCss);

        Assert.DoesNotContain("Post.IsDraft ? \"게시전 로그\" : \"공개 로그\"", postFlowSignals);
        Assert.DoesNotContain("} 대화\")", postFlowSignals);
        Assert.DoesNotContain("} 반응\")", postFlowSignals);
        Assert.DoesNotContain("대화/{post.LikeCount:N0} 반응", postRecallPath);
        Assert.DoesNotContain("article teaser", postRecallPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicStreamDoneAndEmptyStatesKeepKnowledgeFlowLanguage()
    {
        var homePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Home.razor"));
        var postIndexPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "PostIndex.razor"));
        var profilePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Profile.razor"));
        var writerPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WriterPage.razor"));

        Assert.Contains("현재 지식 로그 흐름을 모두 불러왔습니다.", homePage);
        Assert.Contains("에 이어지는 의미 회상 로그가 없습니다.", homePage);
        Assert.Contains("이어진 로그 흐름이 없습니다.", homePage);
        Assert.Contains("aria-label=\"공개 로그 흐름 신호\"", postIndexPage);
        Assert.Contains("현재 공개 흐름", postIndexPage);
        Assert.Contains("FormatPublicLogNodeCount(allPosts.Count)", postIndexPage);
        Assert.Contains("GetFlowScopeLabel()", postIndexPage);
        Assert.Contains("개 공개 로그 노드", postIndexPage);
        Assert.Contains("의미 회상으로 이어진 공개 로그 흐름이 없습니다.", postIndexPage);
        Assert.Contains("내 지식 로그 흐름을 모두 불러왔습니다.", profilePage);
        Assert.Contains("대표로 이어 줄 공개 로그 노드가 아직 없습니다.", writerPage);
        Assert.Contains("이 슬로거의 공개 로그 스트림이 아직 비어 있습니다.", writerPage);
        Assert.Contains("이 슬로거의 공개 로그 스트림을 모두 불러왔습니다.", writerPage);

        foreach (var page in new[] { homePage, profilePage, writerPage })
        {
            Assert.DoesNotContain("DoneText=\"모든 로그를 불러왔습니다.\"", page);
        }

        Assert.DoesNotContain("대표로 보여줄 공개 로그가 없습니다.", writerPage);
        Assert.DoesNotContain("공개된 로그가 없습니다.", writerPage);
        Assert.DoesNotContain("에 이어지는 공개 로그가 없습니다.", postIndexPage);
    }

    [Fact]
    public void WriterHomeHeroExposesKnowledgeFlowSummarySignals()
    {
        var writerPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WriterPage.razor"));
        var appCss = File.ReadAllText(FindRepoFile("src", "Slogs", "wwwroot", "app.css"));

        Assert.Contains("slogger-flowline", writerPage);
        Assert.Contains("aria-label=\"슬로거 지식 흐름 요약\"", writerPage);
        Assert.Contains("지식 흐름 요약", writerPage);
        Assert.Contains("GetSloggerFlowDescription()", writerPage);
        Assert.Contains("GetFeaturedLogSignal()", writerPage);
        Assert.Contains("GetPrimaryClueSignal()", writerPage);
        Assert.Contains("GetPrimarySeriesSignal()", writerPage);
        Assert.Contains("GetSloggerHomeJsonLd()", writerPage);
        Assert.Contains("SeoMetadata.SloggerHomeJsonLd", writerPage);
        Assert.Contains("공개 지식 로그 홈", writerPage);
        Assert.Contains("공개 공유 노드", writerPage);
        Assert.Contains("공개 공유 노드 {totalCount}개", writerPage);
        Assert.Contains("공개 공유 노드로 쌓이기 전의 지식 로그 홈입니다.", writerPage);
        Assert.Contains("슬로거 홈 이미지", writerPage);
        Assert.Contains("슬로거 홈 &#64;주소", writerPage);
        Assert.Contains("대표 기억 노드", writerPage);
        Assert.Contains("PostRecallPath Post=\"featuredPost\"", writerPage);
        Assert.Contains("대표 기억 노드 회상 경로", writerPage);
        Assert.Contains("writer-featured-recall-path", writerPage);
        Assert.Contains("시간순 로그 흐름", writerPage);
        Assert.Contains("대표 로그 연결", writerPage);
        Assert.Contains("주요 단서 #", writerPage);
        Assert.Contains("주요 시리즈", writerPage);
        Assert.Contains("생각과 작업 판단 흐름을 회상합니다.", writerPage);

        Assert.Contains(".slogger-flowline", appCss);
        Assert.Contains(".slogger-flowline__signals", appCss);
        Assert.Contains("border-top: 1px solid var(--theme-border);", appCss);
        Assert.Contains("border-bottom: 1px solid var(--theme-border);", appCss);
        Assert.Contains("@media (max-width: 640px)", appCss);

        Assert.DoesNotContain("slogger-hero-card", writerPage);
        Assert.DoesNotContain("profile metrics", writerPage);
        Assert.DoesNotContain("ProfilePageJsonLd", writerPage);
        Assert.DoesNotContain("프로필 이미지", writerPage);
        Assert.DoesNotContain(">공개 주소", writerPage);
        Assert.DoesNotContain("<p class=\"text-xs font-semibold text-slate-500\">공개 로그</p>", writerPage);
        Assert.DoesNotContain("공개 로그 {totalCount}개", writerPage);
        Assert.DoesNotContain("공개 로그로 쌓이기 전의 지식 로그 홈입니다.", writerPage);
        Assert.DoesNotContain("public knowledge-log home", writerPage);
        Assert.DoesNotContain("featured memory node", writerPage);
        Assert.DoesNotContain("chronological log stream", writerPage);
    }

    [Fact]
    public void PostDetailFirstViewExposesKnowledgeNodeFlowSummary()
    {
        var postDetailsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "PostDetails.razor"));
        var appCss = File.ReadAllText(FindRepoFile("src", "Slogs", "wwwroot", "app.css"));

        Assert.Contains("post-detail-flowline", postDetailsPage);
        Assert.Contains("aria-label=\"로그 노드 흐름 요약\"", postDetailsPage);
        Assert.Contains("노드 흐름 요약", postDetailsPage);
        Assert.Contains("GetPostNodeFlowDescription(post, latestRevisionNumberForNode)", postDetailsPage);
        Assert.Contains("GetPrimaryClueSignal(post)", postDetailsPage);
        Assert.Contains("GetPrimarySeriesSignal(post)", postDetailsPage);
        Assert.Contains("GetRevisionFlowSignal(post, latestRevisionNumberForNode)", postDetailsPage);
        Assert.Contains("GetLinkedLogFlowSignal()", postDetailsPage);
        Assert.Contains("GetAdjacentFlowSignal()", postDetailsPage);
        Assert.Contains("FormatConversationTraceSignal(post.CommentCount)", postDetailsPage);
        Assert.Contains("Title=\"@GetPostSeoTitle(post)\"", postDetailsPage);
        Assert.Contains("Description=\"@GetPostSeoDescription(post)\"", postDetailsPage);
        Assert.Contains("게시전 지식 로그 노드", postDetailsPage);
        Assert.Contains(">게시전 기억</span>", postDetailsPage);
        Assert.Contains(">공개 공유 노드</span>", postDetailsPage);
        Assert.Contains("return $\"{targetPost.Title} {nodeState} | slogs\";", postDetailsPage);
        Assert.Contains("소유자 전용 게시전 기억", postDetailsPage);
        Assert.Contains("return $\"{nodeState}: {summary}", postDetailsPage);
        Assert.Contains("SeoMetadata.PublicLogNodeJsonLd", postDetailsPage);
        Assert.Contains("본문과 대화 흔적은", postDetailsPage);
        Assert.Contains("이어진 지식 로그 노드입니다.", postDetailsPage);
        Assert.Contains("주요 단서 #", postDetailsPage);
        Assert.Contains("로그 시리즈", postDetailsPage);
        Assert.Contains("리비전 흐름 v", postDetailsPage);
        Assert.Contains("개 연결 로그 흐름", postDetailsPage);
        Assert.Contains("연결 로그 흐름 대기", postDetailsPage);
        Assert.Contains("개 앞뒤 흐름", postDetailsPage);
        Assert.Contains("앞뒤 흐름 대기", postDetailsPage);
        Assert.Contains("개 대화 흔적", postDetailsPage);
        Assert.Contains("흐름 갱신 @FormatDateTime(post.UpdatedAt)", postDetailsPage);
        Assert.Contains("연결 로그 흐름 보기", postDetailsPage);
        Assert.Contains("앞선 흐름 이동", postDetailsPage);
        Assert.Contains("다음 흐름 이동", postDetailsPage);
        Assert.Contains(">연결 로그 흐름</h3>", postDetailsPage);
        Assert.Contains("연결 로그 흐름 열기", postDetailsPage);
        Assert.Contains("아직 이어진 연결 로그 흐름이 없습니다.", postDetailsPage);
        Assert.Contains("PostRecallPath Post=\"item\"", postDetailsPage);
        Assert.Contains("연결 로그 회상 경로", postDetailsPage);
        Assert.Contains("PostRecallPath Post=\"previousPost\"", postDetailsPage);
        Assert.Contains("앞선 흐름 회상 경로", postDetailsPage);
        Assert.Contains(">앞선 흐름</p>", postDetailsPage);
        Assert.Contains("앞선 흐름이 없습니다.", postDetailsPage);
        Assert.Contains("PostRecallPath Post=\"nextPost\"", postDetailsPage);
        Assert.Contains("다음 흐름 회상 경로", postDetailsPage);
        Assert.Contains(">다음 흐름</p>", postDetailsPage);
        Assert.Contains("다음 흐름이 없습니다.", postDetailsPage);
        Assert.Contains("post-detail-linked-card__recall-path", postDetailsPage);

        Assert.Contains(".post-detail-flowline", appCss);
        Assert.Contains(".post-detail-flowline__signals", appCss);
        Assert.Contains(".post-detail-linked-card__recall-path", appCss);
        Assert.Contains("border-top: 1px solid var(--theme-border);", appCss);
        Assert.Contains("border-bottom: 1px solid var(--theme-border);", appCss);
        Assert.Contains("@media (max-width: 640px)", appCss);

        Assert.DoesNotContain("post-detail-article-summary-card", postDetailsPage);
        Assert.DoesNotContain("SeoMetadata.ArticleJsonLd", postDetailsPage);
        Assert.DoesNotContain("Type=\"article\"", postDetailsPage);
        Assert.DoesNotContain("Title=\"@($\"{post.Title} | slogs\")\"", postDetailsPage);
        Assert.DoesNotContain("Description=\"@post.Summary\"", postDetailsPage);
        Assert.DoesNotContain(">게시전 로그</span>", postDetailsPage);
        Assert.DoesNotContain(">공개 로그</span>", postDetailsPage);
        Assert.DoesNotContain("관련 글", postDetailsPage);
        Assert.DoesNotContain(">연결된 로그</h3>", postDetailsPage);
        Assert.DoesNotContain("연결된 로그가 없습니다.", postDetailsPage);
        Assert.DoesNotContain("이전 로그 이동", postDetailsPage);
        Assert.DoesNotContain("다음 로그 이동", postDetailsPage);
        Assert.DoesNotContain(">이전 로그</p>", postDetailsPage);
        Assert.DoesNotContain(">다음 로그</p>", postDetailsPage);
        Assert.DoesNotContain("이전 로그가 없습니다.", postDetailsPage);
        Assert.DoesNotContain("다음 로그가 없습니다.", postDetailsPage);
        Assert.DoesNotContain("수정 @FormatDateTime(post.UpdatedAt)", postDetailsPage);
    }

    [Fact]
    public void PostDetailRevisionComparisonUsesRevisionFlowLanguage()
    {
        var postDetailsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "PostDetails.razor"));
        var appCss = File.ReadAllText(FindRepoFile("src", "Slogs", "wwwroot", "app.css"));
        var blogService = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "BlogService.cs"));

        Assert.Contains("post-detail-revision-flow", postDetailsPage);
        Assert.Contains("aria-label=\"리비전 흐름 신호\"", postDetailsPage);
        Assert.Contains("리비전 흐름 비교", postDetailsPage);
        Assert.Contains("선택한 리비전이 이 로그 노드의 기억 흐름을 어떻게 갱신했는지 다시 따라갑니다.", postDetailsPage);
        Assert.Contains("리비전 흐름 변화를 불러오는 중입니다.", postDetailsPage);
        Assert.Contains("리비전 흐름 변화를 불러오지 못했습니다.", postDetailsPage);
        Assert.Contains("비교할 리비전 흐름 변화가 없습니다.", postDetailsPage);
        Assert.Contains("흐름 영역 {diff.Label}", postDetailsPage);
        Assert.Contains("FormatRevisionFlowChangeCount(revisionFlowChangeCount)", postDetailsPage);
        Assert.Contains("흐름 변화 기록 없음", postDetailsPage);
        Assert.Contains("개 흐름 변화", postDetailsPage);
        Assert.Contains("첫 공개 공유", blogService);
        Assert.Contains("record PostRevisionSummaryResponse", File.ReadAllText(FindRepoFile("src", "Slogs.Shared", "Data", "SlogsApiContracts.cs")));

        Assert.Contains(".post-detail-revision-flow", appCss);
        Assert.Contains(".post-detail-revision-flow__signals", appCss);
        Assert.Contains("border-bottom: 1px solid var(--theme-border);", appCss);

        Assert.DoesNotContain("변경점을 불러오는 중입니다.", postDetailsPage);
        Assert.DoesNotContain("변경점을 불러오지 못했습니다.", postDetailsPage);
        Assert.DoesNotContain("비교할 변경점이 없습니다.", postDetailsPage);
        Assert.DoesNotContain("선택한 리비전이 이 로그 노드의 기억을 어떻게 갱신했는지 확인합니다.", postDetailsPage);
        Assert.DoesNotContain("초기 게시", blogService);
    }

    [Fact]
    public void SavedAndResonancePagesUseLogNodeCards()
    {
        var postLogCard = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "PostLogCard.razor"));
        var bookmarksPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "MyBookmarks.razor"));
        var likesPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "MyLikes.razor"));

        Assert.Contains("RenderFragment? ActionContent", postLogCard);
        Assert.Contains("ActionContent is not null", postLogCard);

        foreach (var page in new[] { bookmarksPage, likesPage })
        {
            Assert.Contains("<PostLogCard", page);
            Assert.Contains("<ActionContent>", page);
            Assert.Contains("SummaryMaxLength=\"140\"", page);
            Assert.Contains("CommentsUrl=\"@GetPostCommentsUrl(post)\"", page);
            Assert.Contains("href=\"@GetPostCommentsUrl(post)\"", page);
            Assert.Contains("SlogsIcon Name=\"message-circle\"", page);
            Assert.Contains("대화 흔적", page);
            Assert.Contains("PostNavigationUrlBuilder.BuildPostUrl(post, PostNavigationUrlBuilder.PersonalMenuContext)", page);
            Assert.Contains("PostNavigationUrlBuilder.BuildCommentsUrl(post, PostNavigationUrlBuilder.PersonalMenuContext)", page);
            Assert.DoesNotContain("<PostMetaLine", page);
            Assert.DoesNotContain("#conversation", page);
            Assert.DoesNotContain("GetPostUrl(post)}#comments", page);
        }

        Assert.Contains("저장 회상 흐름", bookmarksPage);
        Assert.Contains("내가 저장한 slogs 로그 흐름을 다시 회상하고 이어 읽습니다.", bookmarksPage);
        Assert.Contains("저장한 판단 단서를 따라 다시 이어 읽을 로그 흐름을 모아봅니다.", bookmarksPage);
        Assert.Contains("모든 저장 회상 흐름을 불러왔습니다.", bookmarksPage);
        Assert.Contains("저장 회상 흐름에서 제거되었습니다.", bookmarksPage);
        Assert.Contains("저장 회상 흐름에 추가되었습니다.", bookmarksPage);
        Assert.Contains("저장 회상 해제", bookmarksPage);
        Assert.Contains("공감 신호 흐름", likesPage);
        Assert.Contains("내가 공감한 slogs 로그 흐름을 다시 따라갑니다.", likesPage);
        Assert.Contains("내 판단에 남은 공감 신호의 로그 흐름을 모아봅니다.", likesPage);
        Assert.Contains("새 공감 신호순", likesPage);
        Assert.Contains("모든 공감 신호 흐름을 불러왔습니다.", likesPage);
        Assert.Contains("공감 신호 흐름에서 해제되었습니다.", likesPage);
        Assert.Contains("공감 신호 흐름에 추가되었습니다.", likesPage);
        Assert.Contains("공감 신호 해제", likesPage);

        Assert.DoesNotContain("저장 로그", bookmarksPage);
        Assert.DoesNotContain("내가 저장한 slogs 로그를 다시 확인합니다.", bookmarksPage);
        Assert.DoesNotContain("다시 이어 읽을 로그를 모아봅니다.", bookmarksPage);
        Assert.DoesNotContain("모든 저장 로그를 불러왔습니다.", bookmarksPage);
        Assert.DoesNotContain("저장 로그에서 제거되었습니다.", bookmarksPage);
        Assert.DoesNotContain("저장 로그에 추가되었습니다.", bookmarksPage);
        Assert.DoesNotContain("저장 상태 변경에 실패했습니다.", bookmarksPage);
        Assert.DoesNotContain("공감 로그", likesPage);
        Assert.DoesNotContain("새 공감순", likesPage);
        Assert.DoesNotContain("내가 공감한 slogs 로그를 확인합니다.", likesPage);
        Assert.DoesNotContain("내가 공감한 로그 흐름을 모아봅니다.", likesPage);
        Assert.DoesNotContain("모든 공감 로그를 불러왔습니다.", likesPage);
        Assert.DoesNotContain("공감 해제 중...", likesPage);
        Assert.DoesNotContain("공감 신호가 해제되었습니다.", likesPage);
        Assert.DoesNotContain("공감 신호가 추가되었습니다.", likesPage);
    }

    [Fact]
    public void PersonalWorkspaceEmptyStatesOfferKnowledgeLogNextActions()
    {
        var icon = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "SlogsIcon.razor"));
        var profilePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Profile.razor"));
        var bookmarksPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "MyBookmarks.razor"));
        var likesPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "MyLikes.razor"));

        Assert.Contains("case \"plus\":", icon);

        Assert.Contains("아직 이어진 지식 로그 흐름이 없습니다.", profilePage);
        Assert.Contains("href=\"/write\"", profilePage);
        Assert.Contains("새 로그 남기기", profilePage);
        Assert.Contains("href=\"/me/llm-wiki/search\"", profilePage);
        Assert.Contains("기억에서 회상", profilePage);
        Assert.DoesNotContain("아직 남긴 로그가 없습니다.", profilePage);

        Assert.Contains("아직 저장 회상 흐름이 없습니다.", bookmarksPage);
        Assert.Contains("공개 흐름에서 다시 이어 읽을 로그", bookmarksPage);
        Assert.Contains("href=\"/post\"", bookmarksPage);
        Assert.Contains("공개 로그 흐름", bookmarksPage);
        Assert.Contains("href=\"/tag\"", bookmarksPage);
        Assert.Contains("단서 회상", bookmarksPage);
        Assert.DoesNotContain("저장한 로그가 없습니다.", bookmarksPage);

        Assert.Contains("아직 공감 신호 흐름이 없습니다.", likesPage);
        Assert.Contains("공감 신호", likesPage);
        Assert.Contains("href=\"/recommended\"", likesPage);
        Assert.Contains("의미 회상", likesPage);
        Assert.Contains("href=\"/post\"", likesPage);
        Assert.DoesNotContain("공감한 로그가 없습니다.", likesPage);
        Assert.DoesNotContain("추천 회상", likesPage);
    }

    [Fact]
    public void WriterConnectionPagesUseRelationshipFlowLanguage()
    {
        var writerConnectionsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WriterConnections.razor"));
        var writerPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WriterPage.razor"));

        Assert.Contains("로그 홈으로 들어오는", writerConnectionsPage);
        Assert.Contains("이어 둔", writerConnectionsPage);
        Assert.Contains("공개 로그 홈이 누구에게 이어지고, 어떤 슬로거의 기억 흐름을 다시 따라갈지 회상합니다.", writerConnectionsPage);
        Assert.Contains("break-words text-2xl", writerConnectionsPage);
        Assert.Contains("이 로그 홈을 잇는 슬로거", writerConnectionsPage);
        Assert.Contains("이어 둔 로그 홈", writerConnectionsPage);
        Assert.Contains("개 관계 흐름", writerConnectionsPage);
        Assert.Contains("관계 로그 홈", writerConnectionsPage);
        Assert.Contains("관계 잇기", writerConnectionsPage);
        Assert.Contains("관계 해제", writerConnectionsPage);
        Assert.Contains("슬로거 회상", writerConnectionsPage);
        Assert.Contains("공개 로그 흐름", writerConnectionsPage);
        Assert.Contains("요청한 슬로거 관계 흐름을 찾지 못했습니다.", writerConnectionsPage);
        Assert.DoesNotContain(">팔로워 (", writerConnectionsPage);
        Assert.DoesNotContain(">팔로잉 (", writerConnectionsPage);
        Assert.DoesNotContain(">팔로우<", writerConnectionsPage);
        Assert.DoesNotContain(">팔로우 해제<", writerConnectionsPage);
        Assert.DoesNotContain("명 연결", writerConnectionsPage);
        Assert.DoesNotContain("아직 이어진 관계 흐름이 없습니다.", writerConnectionsPage);
        Assert.DoesNotContain("해당 슬로거를 찾을 수 없습니다.", writerConnectionsPage);
        Assert.DoesNotContain("어떤 슬로거의 기억 흐름을 따라가는지 확인합니다.", writerConnectionsPage);

        Assert.Contains("이 로그 홈을 잇는 슬로거 @followerCount", writerPage);
        Assert.Contains("이어 둔 로그 홈 @followingCount", writerPage);
        Assert.Contains("관계 흐름 {followerCount}개", writerPage);
        Assert.DoesNotContain(">팔로워 @followerCount", writerPage);
        Assert.DoesNotContain(">팔로잉 @followingCount", writerPage);
        Assert.DoesNotContain(">팔로우<", writerPage);
        Assert.DoesNotContain(">팔로우 해제<", writerPage);
        Assert.DoesNotContain("팔로워 {followerCount}명", writerPage);
    }

    [Fact]
    public void HomeFeedAndPostDetailAuthorActionsUseRelationshipFlowLanguage()
    {
        var homePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Home.razor"));
        var postDetailsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "PostDetails.razor"));

        Assert.Contains(">이어 둔 로그</a>", homePage);
        Assert.Contains("이어 둔 로그 흐름을 보려면 지식 로그 홈으로 돌아가야 합니다.", homePage);
        Assert.Contains("아직 이어 둔 로그 홈이 없습니다.", homePage);
        Assert.Contains("이어 둔 로그 흐름에서", homePage);
        Assert.Contains("관계로 이어 둔 슬로거의 공개 로그 흐름", homePage);
        Assert.DoesNotContain("이어 둔 로그 흐름은 로그인 후 이용 가능합니다.", homePage);
        Assert.DoesNotContain(">팔로우 로그</a>", homePage);
        Assert.DoesNotContain("팔로우 로그 스트림", homePage);
        Assert.DoesNotContain("팔로우한 슬로거", homePage);
        Assert.DoesNotContain("팔로우해 보세요", homePage);

        Assert.Contains("관계 흐름 {authorFollowerCount}개", postDetailsPage);
        Assert.Contains("관계 해제", postDetailsPage);
        Assert.Contains("관계 잇기", postDetailsPage);
        Assert.DoesNotContain("명 팔로워", postDetailsPage);
        Assert.DoesNotContain(">팔로우<", postDetailsPage);
        Assert.DoesNotContain("팔로우 해제", postDetailsPage);
    }

    [Fact]
    public void PostDetailReplyFlowUsesContinuingConversationTraceLanguage()
    {
        var postDetailsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "PostDetails.razor"));

        Assert.Contains("대화 흔적 흐름 (@post.CommentCount)", postDetailsPage);
        Assert.Contains("이 로그 노드에 대화 흔적을 남기려면", postDetailsPage);
        Assert.Contains("placeholder=\"이 로그 노드에 이어질 대화 흔적을 남겨주세요\"", postDetailsPage);
        Assert.Contains("흔적 남기기", postDetailsPage);
        Assert.Contains("아직 이어진 대화 흔적이 없습니다. 첫 흐름을 남겨 보세요.", postDetailsPage);
        Assert.Contains(">최근 흔적</a>", postDetailsPage);
        Assert.Contains(">처음 흔적</a>", postDetailsPage);
        Assert.Contains("흐름 기준: {GetCommentSortLabel(commentSortOrder)} · 대화 흔적 {commentTotalCount}개", postDetailsPage);
        Assert.Contains("모든 대화 흔적 흐름을 불러왔습니다.", postDetailsPage);
        Assert.Contains("대화 잇기", postDetailsPage);
        Assert.Contains("대화 흔적에 이어 남기기", postDetailsPage);
        Assert.Contains("흔적 다듬기", postDetailsPage);
        Assert.Contains("흔적 갱신", postDetailsPage);
        Assert.Contains("흔적 갱신됨", postDetailsPage);
        Assert.Contains("흔적 지우기", postDetailsPage);
        Assert.Contains("이 대화 흔적을 지울까요?", postDetailsPage);
        Assert.Contains("이 이어진 대화 흔적을 지울까요?", postDetailsPage);
        Assert.Contains("대화 흔적을 다듬을 권한이 없습니다.", postDetailsPage);
        Assert.Contains("대화 흔적을 갱신하지 못했습니다.", postDetailsPage);
        Assert.Contains("대화 흔적이 갱신되었습니다.", postDetailsPage);
        Assert.Contains("지울 대화 흔적을 찾을 수 없습니다.", postDetailsPage);
        Assert.Contains("대화 흔적을 지울 권한이 없습니다.", postDetailsPage);
        Assert.Contains("대화 흔적을 지우지 못했습니다.", postDetailsPage);
        Assert.Contains("대화 흔적이 지워졌습니다.", postDetailsPage);
        Assert.Contains("placeholder=\"대화 흔적에 이어 남겨주세요\"", postDetailsPage);
        Assert.Contains("이어갈 대화 흔적을 찾을 수 없습니다.", postDetailsPage);
        Assert.Contains("대화 흔적에 이어 남기려면 지식 로그 홈으로 돌아가야 합니다.", postDetailsPage);
        Assert.Contains("이어 남길 대화 흔적을 입력해 주세요.", postDetailsPage);
        Assert.Contains("이어진 대화 흔적을 남기지 못했습니다.", postDetailsPage);
        Assert.Contains("대화 흔적이 이어졌습니다.", postDetailsPage);
        Assert.DoesNotContain("대화 흔적 (@post.CommentCount)", postDetailsPage);
        Assert.DoesNotContain("text-slate-500\">대화 흔적을 남기려면", postDetailsPage);
        Assert.DoesNotContain("placeholder=\"대화 흔적을 남겨주세요\"", postDetailsPage);
        Assert.DoesNotContain(">남기기</button>", postDetailsPage);
        Assert.DoesNotContain("첫 번째 대화 흔적을 남겨 보세요.", postDetailsPage);
        Assert.DoesNotContain(">최신순</a>", postDetailsPage);
        Assert.DoesNotContain(">오래된순</a>", postDetailsPage);
        Assert.DoesNotContain("정렬: {GetCommentSortLabel(commentSortOrder)}", postDetailsPage);
        Assert.DoesNotContain("상위 대화 {commentTotalCount}개", postDetailsPage);
        Assert.DoesNotContain("모든 대화 흔적을 불러왔습니다.", postDetailsPage);
        Assert.DoesNotContain("수정 저장", postDetailsPage);
        Assert.DoesNotContain("수정됨", postDetailsPage);
        Assert.DoesNotContain("이 대화 흔적을 삭제할까요?", postDetailsPage);
        Assert.DoesNotContain("이 이어진 대화 흔적을 삭제할까요?", postDetailsPage);
        Assert.DoesNotContain("대화 흔적 수정에 실패했습니다.", postDetailsPage);
        Assert.DoesNotContain("대화 흔적이 수정되었습니다.", postDetailsPage);
        Assert.DoesNotContain("삭제할 대화 흔적을 찾을 수 없습니다.", postDetailsPage);
        Assert.DoesNotContain("대화 흔적 삭제에 실패했습니다.", postDetailsPage);
        Assert.DoesNotContain("대화 흔적이 삭제되었습니다.", postDetailsPage);
        Assert.DoesNotContain("답글", postDetailsPage);
        Assert.DoesNotContain("대화 흔적에 대한", postDetailsPage);
        Assert.DoesNotContain("대화 흔적에 답글", postDetailsPage);
        Assert.DoesNotContain("대화 흔적은 로그인 후 이용 가능합니다.", postDetailsPage);
        Assert.DoesNotContain("답글 대상을", postDetailsPage);
        Assert.DoesNotContain("답글 내용을", postDetailsPage);
        Assert.DoesNotContain("답글 등록", postDetailsPage);
    }

    [Fact]
    public void LocalRegistrationStartsWithSloggerProfileFields()
    {
        var registerPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Register.razor"));
        var apiContracts = File.ReadAllText(FindRepoFile("src", "Slogs.Shared", "Data", "SlogsApiContracts.cs"));
        var authService = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "AuthService.cs"));

        Assert.Contains("지식 로그 홈 만들기", registerPage);
        Assert.Contains("public knowledge-log home", registerPage);
        Assert.Contains("슬로거 홈 &#64;주소", registerPage);
        Assert.Contains("첫 슬로거 홈 단서", registerPage);
        Assert.Contains("슬로거 홈 이미지 URL", registerPage);
        Assert.Contains("홈 소개", registerPage);
        Assert.Contains("슬로거 홈 정체성", registerPage);
        Assert.Contains("슬로거 홈 @주소와 비밀번호는 필수입니다.", registerPage);
        Assert.Contains("이미 사용 중인 슬로거 홈 @주소입니다.", registerPage);
        Assert.Contains("지식 로그 홈 생성 처리 중 오류가 발생했습니다.", registerPage);
        Assert.Contains("이미 슬로거 홈이 있다면 <a class=\"font-semibold underline\" href=\"@GetLoginHref()\">지식 로그로 돌아가기</a>", registerPage);
        Assert.Contains("profileImageUrl", registerPage);
        Assert.Contains("bio = profileBio", registerPage);
        Assert.DoesNotContain("회원가입에 실패했습니다.", registerPage);
        Assert.DoesNotContain("아이디와 비밀번호는 필수입니다.", registerPage);
        Assert.DoesNotContain("공개 @주소와 비밀번호는 필수입니다.", registerPage);
        Assert.DoesNotContain("이미 사용 중인 공개 @주소입니다.", registerPage);
        Assert.DoesNotContain("프로필 이미지 URL", registerPage);
        Assert.DoesNotContain("짧은 소개", registerPage);
        Assert.DoesNotContain("\"소개는 280자", registerPage);
        Assert.DoesNotContain("이미 계정이 있다면", registerPage);

        Assert.Contains("string? ProfileImageUrl = null", apiContracts);
        Assert.Contains("string? Bio = null", apiContracts);
        Assert.Contains("string? profileImageUrl = null", authService);
        Assert.Contains("NormalizeProfileImageUrl(profileImageUrl)", authService);
        Assert.Contains("NormalizeProfileBio(bio)", authService);
        Assert.Contains("ProfileUpdatedAt = hasInitialProfile ? now : null", authService);
    }

    [Fact]
    public void LoginEntryUsesKnowledgeLogReturnLanguage()
    {
        var loginPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Login.razor"));

        Assert.Contains("지식 로그로 돌아가기", loginPage);
        Assert.Contains("내 비공개 기억, 게시전 기억, 저장 회상 흐름과 공감 신호 흐름", loginPage);
        Assert.Contains("슬로거 홈 @주소", loginPage);
        Assert.Contains("로그 흐름으로 돌아가기", loginPage);
        Assert.Contains("Google로 지식 로그 이어가기", loginPage);
        Assert.Contains("Google 지식 로그 연결 설정이 아직 완료되지 않았습니다.", loginPage);
        Assert.Contains("Google 지식 로그 연결에 실패했습니다.", loginPage);
        Assert.Contains("Google 지식 로그 연결이 취소되었습니다.", loginPage);
        Assert.Contains("지식 로그 홈 만들기", loginPage);
        Assert.Contains("지식 로그 흐름으로 돌아가지 못했습니다.", loginPage);
        Assert.DoesNotContain(">아이디<", loginPage);
        Assert.DoesNotContain("아이디와 비밀번호", loginPage);
        Assert.DoesNotContain("공개 @주소", loginPage);
        Assert.DoesNotContain("회원가입", loginPage);
        Assert.DoesNotContain("저장 로그와 공감 로그", loginPage);
        Assert.DoesNotContain("저장/공감 흐름", loginPage);
        Assert.DoesNotContain("Google 로그인", loginPage);
        Assert.DoesNotContain("Google 계정 연결이 취소되었습니다.", loginPage);
    }

    [Fact]
    public void PrivateLoginRequiredSurfacesUseKnowledgeLogReturnLanguage()
    {
        var writePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WritePost.razor"));
        var editPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "EditPost.razor"));
        var profilePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Profile.razor"));
        var bookmarksPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "MyBookmarks.razor"));
        var likesPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "MyLikes.razor"));
        var llmWikiPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWiki.razor"));
        var llmWikiSearchPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWikiSearch.razor"));
        var settingsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Settings.razor"));
        var adminUsersPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "AdminUsers.razor"));
        var postDetailsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "PostDetails.razor"));
        var profileSettingsForm = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "ProfileSettingsForm.razor"));

        Assert.Contains("게시전 기억을 남기려면 내 지식 로그 흐름으로 돌아가야 합니다.", writePage);
        Assert.Contains("게시전 기억을 남기려면 지식 로그 홈으로 돌아가야 합니다.", writePage);
        Assert.Contains("로그 흐름을 정리하려면 내 지식 로그 흐름으로 돌아가야 합니다.", editPage);
        Assert.Contains("내 공개 공유 로그와 게시전 기억 흐름을 보려면 지식 로그 홈으로 돌아가야 합니다.", profilePage);
        Assert.Contains("저장 회상 흐름을 다시 열려면 지식 로그 홈으로 돌아가야 합니다.", bookmarksPage);
        Assert.Contains("저장 회상 흐름을 바꾸려면 지식 로그 홈으로 돌아가야 합니다.", bookmarksPage);
        Assert.Contains("공감 신호 흐름을 다시 따라가려면 지식 로그 홈으로 돌아가야 합니다.", likesPage);
        Assert.Contains("공감 신호 흐름을 바꾸려면 지식 로그 홈으로 돌아가야 합니다.", likesPage);
        Assert.Contains("비공개 기억 연결면을 열려면 지식 로그 홈으로 돌아가야 합니다.", llmWikiPage);
        Assert.Contains("비공개 기억을 회상하려면 지식 로그 홈으로 돌아가야 합니다.", llmWikiSearchPage);
        Assert.Contains("슬로거 홈 정체성과 연결 권한을 보려면 지식 로그 홈으로 돌아가야 합니다.", settingsPage);
        Assert.Contains("운영 흐름을 따라가려면 지식 로그 홈으로 돌아가야 합니다.", adminUsersPage);
        Assert.Contains("공감 신호를 남기려면 지식 로그 홈으로 돌아가야 합니다.", postDetailsPage);
        Assert.Contains("저장 회상 흐름을 바꾸려면 지식 로그 홈으로 돌아가야 합니다.", postDetailsPage);
        Assert.Contains("이 로그 노드에 대화 흔적을 남기려면 지식 로그 홈으로 돌아가야 합니다.", postDetailsPage);
        Assert.Contains("슬로거 홈 정체성을 바꾸려면 지식 로그 홈으로 돌아가야 합니다.", profileSettingsForm);

        foreach (var privateSurface in new[]
        {
            writePage,
            editPage,
            profilePage,
            bookmarksPage,
            likesPage,
            llmWikiPage,
            llmWikiSearchPage,
            settingsPage,
            adminUsersPage,
            postDetailsPage,
            profileSettingsForm
        })
        {
            Assert.DoesNotContain("로그인이 필요합니다.", privateSurface);
            Assert.DoesNotContain("로그인</a>해 주세요", privateSurface);
        }
    }

    [Fact]
    public void GoogleConfirmPageUsesSloggerHomeAddressFlowLanguage()
    {
        var program = File.ReadAllText(FindRepoFile("src", "Slogs", "Program.cs"));

        Assert.Contains("Google 지식 로그 연결 | slogs", program);
        Assert.Contains("Google로 지식 로그 이어가기", program);
        Assert.Contains("슬로거 홈 주소 단서", program);
        Assert.Contains("Google 계정에서 이어질 지식 로그 홈의 주소 단서를 정해 주세요.", program);
        Assert.Contains("공개 공유 노드, 게시전 기억, 노트 Vault 흐름이 모이는 슬로거 홈", program);
        Assert.DoesNotContain("공개 로그, 게시전 기억, 노트 Vault 흐름이 모이는 슬로거 홈", program);
        Assert.Contains("슬로거 홈 주소", program);
        Assert.Contains("홈 주소 잇기", program);
        Assert.Contains("연결 취소", program);
        Assert.Contains("슬로거 홈 주소 단서를 입력해 주세요.", program);
        Assert.Contains("Google 계정 연결 정보를 읽을 수 없습니다.", program);
        Assert.DoesNotContain("Google 계정 연결 확인 | slogs", program);
        Assert.DoesNotContain(">공개 주소 확인</h1>", program);
        Assert.DoesNotContain("Slogs에서 사용할 공개 주소를 확인해 주세요.", program);
        Assert.DoesNotContain(">공개 주소", program);
        Assert.DoesNotContain(">확인</button>", program);
        Assert.DoesNotContain(">취소</button>", program);
        Assert.DoesNotContain("공개 주소를 입력해 주세요.", program);
        Assert.DoesNotContain("Google 계정 정보를 확인할 수 없습니다.", program);
    }

    [Fact]
    public void FailureRoutesUseKnowledgeLogRecoveryLanguage()
    {
        var notFoundPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "NotFound.razor"));
        var errorPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Error.razor"));

        Assert.Contains("로그 흐름을 찾지 못했습니다", notFoundPage);
        Assert.Contains("요청한 로그 흐름을 찾지 못했습니다.", notFoundPage);
        Assert.Contains("지식 로그 홈", notFoundPage);
        Assert.Contains("공개 로그 흐름", notFoundPage);
        Assert.Contains("흐름을 다시 잇는 경로", notFoundPage);
        Assert.Contains("단서나 슬로거 흐름", notFoundPage);
        Assert.DoesNotContain("페이지를 찾을 수 없습니다", notFoundPage);
        Assert.DoesNotContain(">홈으로</a>", notFoundPage);
        Assert.DoesNotContain("보기</a>", notFoundPage);

        Assert.Contains("흐름을 이어가지 못했습니다", errorPage);
        Assert.Contains("요청한 흐름을 이어가지 못했습니다.", errorPage);
        Assert.Contains("기억, 로그, 단서 흐름", errorPage);
        Assert.Contains("href=\"/write\">새 로그 남기기", errorPage);
        Assert.Contains("href=\"/me/llm-wiki/search\">의미 회상", errorPage);
        Assert.Contains("href=\"/me\">내 지식 로그", errorPage);
        Assert.Contains("흐름 추적 ID", errorPage);
        Assert.DoesNotContain("<PageTitle>오류</PageTitle>", errorPage);
        Assert.DoesNotContain("요청 처리 중 오류가 발생했습니다.", errorPage);
        Assert.DoesNotContain("href=\"/\">새 로그", errorPage);
        Assert.DoesNotContain("내 공개 로그", errorPage);
    }

    private static string FindRepoFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file: {Path.Combine(relativeSegments)}");
    }
}
