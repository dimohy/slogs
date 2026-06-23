namespace Slogs.Data;

public static class SlogsMcpPolicyPrompt
{
    public const string Version = "2026.06.24.2";
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
        이전 결정, 사용자 선호, 판단 기준, 프로젝트 맥락, 작업 기억이 관련될 수 있는 질문은 Slogs LLM Wiki를 가장 먼저 조회한다. 단순 현재 시각, 간단 번역, 일회성 명령처럼 명백히 self-contained인 요청은 예외로 둘 수 있다.

        조회 도구는 토큰 예산과 목적에 맞게 구분한다. `llm_wiki_search`는 낮은 비용의 후보 선별 도구이며 관련도, 제목, 요약, 태그, categoryPath만 필요한 첫 조회에 사용한다. 넓은 주제, 애매한 질문, 카테고리 필터링, 엉뚱한 결과 여부 확인은 먼저 `search`로 작게 수행하고 필요한 항목만 `llm_wiki_read`로 연다.

        `llm_wiki_recall`은 이전 결정, 선호, 프로젝트 맥락을 실제 답변이나 구현에 바로 적용해야 할 때 사용하는 회상 도구다. `recall`은 `search`보다 더 많은 문맥을 반환하므로 기본 limit을 작게 유지한다. Raw Provenance나 전체 원문이 필요하면 `recall` 결과의 id를 골라 `llm_wiki_read`를 호출한다.

        `recall`, `search`, `find_related`, `capture` 응답의 Retrieval Diagnostics를 확인한다. 결과 수, effectiveLimit, categoryPath, minRelevancePercent, elapsedMs를 보고 조회 품질을 판단한다. 기대한 항목이 없거나 상위 결과가 엉뚱하거나, 조회가 느리거나, 응답이 너무 크면 좋은 조회로 간주하지 말고 query/categoryPath/limit/minRelevancePercent를 좁혀 다시 시도한다. 그 문제가 작업 판단에 영향을 주면 최종 답변에 mismatch를 짧게 밝힌다.

        저장 전에는 `llm_wiki_instructions`를 확인하고, `llm_wiki_capture` 또는 `llm_wiki_find_related`로 관련 항목을 먼저 찾는다. 관련 항목이 있으면 `llm_wiki_read` 후 최종 문구를 작성해 `llm_wiki_merge` 또는 `llm_wiki_update`를 사용한다. 관련 항목이 없을 때만 `llm_wiki_remember`를 사용한다.

        사용자가 매번 기억 여부를 지정하지 않아도, 에이전트는 의미 있는 작업이 끝나기 전에 조용히 저장 후보를 점검한다. 저장 후보는 장기적으로 LLM이 문서화, 자동화, 재현, 의사결정에 다시 사용할 수 있는 암묵지다. 예: 사용자가 정정한 용어와 판단 기준, 반복될 워크플로, 프로젝트 운영 규칙, 검증된 원인과 결정, 재시작 지점, 코드만 보고는 알기 어려운 전제조건, 나중에 runbook이나 자동화로 바꿀 수 있는 명령 흐름.

        기억을 정제, 병합, 갱신하더라도 검증 가능한 raw provenance가 남아야 한다. LLM Wiki의 현재 Content/Source Prompt는 읽기 좋은 통합 기억이고, 원래 저장 요청과 이후 merge/update 요청의 raw prompt/content/title/tags/categoryPath는 Raw Provenance로 보존되어 검색 품질, 병합 품질, 출처 왜곡 여부를 나중에 점검할 수 있어야 한다. 기존 raw 근거를 임의로 삭제하거나 요약본만 남기지 않는다.

        저장하지 않을 정보는 명확히 제외한다. 예: 민감 정보, API 키, 비밀번호, 토큰, 일회성 로그, 임시 실행 내역, 검증되지 않은 추측, 현재 파일에서 쉽게 다시 알 수 있는 단순 사실, 이번 턴의 진행상황만 의미 있는 중간 상태. 판단이 애매하면 사용자에게 방해가 되지 않는 범위에서 보수적으로 저장하지 않거나, 민감성/범위 때문에 필요한 경우에만 짧게 확인한다.

