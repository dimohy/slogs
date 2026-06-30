namespace Slogs.Data;

public static class SlogsMcpPolicyPrompt
{
    public const string Version = "2026.06.30.1";
    public const string McpPath = "/mcp";
    public const string PublicPath = "/prompts/slogs-mcp.md";
    public const string KoreanPublicPath = "/prompts/slogs-mcp.ko.md";
    public const string EnglishPublicPath = "/prompts/slogs-mcp.en.md";
    public const string VersionPath = "/prompts/slogs-mcp.version";
    public const string DefaultPublicBaseUrl = "https://slogs.dev";
    public const string DefaultMcpUrl = $"{DefaultPublicBaseUrl}{McpPath}";
    public const string DefaultPublicUrl = $"{DefaultPublicBaseUrl}{PublicPath}";
    public const string DefaultKoreanPublicUrl = $"{DefaultPublicBaseUrl}{KoreanPublicPath}";
    public const string DefaultEnglishPublicUrl = $"{DefaultPublicBaseUrl}{EnglishPublicPath}";
    public const string DefaultVersionUrl = $"{DefaultPublicBaseUrl}{VersionPath}";

    public const string KoreanAgentsPolicyBlock = """
        - 이전 결정, 선호, 판단 기준, 프로젝트 맥락, 작업 기억이 관련될 수 있으면 Slogs LLM Wiki를 먼저 조회한다. 단순 현재 시각, 간단 번역, 일회성 명령은 예외로 둘 수 있다.
        - 후보 선별, 넓은 주제, 카테고리 필터링에는 `llm_wiki_search`를 작게 사용한다. 답변이나 구현에 바로 적용할 문맥은 `llm_wiki_recall`을 작은 limit으로 사용한다. 전체 원문이나 Raw Provenance가 필요할 때만 결과 id를 골라 `llm_wiki_read`를 호출한다.
        - `recall`, `search`, `find_related`, `capture`의 Retrieval Diagnostics에서 결과 수, effectiveLimit, categoryPath, minRelevancePercent, elapsedMs를 확인한다. 결과가 엉뚱하거나 누락, 과다, 지연이면 query/categoryPath/limit/minRelevancePercent를 좁혀 다시 조회하고, 판단에 영향이 있으면 최종 답변에 짧게 밝힌다.
        - 저장 전에는 `llm_wiki_instructions`를 확인하고 `llm_wiki_capture` 또는 `llm_wiki_find_related`로 관련 항목을 찾는다. 관련 항목이 있으면 `llm_wiki_read` 후 최종 문구를 작성해 `llm_wiki_merge` 또는 `llm_wiki_update`를 사용하고, 관련 항목이 없을 때만 `llm_wiki_remember`를 사용한다.
        - 사용자가 매번 기억 여부를 말하지 않아도 장기적으로 문서화, 자동화, 재현, 의사결정에 다시 쓸 수 있는 암묵지는 저장 후보로 조용히 점검한다. 사용자 정정 용어, 판단 기준, 반복 워크플로, 운영 규칙, 검증된 원인/결정, 재시작 지점, 코드만 보고 알기 어려운 전제조건이 대표 예다.
        - 기억을 병합하거나 갱신하더라도 기존 Raw Provenance를 임의로 삭제하거나 요약본만 남기지 않는다. 현재 Content/Source Prompt는 읽기 좋은 통합 기억이고, 원래 저장/merge/update 요청의 raw prompt/content/title/tags/categoryPath는 감사 가능한 근거로 보존되어야 한다.
        - 민감 정보, API 키, 비밀번호, 토큰, 일회성 로그, 임시 실행 내역, 검증되지 않은 추측, 현재 파일에서 쉽게 다시 알 수 있는 단순 사실, 이번 턴에만 의미 있는 중간 상태는 저장하지 않는다.
        - LLM Wiki 항목은 기본 비공개다. 사용자가 본인의 특정 주제를 명시적으로 공개하라고 요청한 경우에만 `llm_wiki_make_public`으로 관련 항목을 공개한다. 공개 조회는 `llm_wiki_public_list/search/read/recall` 결과에 한정해 답하고, public 도구가 반환하지 않은 민감 정보는 추측하거나 private 조회로 대체하지 않는다.
        - 질문에 `@username`이 나오고 공개 LLM Wiki 정보가 맥락이면 이를 Slogs 사용자 핸들로 해석해 public 도구의 `ownerUserName`에 전달하고, 나머지 주제어를 query로 사용한다. 결과가 없으면 공개된 정보가 없다고 답한다.
        - 기억을 저장, 병합, 갱신할 때는 프로젝트/영역/주제가 알려진 경우 2-4단계 소문자 slash-separated `categoryPath`를 명시한다. 예: `slogs/llm-wiki/graphrag`, `slogs/deployment/wasm-aot`, `preference/coding-policy/slogs`, `codex/mcp/slogs`.
        - Slogs 슬로그(블로그 글) 작성이나 업로드 요청은 기본적으로 `slogs_post_save_draft`로 게시전(소유자 전용, 공개 미노출) 상태로 저장한다. 사용자가 공개 발행을 명시적으로 요청한 경우에만 `slogs_post_publish`를 사용하고, 호출 전에 공개 발행 여부를 확인한다. `slogs_post_*`는 LLM Wiki 기억이 아니라 일반 Slogs 글을 다룬다.
        """;

