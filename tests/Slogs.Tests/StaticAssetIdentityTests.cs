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
        Assert.Contains(">슬로거 관리</a>", adminUsersPage);
        Assert.Contains(">기억 회상 지표</a>", adminUsersPage);
        Assert.Contains(">노트 Vault 흐름</a>", adminUsersPage);
        Assert.Contains("aria-label=\"슬로거 요약\"", adminUsersPage);
        Assert.Contains(">등록 슬로거</div>", adminUsersPage);
        Assert.Contains("placeholder=\"슬로거 단서\"", adminUsersPage);
        Assert.Contains("aria-label=\"슬로거 단서 입력\"", adminUsersPage);
        Assert.Contains("placeholder=\"LLM Wiki 슬로거 단서\"", adminUsersPage);
        Assert.Contains("aria-label=\"LLM Wiki 슬로거 단서 입력\"", adminUsersPage);
        Assert.Contains("placeholder=\"Obsidian Sync 슬로거 단서\"", adminUsersPage);
        Assert.Contains("aria-label=\"Obsidian Sync 슬로거 단서 입력\"", adminUsersPage);
        Assert.Contains("기억 슬로거만", adminUsersPage);
        Assert.Contains("노트 Sync 슬로거만", adminUsersPage);
        Assert.Contains("슬로거 홈, 공개 로그, 게시전 로그 관리 신호를 확인합니다.", adminUsersPage);
        Assert.Contains("비공개 기억, 회상 접근, MCP 호출 품질 신호를 확인합니다.", adminUsersPage);
        Assert.Contains("로컬 노트 Vault, 노트 원문, 연결 기기 흐름을 확인합니다.", adminUsersPage);
        Assert.Contains(">슬로거</div>", navMenu);
        Assert.Contains(">슬로거 관리</a>", navMenu);
        Assert.Contains(">기억 회상</a>", navMenu);
        Assert.Contains(">노트 Vault 흐름</a>", navMenu);
        Assert.Contains("<option value=\"entries\">기억 엔트리순</option>", adminUsersPage);
        Assert.Contains("<option value=\"accesses\">회상 접근순</option>", adminUsersPage);
        Assert.Contains(">기억 엔트리</th>", adminUsersPage);
        Assert.Contains(">근거 소스</th>", adminUsersPage);
        Assert.Contains(">기억 활동</th>", adminUsersPage);
        Assert.Contains(">7일 기억</th>", adminUsersPage);
        Assert.Contains(">30일 기억</th>", adminUsersPage);
        Assert.Contains(">회상 접근</th>", adminUsersPage);
        Assert.Contains(">최근 기억</th>", adminUsersPage);
        Assert.Contains(">최근 회상</th>", adminUsersPage);
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
        Assert.DoesNotContain("LLM Wiki 사용자만", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 사용자만", adminUsersPage);
        Assert.DoesNotContain("사용자 기본 정보와 사용자 관련 관리 기능을 확인합니다.", adminUsersPage);
        Assert.DoesNotContain(">사용자 관리</a>", navMenu);
        Assert.DoesNotContain(">LLM Wiki 통계</a>", adminUsersPage);
        Assert.DoesNotContain(">Obsidian Sync</a>", adminUsersPage);
        Assert.DoesNotContain("LLM Wiki 사용량과 MCP 품질 지표를 확인합니다.", adminUsersPage);
        Assert.DoesNotContain("Obsidian Sync 노트 Vault, 노트 원문, 연결 기기 흐름을 확인합니다.", adminUsersPage);
        Assert.DoesNotContain(">LLM Wiki</a>", navMenu);
        Assert.DoesNotContain(">Obsidian Sync</a>", navMenu);
        Assert.DoesNotContain("공개 {user.PublishedPostCount:N0} / 초안 {user.DraftPostCount:N0}", adminUsersPage);
        Assert.DoesNotContain(">엔트리순</option>", adminUsersPage);
        Assert.DoesNotContain(">조회순</option>", adminUsersPage);
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
    public void SettingsPageFramesConnectionLayerAsKnowledgeLogFlow()
    {
        var settingsPage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Settings.razor"));

        Assert.Contains("지식 로그 연결", settingsPage);
        Assert.Contains("공개 로그 흐름을 한 연결 계층으로 이어 둡니다.", settingsPage);
        Assert.Contains("프로필, Agent, 기억, 로컬 노트, 공개 로그 흐름을 한 연결 계층으로 이어 둡니다.", settingsPage);
        Assert.Contains("기억과 노트가 로그로 이어지는 경로", settingsPage);
        Assert.Contains("Agent는 비공개 기억을 회상하고, Obsidian은 로컬 노트를 원격 노트 Vault에 남기며", settingsPage);

        Assert.DoesNotContain("공개 로그 연결을 설정합니다.", settingsPage);
        Assert.DoesNotContain("공개 로그 흐름을 관리합니다.", settingsPage);
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

        Assert.Contains("Obsidian Sync 노트 흐름 요약", adminUsersPage);
        Assert.Contains("노트 Sync 슬로거", adminUsersPage);
        Assert.Contains("노트 Vault", adminUsersPage);
        Assert.Contains("활성 노트", adminUsersPage);
        Assert.Contains("삭제 흔적", adminUsersPage);
        Assert.Contains("연결 기기", adminUsersPage);
        Assert.Contains("노트 용량", adminUsersPage);
        Assert.Contains("Obsidian Sync 노트 Vault 용량 한도", adminUsersPage);
        Assert.Contains("노트 Sync 슬로거만", adminUsersPage);
        Assert.Contains("<option value=\"updated\">최근 노트 흐름순</option>", adminUsersPage);
        Assert.Contains("<option value=\"vaults\">노트 Vault순</option>", adminUsersPage);
        Assert.Contains("<option value=\"files\">노트 원문순</option>", adminUsersPage);
        Assert.Contains("<option value=\"clients\">연결 기기순</option>", adminUsersPage);
        Assert.Contains(">노트 원문</th>", adminUsersPage);
        Assert.Contains(">노트 흐름</th>", adminUsersPage);
        Assert.Contains(">최근 노트 Vault</th>", adminUsersPage);
        Assert.Contains(">최근 연결 기기</th>", adminUsersPage);
        Assert.Contains("로컬 노트 Vault, 노트 원문, 연결 기기 흐름을 확인합니다.", adminUsersPage);

        Assert.DoesNotContain(">Sync 사용자</div>", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 사용자", adminUsersPage);
        Assert.DoesNotContain("노트 Sync 사용자만", adminUsersPage);
        Assert.DoesNotContain(">Vault</div>", adminUsersPage);
        Assert.DoesNotContain(">활성 파일</div>", adminUsersPage);
        Assert.DoesNotContain(">삭제 기록</div>", adminUsersPage);
        Assert.DoesNotContain(">클라이언트</div>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"updated\">최근 동기화순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"vaults\">Vault순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"files\">파일순</option>", adminUsersPage);
        Assert.DoesNotContain("<option value=\"clients\">클라이언트순</option>", adminUsersPage);
        Assert.DoesNotContain(">Vault</th>", adminUsersPage);
        Assert.DoesNotContain(">파일</th>", adminUsersPage);
        Assert.DoesNotContain(">활성</th>", adminUsersPage);
        Assert.DoesNotContain(">삭제</th>", adminUsersPage);
        Assert.DoesNotContain(">클라이언트</th>", adminUsersPage);
        Assert.DoesNotContain(">Version</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 Vault 변경</th>", adminUsersPage);
        Assert.DoesNotContain(">최근 클라이언트</th>", adminUsersPage);
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
        Assert.Contains("grid-template-areas: \"brand recall actions\";", appCss);
        Assert.Contains("grid-template-columns: minmax(0, max-content) minmax(12rem, 1fr) max-content;", appCss);
        Assert.Contains("grid-area: brand;", appCss);
        Assert.Contains("grid-area: recall;", appCss);
        Assert.Contains("grid-area: actions;", appCss);
        Assert.Contains("justify-self: end;", appCss);
        Assert.Contains(".slogs-header-tools", appCss);
        Assert.Contains("display: contents;", appCss);
        Assert.Contains(".slogs-account-menu > summary", appCss);
        Assert.Contains("max-width: min(17rem, 34vw);", appCss);
        Assert.Contains("grid-template-columns: minmax(0, max-content) minmax(0, 1fr) max-content;", appCss);
        Assert.Contains("min-width: 0;", appCss);
        Assert.Contains("max-width: 4.85rem;", appCss);
        Assert.Contains("max-width: 4.25rem;", appCss);
        Assert.DoesNotContain("grid-template-areas: \"brand tools\";", appCss);
        Assert.DoesNotContain("grid-area: tools;", appCss);
        Assert.DoesNotContain("flex-wrap: nowrap;", appCss);
        Assert.DoesNotContain("max-width: min(68rem, 100%);", appCss);
        Assert.DoesNotContain("flex: 1 1 36rem;", appCss);
        Assert.DoesNotContain("min-width: 18rem;", appCss);
        Assert.DoesNotContain("flex-basis: 32rem;", appCss);
        Assert.DoesNotContain("min-width: 14rem;", appCss);
        Assert.DoesNotContain("min-width: 8rem;", appCss);
        Assert.DoesNotContain("min-width: 7rem;", appCss);
        Assert.DoesNotContain("min-width: 6rem;", appCss);
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
        Assert.Contains("@media (max-width: 340px)", appCss);
        Assert.Contains("\"recall recall\";", appCss);
        Assert.Contains("max-width: none;", appCss);
    }

    [Fact]
    public void PersonalWorkspaceCardsUseLogFlowSignals()
    {
        var program = File.ReadAllText(FindRepoFile("src", "Slogs", "Program.cs"));
        var profilePage = File.ReadAllText(FindRepoFile("src", "Slogs.Client", "Components", "Pages", "Profile.razor"));

        Assert.Contains("공개 로그와 지식 로그 홈", program);
        Assert.DoesNotContain("글과 프로필", program);

        Assert.Contains("<PostFlowSignals Post=\"post\" />", profilePage);
        Assert.Contains("게시전 로그 수정", profilePage);
        Assert.Contains("새 리비전 남기기", profilePage);
        Assert.DoesNotContain("SlogsIcon Name=\"heart\"", profilePage);
        Assert.DoesNotContain("SlogsIcon Name=\"message-circle\"", profilePage);
        Assert.DoesNotContain("로그 시리즈: @series", profilePage);
        Assert.DoesNotContain("FormatUserName", profilePage);
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
            Assert.DoesNotContain("<PostMetaLine", page);
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