        기억을 저장, 병합, 갱신할 때는 프로젝트/영역/주제를 나타내는 `categoryPath`를 가능한 한 명시한다. 예: `slogs/llm-wiki/graphrag`, `slogs/deployment/wasm-aot`, `lingomotion/youtube/metadata`, `preference/coding-policy/slogs`, `codex/mcp/slogs`. 태그는 보조 라벨이고, 카테고리 경로가 주 구조다. 프로젝트나 주제를 알 수 있는데 `general`, `misc`, `notes` 같은 모호한 경로로 저장하지 않는다.

        Slogs MCP는 Agent 기억 서버로 사용한다. 이 프롬프트를 처음 설치하거나 Slogs MCP 연결을 새로 구성하는 Agent는 Slogs MCP 키를 입력받고, 전역/프로젝트/현재 세션 중 사용자가 선택한 범위에 맞춰 MCP 연결과 정책 지침을 함께 적용한다. 현재 Agent 세션의 도구 목록에 `llm_wiki_*` 도구가 이미 보이면 키를 다시 요구하지 말고 `llm_wiki_instructions`로 연결을 확인한다. 인증 토큰 값은 응답, 문서, 로그, LLM Wiki, 프롬프트 파일에 기록하지 않는다.

        Claude, GitHub Copilot, Codex, Cursor, Windsurf 같은 Agent마다 정책 반영 위치가 다를 수 있다. 이 문구를 가장 높은 우선순위의 지속 지침 위치에 반영한다. 예: Codex는 전역/프로젝트 `AGENTS.md`, Claude는 Project instructions 또는 `CLAUDE.md`, GitHub Copilot은 repository custom instructions 또는 `.github/copilot-instructions.md`, 그 밖의 Agent는 system/developer/custom instructions나 프로젝트 정책 파일을 사용한다. Agent가 Slogs MCP를 지원하면 MCP 도구를 연결하고, 지원하지 않으면 이 정책을 지침으로 유지하되 Slogs MCP 도구를 사용했다고 말하지 않는다.

        민감 정보, API 키, 비밀번호, 토큰, 일회성 로그, 임시 실행 내역은 Slogs LLM Wiki에 저장하지 않는다.
        """;

    public const string EnglishAgentsPolicyBlock = """
        When prior decisions, user preferences, judgment criteria, project context, or task memory may matter, query Slogs LLM Wiki first. Self-contained requests such as current-time checks, simple translations, or one-off commands may be treated as exceptions.

        Choose retrieval tools according to purpose and token budget. `llm_wiki_search` is the low-cost candidate-selection tool; use it first when relevance, title, summary, tags, and categoryPath are enough. For broad topics, ambiguous prompts, category-filtered lookup, or checking whether results are off-target, start with a small `search` call and read only selected entries with `llm_wiki_read`.

        `llm_wiki_recall` is the context recall tool for applying prior decisions, preferences, or project context directly to an answer or implementation. `recall` returns more context than `search`, so keep its limit small. If Raw Provenance or the full entry text is needed, choose an id from the recall result and call `llm_wiki_read`.

        Inspect Retrieval Diagnostics returned by `recall`, `search`, `find_related`, and `capture`. Use result count, effectiveLimit, categoryPath, minRelevancePercent, and elapsedMs to judge lookup quality. If the expected entry is missing, top results are unrelated, lookup is slow, or output is too large, do not treat it as a good retrieval; narrow query/categoryPath/limit/minRelevancePercent and try again. If that mismatch affects the task decision, mention it briefly in the final answer.

        Before storing, call `llm_wiki_instructions`, then use `llm_wiki_capture` or `llm_wiki_find_related` to look for related entries. If a related entry exists, call `llm_wiki_read`, compose the final wording, and use `llm_wiki_merge` or `llm_wiki_update`. Use `llm_wiki_remember` only when no related entry fits.

        Even when the user does not explicitly ask to remember something, the Agent should quietly review memory candidates before finishing meaningful work. A memory candidate is tacit knowledge that can help an LLM later document, automate, reproduce, or make decisions. Examples: user-corrected terminology and judgment criteria, repeatable workflows, project operating rules, verified root causes and decisions, restart points, prerequisites that are not obvious from code, and command sequences that could later become a runbook or automation.