    public const string EnglishAgentsPolicyBlock = """
        - When prior decisions, preferences, judgment criteria, project context, or task memory may matter, query Slogs LLM Wiki first. Current-time checks, simple translations, and one-off commands may be treated as exceptions.
        - Use `llm_wiki_search` with a small limit for candidate selection, broad topics, and category filtering. Use `llm_wiki_recall` with a small limit when context must be applied directly to an answer or implementation. Use `llm_wiki_read` only for selected ids when full text or Raw Provenance is needed.
        - Inspect Retrieval Diagnostics from `recall`, `search`, `find_related`, and `capture`: result count, effectiveLimit, categoryPath, minRelevancePercent, and elapsedMs. If results are unrelated, missing, too broad, too large, or slow, narrow query/categoryPath/limit/minRelevancePercent and retry. Mention the mismatch briefly when it affects the decision.
        - Before storing, call `llm_wiki_instructions`, then use `llm_wiki_capture` or `llm_wiki_find_related`. If a related entry exists, call `llm_wiki_read`, compose the final wording, and use `llm_wiki_merge` or `llm_wiki_update`. Use `llm_wiki_remember` only when no related entry fits.
        - Even when the user does not explicitly ask to remember something, quietly review durable tacit knowledge before finishing meaningful work: corrected terminology, judgment criteria, repeatable workflows, operating rules, verified causes or decisions, restart points, and prerequisites that are not obvious from code.
        - When refining, merging, or updating memory, do not delete prior Raw Provenance or leave only a summary. The current Content/Source Prompt is the readable consolidated memory; raw prompt/content/title/tags/categoryPath from remember/merge/update requests must remain auditable.
        - Do not store secrets, API keys, passwords, tokens, one-time logs, temporary execution traces, unverified speculation, simple facts easily recovered from current files, or intermediate state that only matters during the current turn.
        - LLM Wiki entries are private by default. Only when the user explicitly asks to publish a specific topic from their own wiki may `llm_wiki_make_public` be used. Answer public questions only from `llm_wiki_public_list/search/read/recall`; do not infer sensitive information missing from public results and do not substitute private lookup results.
        - If a question contains `@username` and asks about public LLM Wiki context, treat it as a Slogs handle, pass it as `ownerUserName`, and use the remaining topic words as the query. If public tools return no results, say no public information was found.
        - When creating, merging, or updating memory, provide a 2-4 segment lowercase slash-separated `categoryPath` whenever the project/domain/topic is known. Examples: `slogs/llm-wiki/graphrag`, `slogs/deployment/wasm-aot`, `preference/coding-policy/slogs`, `codex/mcp/slogs`.
        - Treat Slogs post (blog) authoring or upload requests as draft-first: save with `slogs_post_save_draft` as a pre-publish, owner-only post that is not publicly listed. Use `slogs_post_publish` only when the user explicitly asks to publish publicly, and confirm public publication before calling it. `slogs_post_*` tools manage normal Slogs posts, not LLM Wiki memories.
        """;

