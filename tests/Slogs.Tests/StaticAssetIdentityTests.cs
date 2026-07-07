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
        var llmWikiService = File.ReadAllText(FindRepoFile("src", "Slogs", "Data", "LlmWikiService.cs"));

        Assert.DoesNotContain("게시글 생성", apiClient);
        Assert.Contains("로그 생성에 실패했습니다.", apiClient);

        Assert.DoesNotContain("Slogs 글", postMcpTools);
        Assert.Contains("Slogs 로그 업데이트에 실패했습니다.", postMcpTools);
        Assert.Contains("Slogs 로그 삭제에 실패했습니다.", postMcpTools);
        Assert.Contains("Slogs 로그 slug가 필요합니다.", postMcpTools);
        Assert.Contains("수정할 수 있는 Slogs 로그를 찾지 못했습니다.", postMcpTools);
        Assert.Contains("Saves a Markdown Slogs log before public sharing", postMcpTools);
        Assert.Contains("Pre-publish logs are visible only to the owner", postMcpTools);
        Assert.Contains("Share a Markdown Slogs log publicly", postMcpTools);
        Assert.Contains("confirm public sharing before calling this", postMcpTools);
        Assert.Contains("Read an owned or public Slogs log by slug", postMcpTools);
        Assert.Contains("# Slogs Log Saved Before Public Sharing", postMcpTools);
        Assert.Contains("# Slogs Log Shared Publicly", postMcpTools);
        Assert.Contains("The log is a public Slogs log, not an LLM Wiki entry.", postMcpTools);
        Assert.Contains("Slogs log MCP call", postMcpTools);
        Assert.Contains("Slogs 공개 공유에는 로그 제목이 필요합니다.", postMcpTools);
        Assert.Contains("Slogs 공개 공유에는 로그 Markdown 본문이 필요합니다.", postMcpTools);
        Assert.Contains("Status: {(post.IsDraft ? \"Before public sharing\" : \"Publicly shared\")}", postMcpTools);
        Assert.Contains("Former status: {(post.IsDraft ? \"Before public sharing\" : \"Publicly shared\")}", postMcpTools);
        Assert.DoesNotContain("Slogs post", postMcpTools);
        Assert.DoesNotContain("Slogs posts", postMcpTools);
        Assert.DoesNotContain("site post", postMcpTools);
        Assert.DoesNotContain("# Slogs Post", postMcpTools);
        Assert.DoesNotContain("publish publicly", postMcpTools);
        Assert.DoesNotContain("publicly publish", postMcpTools);
        Assert.DoesNotContain("Slogs post MCP call", postMcpTools);
        Assert.DoesNotContain("Slogs 게시에는 제목", postMcpTools);
        Assert.DoesNotContain("Slogs 게시에는 Markdown", postMcpTools);
        Assert.DoesNotContain("Status: {(post.IsDraft ? \"Pre-publish\" : \"Published\")}", postMcpTools);
        Assert.DoesNotContain("Former status: {(post.IsDraft ? \"Pre-publish\" : \"Publicly shared\")}", postMcpTools);

        Assert.DoesNotContain("검색어가 필요", llmWikiService);
        Assert.Contains("회상어가 필요합니다.", llmWikiService);
    }

    [Fact]
    public void AdminUserFiltersUseClueLanguageInsteadOfGenericSearch()
    {
        var adminUsersPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "AdminUsers.razor"));
        var navMenu = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Layout", "NavMenu.razor"));

        Assert.Contains("<PageTitle>어드민 슬로거 흐름 | slogs</PageTitle>", adminUsersPage);
        Assert.Contains(">어드민 슬로거 흐름</h1>", adminUsersPage);
        Assert.Contains("aria-label=\"어드민 슬로거 흐름 보기\"", adminUsersPage);
        Assert.Contains(">슬로거 홈 흐름</a>", adminUsersPage);
        Assert.Contains(">기억 회상 지표</a>", adminUsersPage);
        Assert.Contains(">노트 Vault 흐름</a>", adminUsersPage);
        Assert.Contains("aria-label=\"슬로거 홈 흐름 요약\"", adminUsersPage);
        Assert.Contains(">등록 슬로거</div>", adminUsersPage);
        Assert.Contains(">로그 흐름</div>", adminUsersPage);
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
        Assert.Contains("이어 볼 슬로거 홈 흐름이 없습니다.", adminUsersPage);
        Assert.Contains(">홈 열기</a>", adminUsersPage);
        Assert.Contains("@@name 정리 후 해당 슬로거는 다시 로그인해야 합니다.", adminUsersPage);
        Assert.Contains("\"정리 중\" : \"정리\"", adminUsersPage);
        Assert.Contains("aria-label=\"LLM Wiki 기억 요약\"", adminUsersPage);
        Assert.Contains("슬로거 홈과 공개/게시전 로그가 어떻게 이어지는지 확인합니다.", adminUsersPage);
        Assert.Contains("비공개 기억, 회상 접근, Agent 연결 품질 신호를 확인합니다.", adminUsersPage);
        Assert.Contains("로컬 노트 Vault, 노트 원문, 연결 기기 흐름을 확인합니다.", adminUsersPage);
        Assert.Contains(">기억 엔트리</div>", adminUsersPage);
        Assert.Contains(">기억 활동</div>", adminUsersPage);
        Assert.Contains(">7일 기억</div>", adminUsersPage);
        Assert.Contains(">30일 기억</div>", adminUsersPage);
        Assert.Contains("aria-label=\"MCP 회상 품질 지표\"", adminUsersPage);
        Assert.Contains(">LLM Wiki 회상 품질</h2>", adminUsersPage);
        Assert.Contains("최근 Agent 접근", adminUsersPage);
        Assert.Contains(">30일 Agent 접근</div>", adminUsersPage);
        Assert.Contains("후보 회상 접근", adminUsersPage);
        Assert.Contains(">유효 회상률</div>", adminUsersPage);
        Assert.Contains("빈 회상", adminUsersPage);
        Assert.Contains(">회상 속도</div>", adminUsersPage);
        Assert.Contains(">반복 회상률</div>", adminUsersPage);
        Assert.Contains("느린 회상", adminUsersPage);
        Assert.Contains(">기억 변경</div>", adminUsersPage);
        Assert.Contains(">접근</th>", adminUsersPage);
        Assert.Contains(">유효</th>", adminUsersPage);
        Assert.Contains(">최근 접근</th>", adminUsersPage);
        Assert.Contains("최근 MCP 회상 감사 로그가 없습니다.", adminUsersPage);
        Assert.Contains(">슬로거</div>", navMenu);
        Assert.Contains(">슬로거 홈 흐름</a>", navMenu);
        Assert.Contains(">기억 회상</a>", navMenu);
        Assert.Contains(">노트 Vault 흐름</a>", navMenu);
        Assert.Contains("<option value=\"entries\">기억 엔트리순</option>", adminUsersPage);
        Assert.Contains("<option value=\"accesses\">회상 접근순</option>", adminUsersPage);
        Assert.Contains("<option value=\"tokens\">Agent 연결순</option>", adminUsersPage);
        Assert.Contains("<option value=\"activity\">최근 기억순</option>", adminUsersPage);
        Assert.Contains("aria-label=\"LLM Wiki 기억 정렬\"", adminUsersPage);
        Assert.Contains("명 기억 흐름 표시", adminUsersPage);
        Assert.Contains(">기억 엔트리</th>", adminUsersPage);
        Assert.Contains(">근거 소스</th>", adminUsersPage);
        Assert.Contains(">기억 활동</th>", adminUsersPage);
        Assert.Contains(">7일 기억</th>", adminUsersPage);
        Assert.Contains(">30일 기억</th>", adminUsersPage);
        Assert.Contains(">회상 접근</th>", adminUsersPage);
        Assert.Contains(">Agent 연결</th>", adminUsersPage);
        Assert.Contains(">최근 기억</th>", adminUsersPage);
        Assert.Contains(">최근 회상</th>", adminUsersPage);
        Assert.Contains("이어 볼 LLM Wiki 기억 슬로거가 없습니다.", adminUsersPage);
        Assert.Contains("공개 로그 {user.PublishedPostCount:N0} / 게시전 로그 {user.DraftPostCount:N0}", adminUsersPage);

        Assert.DoesNotContain("placeholder=\"사용자 검색\"", adminUsersPage);
        Assert.DoesNotContain("사용자 검색어 입력", adminUsersPage);
        Assert.DoesNotContain("LLM Wiki 사용자 검색", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync 사용자 검색", adminUsersPage);
        Assert.DoesNotContain("어드민 사용자", adminUsersPage);
        Assert.DoesNotContain(">사용자 관리</a>", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"사용자 요약\"", adminUsersPage);
        Assert.DoesNotContain(">가입 사용자</div>", adminUsersPage);
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
        Assert.DoesNotContain(">사용자 관리</a>", navMenu);
        Assert.DoesNotContain(">슬로거 관리</a>", adminUsersPage);
        Assert.DoesNotContain(">슬로거 관리</a>", navMenu);
        Assert.DoesNotContain(">LLM Wiki 통계</a>", adminUsersPage);
        Assert.DoesNotContain(">Obsidian Sync</a>", adminUsersPage);
        Assert.DoesNotContain("LLM Wiki 사용량과 MCP 품질 지표를 확인합니다.", adminUsersPage);
        Assert.DoesNotContain("MCP 호출 품질 신호", adminUsersPage);
        Assert.DoesNotContain("aria-label=\"MCP 품질 지표\"", adminUsersPage);
        Assert.DoesNotContain(">LLM Wiki MCP 품질</h2>", adminUsersPage);
        Assert.DoesNotContain("최근 호출", adminUsersPage);
        Assert.DoesNotContain(">30일 호출</div>", adminUsersPage);
        Assert.DoesNotContain("후보 탐색 호출", adminUsersPage);
        Assert.DoesNotContain(">유효 결과율</div>", adminUsersPage);
        Assert.DoesNotContain("빈 결과", adminUsersPage);
        Assert.DoesNotContain(">응답 속도</div>", adminUsersPage);
        Assert.DoesNotContain(">재조회율</div>", adminUsersPage);
        Assert.DoesNotContain(">기록 변경</div>", adminUsersPage);
        Assert.DoesNotContain(">호출</th>", adminUsersPage);
        Assert.DoesNotContain(">성공</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 호출</th>", adminUsersPage);
        Assert.DoesNotContain("최근 MCP 감사 로그가 없습니다.", adminUsersPage);
        Assert.DoesNotContain("MCP 토큰순", adminUsersPage);
        Assert.DoesNotContain(">MCP 토큰</th>", adminUsersPage);
        Assert.DoesNotContain("게시전 로그 관리 신호", adminUsersPage);
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
        Assert.DoesNotContain("font-bold uppercase text-slate-500\">등록 슬로거", adminUsersPage);
        Assert.DoesNotContain(">엔트리순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"activity\">최근 활동순</option>", adminUsersPage);
        Assert.DoesNotContain(">조회순</option>", adminUsersPage);
        Assert.DoesNotContain("표시할 LLM Wiki 사용자가 없습니다.", adminUsersPage);
        Assert.DoesNotContain(">AGENT 연결</th>", adminUsersPage);
        Assert.DoesNotContain(">엔트리</div>", adminUsersPage);
        Assert.DoesNotContain(">활동</div>", adminUsersPage);
        Assert.DoesNotContain(">7일 활동</div>", adminUsersPage);
        Assert.DoesNotContain(">30일 활동</div>", adminUsersPage);
        Assert.DoesNotContain(">엔트리</th>", adminUsersPage);
        Assert.DoesNotContain(">소스</th>", adminUsersPage);
        Assert.DoesNotContain(">활동</th>", adminUsersPage);
        Assert.DoesNotContain(">조회</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 활동</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 조회</th>", adminUsersPage);
    }

    [Fact]
    public void LlmWikiRecallCardsUseRecallAccessAndMemoryFlowWording()
    {
        var llmWikiSearchPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWikiSearch.razor"));

        Assert.Contains(">기억 범주</p>", llmWikiSearchPage);
        Assert.Contains("기억 범주로 좁힌 뒤 아래 카드 그리드에서 이어 쓸 기억을 선택합니다.", llmWikiSearchPage);
        Assert.Contains("기억 범주가 없습니다.", llmWikiSearchPage);
        Assert.Contains("전체 기억", llmWikiSearchPage);
        Assert.Contains("모든 기억 범주", llmWikiSearchPage);
        Assert.Contains("개 기억 회상 중", llmWikiSearchPage);
        Assert.Contains("회 회상 접근", llmWikiSearchPage);
        Assert.Contains("다음 기억을 불러오는 중...", llmWikiSearchPage);
        Assert.Contains("더 이상 이어 볼 기억이 없습니다.", llmWikiSearchPage);
        Assert.Contains("선택한 기억 범주에 이어 볼 기억이 없습니다.", llmWikiSearchPage);
        Assert.Contains("저장된 비공개 기억이 없습니다.", llmWikiSearchPage);
        Assert.Contains("회상된 기억이 없습니다.", llmWikiSearchPage);

        Assert.DoesNotContain(">카테고리</p>", llmWikiSearchPage);
        Assert.DoesNotContain("카테고리로 좁힌", llmWikiSearchPage);
        Assert.DoesNotContain("카테고리가 없습니다.", llmWikiSearchPage);
        Assert.DoesNotContain("모든 카테고리", llmWikiSearchPage);
        Assert.DoesNotContain("개 표시 중", llmWikiSearchPage);
        Assert.DoesNotContain("회 열람", llmWikiSearchPage);
        Assert.DoesNotContain("다음 Wiki를 불러오는 중", llmWikiSearchPage);
        Assert.DoesNotContain("더 이상 표시할 Wiki", llmWikiSearchPage);
        Assert.DoesNotContain("표시할 LLM Wiki", llmWikiSearchPage);
    }

    [Fact]
    public void LlmWikiMemoryToLogBridgeUsesPrePublishLogWording()
    {
        var llmWikiGuidePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWiki.razor"));
        var llmWikiSearchPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWikiSearch.razor"));

        Assert.Contains("개인 LLM Wiki 기억을 회상하고 Slogs 게시전 로그로 이어 씁니다.", llmWikiSearchPage);
        Assert.Contains("소유자 전용 게시전 로그로 이어 씁니다.", llmWikiSearchPage);
        Assert.Contains("비공개 기억을 바로 공개하지 않고 소유자 전용 게시전 로그로 옮긴 뒤", llmWikiSearchPage);
        Assert.Contains("게시전 로그로 이어쓰기", llmWikiSearchPage);
        Assert.Contains("게시전 로그 여는 중...", llmWikiSearchPage);
        Assert.Contains("data-llm-wiki-draft-action-boundary=\"true\"", llmWikiSearchPage);
        Assert.Contains("비공개 기억 -> 소유자 전용 게시전 로그 -> 검토 후 공개 공유", llmWikiSearchPage);
        Assert.Contains("소유자 전용 게시전 로그", llmWikiSearchPage);
        Assert.Contains("이 게시전 로그는 Slogs LLM Wiki에서 이어온 소유자 전용 흐름입니다.", llmWikiSearchPage);
        Assert.Contains("소유자 전용 게시전 로그로 이어집니다.", llmWikiGuidePage);
        Assert.Contains("<span>게시전 로그</span>", llmWikiGuidePage);

        Assert.DoesNotContain("Slogs 로그 초안으로 이어 씁니다.", llmWikiSearchPage);
        Assert.DoesNotContain("게시전 로그 초안", llmWikiSearchPage);
        Assert.DoesNotContain("초안 생성 중...", llmWikiSearchPage);
        Assert.DoesNotContain("로그 초안으로 이어쓰기", llmWikiSearchPage);
        Assert.DoesNotContain("이 초안은 Slogs LLM Wiki", llmWikiSearchPage);
        Assert.DoesNotContain("소유자 전용 로그 초안", llmWikiGuidePage);
        Assert.DoesNotContain("<span>로그 초안</span>", llmWikiGuidePage);
        Assert.DoesNotContain("즉시 공개", llmWikiSearchPage);
    }

    [Fact]
    public void LlmWikiUsageGuideFramesToolNamesAsRecallFlow()
    {
        var llmWikiGuidePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "LlmWiki.razor"));
        var navMenu = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Layout", "NavMenu.razor"));

        Assert.Contains("LLM Wiki 기억 연결", llmWikiGuidePage);
        Assert.Contains("비공개 기억을 Agent 회상과 Slogs 게시전 로그로 이어 두는 연결면입니다.", llmWikiGuidePage);
        Assert.Contains(">비공개 기억</div>", navMenu);
        Assert.Contains("기억 연결 가이드", navMenu);
        Assert.Contains("Slogs LLM Wiki에서 먼저 관련 기억을 회상합니다.", llmWikiGuidePage);
        Assert.Contains("search</code> 도구는 회상 후보 흐름을 압축해 보여 주고", llmWikiGuidePage);
        Assert.Contains("recall</code> 도구는 답변/구현에 바로 적용할 기억 맥락으로 이어 줍니다.", llmWikiGuidePage);
        Assert.Contains("MCP 회상 응답의 Retrieval Diagnostics", llmWikiGuidePage);
        Assert.Contains("저장 전에는 관련 기억을 먼저 회상하고", llmWikiGuidePage);
        Assert.Contains("tool_search</code> 같은 도구 노출 확인", llmWikiGuidePage);
        Assert.Contains("search</code>로 작은 회상 후보 흐름을 잡습니다.", llmWikiGuidePage);
        Assert.Contains("답변이나 구현에 바로 적용할 기억 맥락은 낮은 limit의", llmWikiGuidePage);
        Assert.Contains("다시 회상합니다.", llmWikiGuidePage);

        Assert.DoesNotContain("LLM Wiki 사용법", navMenu);
        Assert.DoesNotContain("님의 기억을 회상하고 Slogs 로그로 이어 쓰는 방법입니다.", llmWikiGuidePage);
        Assert.DoesNotContain("Slogs LLM Wiki를 먼저 조회합니다.", llmWikiGuidePage);
        Assert.DoesNotContain("MCP 응답의 Retrieval Diagnostics", llmWikiGuidePage);
        Assert.DoesNotContain("저장 전에는 관련 기억을 먼저 찾고", llmWikiGuidePage);
        Assert.DoesNotContain("다시 조회합니다.", llmWikiGuidePage);
        Assert.DoesNotContain("회상 후보 요약 목록", llmWikiGuidePage);
        Assert.DoesNotContain("초기 목록", llmWikiGuidePage);
        Assert.DoesNotContain("작은 회상 후보 목록", llmWikiGuidePage);
        Assert.DoesNotContain("압축 컨텍스트로 구분합니다.", llmWikiGuidePage);
    }

    [Fact]
    public void SettingsPageFramesConnectionLayerAsKnowledgeLogFlow()
    {
        var settingsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Settings.razor"));
        var settingsComponent = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "LlmWikiMcpSettings.razor"));

        Assert.Contains("지식 로그 연결", settingsPage);
        Assert.Contains("공개 로그 흐름을 한 연결 계층으로 이어 둡니다.", settingsPage);
        Assert.Contains("프로필, Agent, 기억, 로컬 노트, 공개 로그 흐름을 한 연결 계층으로 이어 둡니다.", settingsPage);
        Assert.Contains("기억과 노트가 로그로 이어지는 경로", settingsPage);
        Assert.Contains("Agent는 비공개 기억을 회상하고, Obsidian은 로컬 노트를 원격 노트 Vault에 남기며", settingsPage);
        Assert.Contains("비공개 기억을 회상해 소유자 전용 게시전 로그로 이어 둡니다.", settingsPage);
        Assert.Contains("검토 가능한 소유자 전용 게시전 로그를 만들고", settingsComponent);
        Assert.Contains("Slogs MCP 연결 주소", settingsComponent);
        Assert.Contains("Agent 회상 권한 헤더", settingsComponent);
        Assert.Contains("Agent 연결 설정 예시", settingsComponent);
        Assert.Contains("노트 Vault 플러그인 ID", settingsComponent);
        Assert.Contains("Slogs Drive 설치 흐름", settingsComponent);
        Assert.Contains("Slogs Drive 실행 흐름", settingsComponent);

        Assert.DoesNotContain("공개 로그 연결을 설정합니다.", settingsPage);
        Assert.DoesNotContain("공개 로그 흐름을 관리합니다.", settingsPage);
        Assert.DoesNotContain("게시전 로그 초안", settingsPage);
        Assert.DoesNotContain("게시전 로그 초안", settingsComponent);
        Assert.DoesNotContain(">Endpoint</p>", settingsComponent);
        Assert.DoesNotContain(">Authorization Header</p>", settingsComponent);
        Assert.DoesNotContain(">Client Config Example</p>", settingsComponent);
        Assert.DoesNotContain(">Plugin ID</p>", settingsComponent);
        Assert.DoesNotContain(">Drive install</p>", settingsComponent);
        Assert.DoesNotContain(">Drive run</p>", settingsComponent);
    }

    [Fact]
    public void ObsidianVaultCardsUseNoteFlowAndConnectedDeviceWording()
    {
        var settingsComponent = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "LlmWikiMcpSettings.razor"));

        Assert.Contains("노트 흐름 v@vault.CurrentVersion", settingsComponent);
        Assert.Contains("활성 노트 @status.ActiveFileCount", settingsComponent);
        Assert.Contains("삭제 흔적 @status.DeletedFileCount", settingsComponent);
        Assert.Contains("노트 원문, 삭제 흔적, 연결 기기 상태, 노트 버전 이력", settingsComponent);
        Assert.Contains("노트 흐름 v@client.LastSeenVersion", settingsComponent);

        Assert.DoesNotContain(">v@vault.CurrentVersion ·", settingsComponent);
        Assert.DoesNotContain(">활성 @status.ActiveFileCount", settingsComponent);
        Assert.DoesNotContain(">삭제 기록 @status.DeletedFileCount", settingsComponent);
        Assert.DoesNotContain("파일, 삭제 기록, 클라이언트 상태, 버전 이력", settingsComponent);
        Assert.DoesNotContain("@client.ClientKind · v@client.LastSeenVersion", settingsComponent);
    }

    [Fact]
    public void AdminObsidianMetricsUseNoteVaultAndConnectedDeviceWording()
    {
        var adminUsersPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "AdminUsers.razor"));

        Assert.Contains("노트 Vault 흐름 요약", adminUsersPage);
        Assert.Contains("노트 Vault 슬로거", adminUsersPage);
        Assert.Contains("노트 Vault", adminUsersPage);
        Assert.Contains("활성 노트", adminUsersPage);
        Assert.Contains("삭제 흔적", adminUsersPage);
        Assert.Contains("연결 기기", adminUsersPage);
        Assert.Contains("노트 용량 흐름", adminUsersPage);
        Assert.Contains("aria-label=\"노트 Vault 용량 흐름\"", adminUsersPage);
        Assert.Contains(">노트 Vault 용량 흐름</h2>", adminUsersPage);
        Assert.Contains(">전체 Vault 한도 GiB</label>", adminUsersPage);
        Assert.Contains("aria-label=\"노트 Vault 전체 용량 한도 GiB\"", adminUsersPage);
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
        Assert.Contains("<option value=\"clients\">연결 기기순</option>", adminUsersPage);
        Assert.Contains("<option value=\"size\">노트 용량순</option>", adminUsersPage);
        Assert.Contains("<option value=\"name\">@@name순</option>", adminUsersPage);
        Assert.Contains("명 노트 흐름 표시", adminUsersPage);
        Assert.Contains(">노트 원문</th>", adminUsersPage);
        Assert.Contains(">노트 흐름</th>", adminUsersPage);
        Assert.Contains(">Vault 한도</th>", adminUsersPage);
        Assert.Contains(">Vault 여유</th>", adminUsersPage);
        Assert.Contains(">최근 Vault 흐름</th>", adminUsersPage);
        Assert.Contains(">최근 연결 기기</th>", adminUsersPage);
        Assert.Contains("이어 볼 노트 Vault 슬로거가 없습니다.", adminUsersPage);
        Assert.Contains("로컬 노트 Vault, 노트 원문, 연결 기기 흐름을 확인합니다.", adminUsersPage);

        Assert.DoesNotContain(">Sync 사용자</div>", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 사용자", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 사용자만", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 슬로거", adminUsersPage);
        Assert.DoesNotContain(">Vault</div>", adminUsersPage);
        Assert.DoesNotContain(">활성 파일</div>", adminUsersPage);
        Assert.DoesNotContain(">삭제 기록</div>", adminUsersPage);
        Assert.DoesNotContain(">클라이언트</div>", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync 노트 흐름 요약", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync 노트 Vault 용량 한도", adminUsersPage);
        Assert.DoesNotContain(">스토리지 한도</h2>", adminUsersPage);
        Assert.DoesNotContain(">전체 한도 GiB</label>", adminUsersPage);
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
        Assert.DoesNotContain("<option value=\"size\">용량순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"name\">아이디순</option>", adminUsersPage);
        Assert.DoesNotContain(">Vault</th>", adminUsersPage);
        Assert.DoesNotContain(">파일</th>", adminUsersPage);
        Assert.DoesNotContain(">활성</th>", adminUsersPage);
        Assert.DoesNotContain(">삭제</th>", adminUsersPage);
        Assert.DoesNotContain(">클라이언트</th>", adminUsersPage);
        Assert.DoesNotContain(">Version</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 Vault 변경</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 노트 Vault</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 클라이언트</th>", adminUsersPage);
        Assert.DoesNotContain("표시할 Obsidian Sync 사용자가 없습니다.", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync vault, 파일, 클라이언트 현황", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync 노트 Vault, 노트 원문, 연결 기기 흐름을 확인합니다.", adminUsersPage);
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
            Assert.Contains("제목으로 단서 추천", authoringPage);
            Assert.DoesNotContain("공유 주소 slug", authoringPage);
            Assert.DoesNotContain("제목으로 주소 추천", authoringPage);
        }
    }

    [Fact]
    public void AuthoringDraftFlowUsesPrePublishAndShareLanguage()
    {
        var writePost = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WritePost.razor"));
        var editPost = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "EditPost.razor"));

        Assert.Contains("게시전 저장", writePost);
        Assert.Contains("공개 공유", writePost);
        Assert.Contains("SaveDraft", writePost);
        Assert.Contains("SaveAsync(isDraft: true)", writePost);
        Assert.Contains("SaveAsync(isDraft: false)", writePost);
        Assert.Contains("isDraft: isDraft", writePost);

        Assert.Contains("게시전 로그 수정", editPost);
        Assert.Contains("게시전 저장", editPost);
        Assert.Contains("공개 공유", editPost);
        Assert.Contains("리비전 공유", editPost);
        Assert.Contains("SaveAsync(isDraft: true)", editPost);
        Assert.Contains("SaveAsync(isDraft: false)", editPost);
        Assert.Contains("post.IsDraft ? CurrentSlug : null", editPost);
        Assert.Contains("post.IsDraft ? $\"/edit/{Uri.EscapeDataString(post.Slug)}\" : GetPostUrl(post)", editPost);

        foreach (var authoringPage in new[] { writePost, editPost })
        {
            Assert.DoesNotContain("임시저장", authoringPage);
            Assert.DoesNotContain("게시하기", authoringPage);
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
        Assert.Contains("grid-template-areas: \"brand tools\";", appCss);
        Assert.Contains("grid-template-columns: minmax(0, max-content) minmax(0, 1fr);", appCss);
        Assert.Contains("grid-area: brand;", appCss);
        Assert.Contains("grid-area: tools;", appCss);
        Assert.Contains("grid-area: recall;", appCss);
        Assert.Contains("grid-area: actions;", appCss);
        Assert.Contains("justify-self: end;", appCss);
        Assert.Contains(".slogs-header-tools", appCss);
        Assert.Contains("display: flex;", appCss);
        Assert.Contains("flex-wrap: nowrap;", appCss);
        Assert.Contains("max-width: 100%;", appCss);
        Assert.Contains(".slogs-account-menu > summary", appCss);
        Assert.Contains("max-width: min(17rem, 34vw);", appCss);
        Assert.Contains("min-width: 0;", appCss);
        Assert.Contains("max-width: 4.85rem;", appCss);
        Assert.Contains("max-width: 4.25rem;", appCss);
        Assert.Contains("width: min(68rem, 100%);", appCss);
        Assert.Contains("flex: 1 1 36rem;", appCss);
        Assert.Contains("min-width: 18rem;", appCss);
        Assert.Contains("flex-basis: 32rem;", appCss);
        Assert.Contains("min-width: 14rem;", appCss);
        Assert.Contains("@media (max-width: 900px)", appCss);
        Assert.Contains(".slogs-brand__tagline {\n        display: none;\n    }", appCss);
        Assert.Contains("min-width: 8rem;", appCss);
        Assert.Contains("min-width: 7rem;", appCss);
        Assert.Contains("min-width: 6rem;", appCss);
        Assert.DoesNotContain("grid-template-areas: \"brand recall actions\";", appCss);
        Assert.DoesNotContain("grid-template-columns: minmax(0, max-content) minmax(12rem, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-template-columns: max-content minmax(0, 1fr);", appCss);
        Assert.DoesNotContain("grid-template-columns: max-content minmax(0, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-template-columns: minmax(11rem, max-content) minmax(14rem, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-template-columns: max-content minmax(16rem, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-template-columns: max-content minmax(12rem, 1fr) max-content;", appCss);
        Assert.DoesNotContain("grid-template-columns: max-content minmax(10rem, 1fr) max-content;", appCss);
        Assert.Contains("width: min(56rem, 100%);", appCss);
        Assert.Contains("max-width: 56rem;", appCss);
        Assert.Contains("width: min(52rem, 100%);", appCss);
        Assert.DoesNotContain("@media (max-width: 390px)", appCss);
        Assert.Contains("@media (max-width: 380px)", appCss);
        Assert.Contains("width: 2.2rem;", appCss);
        Assert.Contains("@media (max-width: 360px)", appCss);
        Assert.Contains(".slogs-brand__text {\n        display: none;\n    }", appCss);
        Assert.Contains("@media (max-width: 300px)", appCss);
        Assert.DoesNotContain("@media (max-width: 340px)", appCss);
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

        Assert.Contains("공개 로그와 지식 로그 홈", program);
        Assert.DoesNotContain("글과 프로필", program);

        Assert.Contains("소유자 전용 게시전 로그 흐름을 이어 봅니다.", profilePage);
        Assert.Contains("<PostFlowSignals Post=\"post\" />", profilePage);
        Assert.Contains("게시전 로그 수정", profilePage);
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
        Assert.Contains("회상 접근, 대화 흔적, 공감으로 다시 이어지는 공개 로그", homePage);
        Assert.Contains("FormatRecallAccessCount(post.ViewCount)", postDetailsPage);
        Assert.Contains("FormatRecallAccessCount(post.ViewCount)", profilePage);
        Assert.Contains("회상 접근", writerPage);

        Assert.DoesNotContain("<span>@Post.ViewCount</span>", postMetaLine);
        Assert.DoesNotContain("<span>@post.ViewCount</span>", postDetailsPage);
        Assert.DoesNotContain("<span>@post.ViewCount</span>", profilePage);
        Assert.DoesNotContain("조회, 대화 흔적, 공감", homePage);
        Assert.DoesNotContain("회상 진입", writerPage);
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
        Assert.Contains("모든 슬로거 홈 흐름을 불러왔습니다.", writerIndexPage);
        Assert.Contains("공개 로그 흐름을 남긴 슬로거가 아직 없습니다.", writerIndexPage);

        Assert.DoesNotContain("<PageTitle>슬로거</PageTitle>", writerIndexPage);
        Assert.DoesNotContain("찾고 로그 홈으로 이동합니다.", writerIndexPage);
        Assert.DoesNotContain(">로그 수</a>", writerIndexPage);
        Assert.DoesNotContain(">이름순</a>", writerIndexPage);
        Assert.DoesNotContain("(@item.Count)", writerIndexPage);
        Assert.DoesNotContain("모든 슬로거를 불러왔습니다.", writerIndexPage);
        Assert.DoesNotContain("공개 로그를 남긴 슬로거가 아직 없습니다.", writerIndexPage);
    }

    [Fact]
    public void PublicClueAndSeriesDiscoveryUseFlowSortLabels()
    {
        var tagIndexPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "TagIndex.razor"));
        var seriesIndexPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "SeriesIndex.razor"));

        Assert.Contains(">반복 단서</h1>", tagIndexPage);
        Assert.Contains(">단서명순</a>", tagIndexPage);
        Assert.Contains("모든 단서 흐름을 불러왔습니다.", tagIndexPage);
        Assert.Contains(">로그 시리즈</h1>", seriesIndexPage);
        Assert.Contains(">시리즈명순</a>", seriesIndexPage);
        Assert.Contains("모든 시리즈 흐름을 불러왔습니다.", seriesIndexPage);

        Assert.DoesNotContain(">이름순</a>", tagIndexPage);
        Assert.DoesNotContain(">이름순</a>", seriesIndexPage);
        Assert.DoesNotContain("모든 단서를 불러왔습니다.", tagIndexPage);
        Assert.DoesNotContain("모든 로그 시리즈를 불러왔습니다.", seriesIndexPage);
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
        Assert.Contains("의미 회상", homePage);
        Assert.Contains("사람과 AI가 이어 쓰는 공개 지식 로그 흐름입니다.", homePage);

        Assert.Contains(".slogs-home-flowline", appCss);
        Assert.Contains("border-top: 1px solid var(--theme-border);", appCss);
        Assert.Contains("border-bottom: 1px solid var(--theme-border);", appCss);
        Assert.Contains(".slogs-home-flowline__signals", appCss);
        Assert.Contains("@media (max-width: 640px)", appCss);

        Assert.DoesNotContain("slogs-home-hero", homePage);
        Assert.DoesNotContain("마케팅", homePage);
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
        Assert.Contains("공개 지식 로그 홈", writerPage);
        Assert.Contains("대표 기억 노드", writerPage);
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
        Assert.Contains("GetLinkedLogSignal()", postDetailsPage);
        Assert.Contains("본문과 대화 흔적은", postDetailsPage);
        Assert.Contains("이어진 지식 로그 노드입니다.", postDetailsPage);
        Assert.Contains("주요 단서 #", postDetailsPage);
        Assert.Contains("로그 시리즈", postDetailsPage);
        Assert.Contains("리비전 흐름 v", postDetailsPage);
        Assert.Contains("개 연결 로그", postDetailsPage);

        Assert.Contains(".post-detail-flowline", appCss);
        Assert.Contains(".post-detail-flowline__signals", appCss);
        Assert.Contains("border-top: 1px solid var(--theme-border);", appCss);
        Assert.Contains("border-bottom: 1px solid var(--theme-border);", appCss);
        Assert.Contains("@media (max-width: 640px)", appCss);

        Assert.DoesNotContain("post-detail-article-summary-card", postDetailsPage);
        Assert.DoesNotContain("관련 글", postDetailsPage);
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

        Assert.Contains("저장 로그", bookmarksPage);
        Assert.Contains("저장 해제", bookmarksPage);
        Assert.Contains("공감 로그", likesPage);
        Assert.Contains("공감 해제", likesPage);
    }

    [Fact]
    public void PersonalWorkspaceEmptyStatesOfferKnowledgeLogNextActions()
    {
        var icon = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "SlogsIcon.razor"));
        var profilePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Profile.razor"));
        var bookmarksPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "MyBookmarks.razor"));
        var likesPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "MyLikes.razor"));

        Assert.Contains("case \"plus\":", icon);

        Assert.Contains("아직 공개/게시전 로그 흐름이 없습니다.", profilePage);
        Assert.Contains("href=\"/write\"", profilePage);
        Assert.Contains("새 로그 남기기", profilePage);
        Assert.Contains("href=\"/me/llm-wiki/search\"", profilePage);
        Assert.Contains("기억에서 회상", profilePage);
        Assert.DoesNotContain("아직 남긴 로그가 없습니다.", profilePage);

        Assert.Contains("아직 저장 로그 흐름이 없습니다.", bookmarksPage);
        Assert.Contains("공개 흐름에서 다시 이어 읽을 로그", bookmarksPage);
        Assert.Contains("href=\"/post\"", bookmarksPage);
        Assert.Contains("공개 로그 흐름", bookmarksPage);
        Assert.Contains("href=\"/tag\"", bookmarksPage);
        Assert.Contains("단서 회상", bookmarksPage);
        Assert.DoesNotContain("저장한 로그가 없습니다.", bookmarksPage);

        Assert.Contains("아직 공감 로그 흐름이 없습니다.", likesPage);
        Assert.Contains("공감 신호", likesPage);
        Assert.Contains("href=\"/recommended\"", likesPage);
        Assert.Contains("추천 회상", likesPage);
        Assert.Contains("href=\"/post\"", likesPage);
        Assert.DoesNotContain("공감한 로그가 없습니다.", likesPage);
    }

    [Fact]
    public void WriterConnectionPagesUseRelationshipFlowLanguage()
    {
        var writerConnectionsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WriterConnections.razor"));
        var writerPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "WriterPage.razor"));

        Assert.Contains("로그 홈으로 들어오는", writerConnectionsPage);
        Assert.Contains("이어 둔", writerConnectionsPage);
        Assert.Contains("공개 로그 홈이 누구에게 이어지고", writerConnectionsPage);
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
        Assert.Contains("이어 둔 로그 흐름은 로그인 후 이용 가능합니다.", homePage);
        Assert.Contains("아직 이어 둔 로그 홈이 없습니다.", homePage);
        Assert.Contains("이어 둔 로그 흐름에서", homePage);
        Assert.Contains("관계로 이어 둔 슬로거의 공개 로그 흐름", homePage);
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
        Assert.Contains("공개 &#64;주소", registerPage);
        Assert.Contains("프로필 이미지 URL", registerPage);
        Assert.Contains("짧은 소개", registerPage);
        Assert.Contains("공개 @주소와 비밀번호는 필수입니다.", registerPage);
        Assert.Contains("이미 사용 중인 공개 @주소입니다.", registerPage);
        Assert.Contains("지식 로그 홈 생성 처리 중 오류가 발생했습니다.", registerPage);
        Assert.Contains("profileImageUrl", registerPage);
        Assert.Contains("bio = profileBio", registerPage);
        Assert.DoesNotContain("회원가입에 실패했습니다.", registerPage);
        Assert.DoesNotContain("아이디와 비밀번호는 필수입니다.", registerPage);

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
        Assert.Contains("내 비공개 기억, 게시전 로그, 저장 로그와 공감 로그", loginPage);
        Assert.Contains("공개 @주소", loginPage);
        Assert.Contains("로그 흐름으로 돌아가기", loginPage);
        Assert.Contains("Google로 지식 로그 이어가기", loginPage);
        Assert.Contains("지식 로그 홈 만들기", loginPage);
        Assert.Contains("지식 로그 흐름으로 돌아가지 못했습니다.", loginPage);
        Assert.DoesNotContain(">아이디<", loginPage);
        Assert.DoesNotContain("아이디와 비밀번호", loginPage);
        Assert.DoesNotContain("회원가입", loginPage);
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
        Assert.Contains("흐름 추적 ID", errorPage);
        Assert.DoesNotContain("<PageTitle>오류</PageTitle>", errorPage);
        Assert.DoesNotContain("요청 처리 중 오류가 발생했습니다.", errorPage);
        Assert.DoesNotContain("href=\"/\">새 로그", errorPage);
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