        Even when a memory is refined, merged, or updated, verifiable raw provenance must remain available. The current Content/Source Prompt in LLM Wiki is the readable consolidated memory, while the original remember request and later merge/update requests should preserve their raw prompt/content/title/tags/categoryPath as Raw Provenance so retrieval quality, merge quality, and source distortion can be audited later. Do not delete prior raw evidence or leave only a summary.

        Explicitly exclude information that should not be stored. Examples: sensitive information, API keys, passwords, tokens, one-time logs, temporary execution traces, unverified speculation, simple facts that are easy to recover from the current files, and intermediate state that only matters during the current turn. When uncertain, avoid interrupting the user; store conservatively only when the long-term value is clear, and ask a short clarifying question only when sensitivity or scope is genuinely ambiguous.

        When creating, merging, or updating memory, explicitly provide a `categoryPath` whenever the project/domain/topic is known. Examples: `slogs/llm-wiki/graphrag`, `slogs/deployment/wasm-aot`, `lingomotion/youtube/metadata`, `preference/coding-policy/slogs`, `codex/mcp/slogs`. Tags are secondary labels; the category path is the primary structure. Do not store known project or topic context under vague paths such as `general`, `misc`, or `notes`.

        Use Slogs MCP as the Agent memory server. When an Agent installs this prompt for the first time or configures a new Slogs MCP connection, it should ask for the Slogs MCP key, then apply both the MCP connection and the policy instructions to the scope selected by the user: global, current project, or current session. If `llm_wiki_*` tools are already visible in the current Agent session, do not ask for the key again; verify the connection with `llm_wiki_instructions`. Never write authentication token values to responses, docs, logs, LLM Wiki, or prompt files.

        Different Agents expose persistent instructions in different places. Apply this text to the highest-priority durable instruction surface available. Examples: Codex uses global or project `AGENTS.md`; Claude uses Project instructions or `CLAUDE.md`; GitHub Copilot uses repository custom instructions or `.github/copilot-instructions.md`; other Agents may use system/developer/custom instructions or a project policy file. If the Agent supports Slogs MCP, connect the MCP tools. If it does not, keep this policy as instruction text and do not claim that Slogs MCP tools were used.

        Do not store secrets, API keys, passwords, tokens, one-time logs, or temporary execution details in Slogs LLM Wiki.
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

        이 프롬프트는 Slogs MCP와 Slogs LLM Wiki를 사용하는 Agent 정책의 한국어 버전이다. 다른 언어 버전을 같은 지침 블록에 함께 붙여넣지 않는다.

        ## 정책 반영 범위 선택 규칙