    public static string BuildMarkdown()
        => BuildKoreanMarkdown();

    public static string BuildKoreanMarkdown()
        => $$"""
        # Slogs MCP / LLM Wiki Agent Prompt

        언어: 한국어
        Canonical URL: {{DefaultKoreanPublicUrl}}
        기본 호환 URL: {{DefaultPublicUrl}}
        Version URL: {{DefaultVersionUrl}}
        Prompt Version: {{Version}}

        이 문서는 Agent 지속 지침에 설치하는 Slogs MCP/LLM Wiki compact 정책이다. 기능을 줄이지 않기 위해 설치, 동기화, 도구 노출, 조회, 저장, 공개 Wiki, categoryPath 규칙을 모두 유지하되 런타임에 필요한 문장만 둔다.

        ## 설치와 범위

        - 처음 설치하거나 새 Slogs MCP 연결을 구성할 때는 먼저 현재 세션에 `llm_wiki_*` 또는 `mcp__slogs.*` 도구가 보이는지 확인한다. 보이면 키를 다시 묻지 말고 `llm_wiki_instructions`로 연결을 확인한다.
        - 도구가 보이지 않으면 키 요청이나 재설정 안내 전에 `tool_search`, MCP tool discovery, plugin/connector search 같은 도구 검색으로 `slogs llm_wiki`, `llm_wiki_instructions`, `llm_wiki_search`, `llm_wiki_recall`, `llm_wiki_remember`를 찾아 노출한다.
        - 검색 후에도 도구가 없고 새 연결이 필요할 때만 Slogs MCP 키를 요청한다. 키를 받은 뒤에는 영구 지침이나 MCP 설정을 자동 수정하지 말고 전역, 현재 프로젝트, 현재 세션 중 적용 범위를 먼저 묻는다.
        - 사용자가 선택한 범위에만 이 프롬프트와 `{{DefaultMcpUrl}}` MCP 연결을 함께 적용한다. 토큰 값은 응답, AGENTS.md, CLAUDE.md, 문서, 로그, LLM Wiki, 프롬프트 파일에 쓰지 않는다. 평문 MCP 설정에만 저장 가능하면 쓰기 전에 알리고 확인을 받는다.
        - Agent가 선택된 지침 위치나 MCP 설정을 직접 수정할 수 없으면 아직 영구 반영되지 않았다고 말하고, 사용자가 붙여넣을 위치와 endpoint/Authorization 헤더 형식을 안내한다.

        ## 버전 동기화

        - 새 Agent/Codex 세션이 시작되면 먼저 `{{DefaultVersionUrl}}`의 한 줄 버전만 읽어 설치된 `SLOGS_MCP_PROMPT` 관리 블록의 `version` 또는 본문 `Prompt Version`과 비교한다.
        - 버전이 같으면 전체 프롬프트를 다시 읽지 않고, 파일을 다시 쓰지 않고, 키 요청이나 범위 선택도 반복하지 않는다.
        - 버전이 다르거나 로컬 버전이 없을 때만 `{{DefaultKoreanPublicUrl}}` 또는 호환 URL `{{DefaultPublicUrl}}`의 전체 Markdown을 한 번 읽어 같은 지침 위치의 기존 관리 블록을 교체한다. 두 한국어 URL 차이만으로는 같은 버전에서 갱신하지 않는다.
        - 이 동기화는 세션 시작 시 버전 차이를 발견했을 때 수행하는 1회 작업이다. 주기 실행, 백그라운드 반복 실행, Windows Scheduled Task 등록으로 구현하지 않는다.
        - 중복 정책을 누적하지 말고 이전 `SLOGS_MCP_PROMPT` 관리 블록을 새 블록으로 교체한다. Codex는 전역/프로젝트 `AGENTS.md`, Claude는 Project instructions 또는 `CLAUDE.md`, GitHub Copilot은 repository instructions, 그 밖의 Agent는 가장 높은 우선순위의 지속 지침 위치를 사용한다.

        ## 런타임 규칙

        {{KoreanAgentsPolicyBlock}}
        """;