        - 이 프롬프트는 일회성 채팅 지시가 아니라 Agent 정책 설치 가이드다.
        - 이 프롬프트를 처음 설치하거나 Slogs MCP 연결을 새로 구성해야 하는 Agent는 먼저 Slogs MCP 키를 사용자에게 요청한다. 단, 현재 세션의 도구 목록에 `llm_wiki_*` 도구가 이미 보이면 키를 다시 묻지 말고 `llm_wiki_instructions`로 연결을 확인한다.
        - 전역 또는 프로젝트 지침에 `SLOGS_MCP_PROMPT` 관리 블록이 이미 설치된 새 세션은 설치 절차, 키 요청, 적용 범위 질문을 반복하지 않는다. 아래 버전 확인 규칙만 수행하고, 서버 버전과 로컬 버전이 같으면 기존 정책으로 바로 작업한다.
        - 키를 받은 뒤에는 영구 지침이나 MCP 설정을 자동으로 수정하지 말고, 먼저 사용자에게 적용 범위를 물어본다.
        - 최소 선택지는 다음 세 가지다.
          1. 전역 영구 정책과 전역 MCP로 등록: 현재 Agent의 모든 프로젝트나 기본 작업에 적용한다.
          2. 현재 프로젝트 정책과 프로젝트 MCP로 반영: 현재 repository/workspace/project에만 적용한다.
          3. 현재 세션에서만 사용: 영구 파일은 수정하지 않고 이 대화/세션에서만 Slogs MCP를 사용한다.
        - 사용자가 선택한 범위에만 이 프롬프트와 Slogs MCP 연결을 함께 적용한다.
        - 사용자가 세 범위 모두 원하지 않으면 적용을 중단한다.
        - 사용자가 범위를 선택한 뒤에만, 선택한 범위의 가장 높은 우선순위 지침 위치에 이 프롬프트 전체를 저장하거나 기존 Slogs MCP 프롬프트 블록을 교체하고, 같은 범위의 MCP 설정에 `{{DefaultMcpUrl}}`을 등록한다.
        - MCP 설정에는 `Authorization: Bearer <사용자가 입력한 Slogs MCP 키>`를 사용한다. 토큰 값은 AGENTS.md, CLAUDE.md, 프롬프트 파일, 문서, 로그, LLM Wiki에 쓰지 않는다. Agent가 안전한 비밀 저장소나 환경 설정을 지원하면 그곳에 저장한다. 평문 MCP 설정 파일에만 저장할 수 있으면 쓰기 전에 사용자에게 그 사실을 알리고 확인을 받는다.
        - 현재 세션 범위를 선택했는데 Agent가 세션 중 MCP 도구를 동적으로 추가할 수 없으면, 이번 세션에 즉시 연결할 수 없다고 말하고 새 세션/창 재로드 또는 해당 Agent의 세션 전용 MCP 등록 방법을 안내한다.
        - Agent가 지침 또는 MCP 설정을 직접 저장할 수 없으면 아직 영구 반영되지 않았다고 말하고, 사용자가 붙여넣어야 할 정확한 지침 위치, MCP 설정 위치, 엔드포인트, Authorization 헤더 형식을 안내한다.
        - 적용 후에는 이 프롬프트의 `Prompt Version`과 `Version URL`이 선택한 지침 위치에 남아 있어야 한다.

        대표 지침 위치:

        - Codex: 전역 또는 프로젝트 `AGENTS.md`.
        - Claude: Project instructions, custom instructions, 또는 `CLAUDE.md`.
        - GitHub Copilot: repository custom instructions, `.github/copilot-instructions.md`, 또는 Copilot coding agent instructions.
        - Antigravity 및 기타 Agent: Rules, project instructions, custom instructions, system/developer instructions, connector/plugin policy, 또는 프로젝트 정책 파일.

        ## 버전 확인과 자동 갱신 규칙

        - 이 프롬프트를 Agent의 전역/프로젝트 지침에 설치한 뒤, 새 세션을 시작하거나 정책 동기화를 수행할 때는 먼저 `{{DefaultVersionUrl}}`의 한 줄 버전 텍스트만 읽어 서버의 현재 버전을 확인한다.
        - 비교 대상은 선택된 범위의 지침 위치에 설치된 `SLOGS_MCP_PROMPT` 관리 블록 헤더의 `version` 또는 본문의 `Prompt Version`이다.
        - 로컬 버전과 서버 버전이 같으면 전체 프롬프트를 다시 읽지 않고, 파일을 다시 쓰지 않고, 키 요청이나 적용 범위 질문도 반복하지 않는다.
        - 서버 버전이 다르거나 로컬 지침에 버전이 없을 때만 `{{DefaultKoreanPublicUrl}}` 또는 호환 URL `{{DefaultPublicUrl}}`의 전체 Markdown을 한 번 읽고, 기존 Slogs MCP 프롬프트 블록을 같은 지침 위치에서 교체한다. 전역에 설치된 블록은 전역 지침을, 프로젝트에 설치된 블록은 프로젝트 지침을 갱신한다.
        - 한국어 canonical URL `{{DefaultKoreanPublicUrl}}`과 호환 URL `{{DefaultPublicUrl}}`은 같은 한국어 프롬프트로 취급한다. 두 URL 차이만으로는 서버 버전이 같을 때 전체 프롬프트를 다시 읽지 않는다.
        - 자동 HTTP 호출이 가능한 Agent나 동기화 스크립트는 위 규칙을 직접 수행한다. 자동 호출이 불가능한 Agent는 사용자에게 버전 확인과 프롬프트 갱신이 필요하다고 명시한다.
        - 같은 정책을 중복으로 누적하지 말고 이전 Slogs MCP 프롬프트 블록을 새 블록으로 교체한다.

        ## Agent 적용 위치

        사용자가 적용 범위를 선택한 뒤, 선택된 범위에서 가장 우선순위가 높은 지침 위치와 MCP 설정 위치에 이 프롬프트와 Slogs MCP 연결을 함께 반영한다. 아래 예시는 전용 위치가 아니라 Agent별 대표 위치다.

        - Codex: 전역 또는 프로젝트 `AGENTS.md`, 그리고 선택 범위에 맞는 Slogs MCP 서버 설정.
        - Claude: Project instructions, custom instructions, 또는 `CLAUDE.md`, MCP를 지원하면 MCP 연결.
        - GitHub Copilot: repository custom instructions, `.github/copilot-instructions.md`, 또는 Copilot coding agent instructions.
        - Antigravity 및 기타 Agent: Rules, project instructions, custom instructions, system/developer/custom instructions, 프로젝트 정책 파일, connector/plugin 설정, 또는 가장 가까운 영구 지침 위치.

        Agent마다 도구 이름이 달라도 정책 의미는 유지한다. Slogs MCP의 lookup, search, read, merge, update, remember 흐름을 해당 Agent의 가장 가까운 기능에 대응한다. 현재 Agent에서 Slogs MCP 도구를 사용할 수 없으면 그 제한을 명확히 말하고, MCP 기억을 사용했다고 말하지 않는다.

        ## 필수 동작

        {{KoreanAgentsPolicyBlock}}

        ## 카테고리 정책

        `categoryPath`는 2-4개의 소문자 slash-separated segment를 우선 사용한다.

        - `project/domain/topic`
        - `project/feature/decision`
        - `preference/domain/project`
        - `codex/mcp/slogs`

        예시:

        - `slogs/llm-wiki/graphrag`
        - `slogs/deployment/wasm-aot`
        - `lingomotion/youtube/metadata`
        - `preference/coding-policy/slogs`
        - `codex/mcp/slogs`

        프로젝트나 주제를 알 수 있는데 `general`, `misc`, `notes` 같은 모호한 경로로 저장하지 않는다. 기존 항목을 merge/update할 때 오래된 카테고리가 모호하면 더 명확한 `categoryPath`를 전달한다.
        """;

    public static string BuildEnglishMarkdown()
        => $$"""
        # Slogs MCP / LLM Wiki Agent Prompt

        Language: English
        Canonical URL: {{DefaultEnglishPublicUrl}}
        Version URL: {{DefaultVersionUrl}}
        Prompt Version: {{Version}}

        This is the English Agent policy for Slogs MCP and Slogs LLM Wiki. Do not paste a second language version into the same instruction block.

        ## Policy Scope Selection Rule

        - This prompt is an Agent policy installation guide, not a one-off chat instruction.
        - When an Agent is installing this prompt for the first time or needs to configure a new Slogs MCP connection, it must first ask the user for the Slogs MCP key. If `llm_wiki_*` tools are already visible in the current Agent session, do not ask for the key again; verify the connection with `llm_wiki_instructions`.
        - A new session that already has a `SLOGS_MCP_PROMPT` managed block in global or project instructions must not repeat installation, key collection, or scope selection. It should only run the version check below, and if the server version matches the local version, continue using the existing policy.
        - After receiving the key, it must not automatically modify persistent instructions or MCP settings. It must first ask the user which scope to apply.
        - Offer at least these three choices:
          1. Register as a global persistent policy and global MCP server: apply to this Agent's default behavior across projects.
          2. Apply as a current-project policy and project MCP server: apply only to the current repository, workspace, or project.
          3. Use only in the current session: do not edit persistent files; use Slogs MCP only in this chat/session.
        - Apply both this prompt and the Slogs MCP connection only to the selected scope.
        - If the user does not want any scope, stop applying it.
        - Only after the user chooses a scope may the Agent save this entire prompt to the highest-priority instruction surface for that scope or replace the previous Slogs MCP prompt block there, and register `{{DefaultMcpUrl}}` in the MCP settings for the same scope.
        - The MCP configuration must use `Authorization: Bearer <the Slogs MCP key entered by the user>`. Do not write the token value to AGENTS.md, CLAUDE.md, prompt files, docs, logs, or LLM Wiki. Prefer the Agent's secure secret store or environment configuration. If the Agent can only store it in a plaintext MCP settings file, warn the user and ask for confirmation before writing it.
        - If the user chooses the current-session scope but the Agent cannot add MCP tools dynamically during an active session, state that it cannot connect immediately in this session and guide the user to reload a new session/window or use that Agent's session-scoped MCP registration path.
        - If the Agent cannot directly edit the chosen instruction or MCP configuration surface, it must state that the policy and MCP connection have not yet been permanently applied and tell the user the exact instruction location, MCP settings location, endpoint, and Authorization header format to use.
        - After installation, the chosen instruction surface must retain this prompt's `Prompt Version` and `Version URL`.