    public static string BuildEnglishMarkdown()
        => $$"""
        # Slogs MCP / LLM Wiki Agent Prompt

        Language: English
        Canonical URL: {{DefaultEnglishPublicUrl}}
        Version URL: {{DefaultVersionUrl}}
        Prompt Version: {{Version}}

        This is a compact persistent Agent policy for Slogs MCP and Slogs LLM Wiki. It preserves setup, sync, tool exposure, lookup, storage, public Wiki, and categoryPath behavior while keeping only runtime-essential wording.

        ## Installation And Scope

        - When installing for the first time or configuring a new Slogs MCP connection, first check whether `llm_wiki_*` or `mcp__slogs.*` tools are visible. If they are visible, do not ask for the key again; verify the connection with `llm_wiki_instructions`.
        - If tools are not visible, run tool discovery such as `tool_search`, MCP tool discovery, or plugin/connector search for `slogs llm_wiki`, `llm_wiki_instructions`, `llm_wiki_search`, `llm_wiki_recall`, and `llm_wiki_remember` before asking for a key or suggesting reconfiguration.
        - Only when discovery still exposes no tools and a new connection is needed should the Agent ask for the Slogs MCP key. After receiving it, do not automatically modify persistent instructions or MCP settings; first ask whether to apply globally, to the current project, or only to the current session.
        - Apply this prompt and the `{{DefaultMcpUrl}}` MCP connection only to the selected scope. Never write token values to responses, AGENTS.md, CLAUDE.md, docs, logs, LLM Wiki, or prompt files. If plaintext MCP settings are the only available storage, warn the user and ask before writing.
        - If the Agent cannot directly edit the chosen instruction or MCP configuration surface, state that it has not been permanently applied yet and provide the exact instruction location plus endpoint/Authorization header format for manual setup.

        ## Version Sync

        - When a new Agent/Codex session starts, first read only the one-line version from `{{DefaultVersionUrl}}` and compare it with the installed `SLOGS_MCP_PROMPT` managed block `version` or body `Prompt Version`.
        - If versions match, do not fetch the full prompt, rewrite files, ask for the key again, or repeat scope selection.
        - If versions differ or no local version exists, fetch the full Markdown from `{{DefaultEnglishPublicUrl}}` once and replace the previous managed block in the same instruction location.
        - This is a one-time session-start sync when a version difference is detected. Do not implement it as a periodic/background job or a Windows Scheduled Task.
        - Do not accumulate duplicate policy blocks. Codex uses global/project `AGENTS.md`; Claude uses Project instructions or `CLAUDE.md`; GitHub Copilot uses repository instructions; other Agents should use their highest-priority durable instruction surface.

        ## Runtime Rules

        {{EnglishAgentsPolicyBlock}}
        """;

    public static string BuildPromptUrl(string? publicBaseUrl = null)
    {
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? DefaultPublicBaseUrl
            : publicBaseUrl.Trim();

        return $"{baseUrl.TrimEnd('/')}{PublicPath}";
    }

    public static string BuildKoreanPromptUrl(string? publicBaseUrl = null)
    {
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? DefaultPublicBaseUrl
            : publicBaseUrl.Trim();

        return $"{baseUrl.TrimEnd('/')}{KoreanPublicPath}";
    }

    public static string BuildEnglishPromptUrl(string? publicBaseUrl = null)
    {
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? DefaultPublicBaseUrl
            : publicBaseUrl.Trim();

        return $"{baseUrl.TrimEnd('/')}{EnglishPublicPath}";
    }

    public static string BuildVersionUrl(string? publicBaseUrl = null)
    {
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? DefaultPublicBaseUrl
            : publicBaseUrl.Trim();

        return $"{baseUrl.TrimEnd('/')}{VersionPath}";
    }

    public static string BuildVersionText() => $"{Version}\n";
}