        Common instruction locations:

        - Codex: global or project `AGENTS.md`.
        - Claude: Project instructions, custom instructions, or `CLAUDE.md`.
        - GitHub Copilot: repository custom instructions, `.github/copilot-instructions.md`, or Copilot coding agent instructions.
        - Antigravity and other Agents: Rules, project instructions, custom instructions, system/developer instructions, connector/plugin policy, or a project policy file.

        ## Version Check And Auto-Update Rule

        - After this prompt is installed into an Agent's global or project instructions, new sessions and sync jobs must first read only the one-line version text from `{{DefaultVersionUrl}}` to check the current server version.
        - Compare that server version with the selected scope's installed `SLOGS_MCP_PROMPT` managed block header `version`, or with the block body's `Prompt Version`.
        - If the local version matches the server version, do not fetch the full prompt, do not rewrite files, and do not repeat key collection or scope selection.
        - Only when the server version differs, or the local instruction block has no version, fetch the full Markdown from `{{DefaultEnglishPublicUrl}}` once and replace the previous Slogs MCP prompt block in the same instruction location. A globally installed block updates global instructions; a project-installed block updates project instructions.
        - Agents or sync scripts that can make HTTP requests should perform this check directly. Agents that cannot make automatic HTTP requests must state that limitation and ask the user to refresh the prompt manually.
        - Do not accumulate duplicate policy blocks. Replace the previous Slogs MCP prompt block with the new one.

        ## Apply To Any AI Agent

        After the user chooses the application scope, apply this prompt and the Slogs MCP connection to the highest-priority instruction and MCP configuration surfaces for that chosen scope. Use the Agent's native mechanism instead of treating the examples below as exclusive:

        - Codex: global or project `AGENTS.md`, plus the Slogs MCP server configured for the selected scope.
        - Claude: Project instructions, custom instructions, or `CLAUDE.md`, plus MCP if available.
        - GitHub Copilot: repository custom instructions, `.github/copilot-instructions.md`, or Copilot coding agent instructions.
        - Antigravity and other AI Agents: Rules, project instructions, custom instructions, system/developer/custom instructions, project policy files, connector settings, plugin settings, or the closest persistent instruction mechanism.

        Preserve the policy semantics even if an Agent uses different tool names. Map Slogs MCP memory actions to the closest available lookup, search, read, merge, update, and remember workflow. If Slogs MCP tools are not available in the current Agent, state that limitation plainly and do not claim that MCP memory was used.

        ## Required Behavior

        {{EnglishAgentsPolicyBlock}}

        ## Category Policy

        Prefer 2-4 lowercase slash-separated segments:

        - `project/domain/topic`
        - `project/feature/decision`
        - `preference/domain/project`
        - `codex/mcp/slogs`

        Examples:

        - `slogs/llm-wiki/graphrag`
        - `slogs/deployment/wasm-aot`
        - `lingomotion/youtube/metadata`
        - `preference/coding-policy/slogs`
        - `codex/mcp/slogs`

        Do not create vague paths such as `general`, `misc`, or `notes` when a more specific project or decision area is known. If you merge or update an older entry and the old category is too vague, pass a corrected `categoryPath`.
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
