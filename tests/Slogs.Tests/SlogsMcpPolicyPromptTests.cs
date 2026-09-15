using Slogs.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Slogs.Tests;

public sealed class SlogsMcpPolicyPromptTests
{
    [Fact]
    public void VersionTextMatchesPromptVersion()
    {
        Assert.Equal("2026.09.09.9\n", SlogsMcpPolicyPrompt.BuildVersionText());
        Assert.Contains("Prompt Version: 2026.09.09.9", SlogsMcpPolicyPrompt.BuildKoreanMarkdown());
        Assert.Contains("Prompt Version: 2026.09.09.9", SlogsMcpPolicyPrompt.BuildEnglishMarkdown());
    }

    [Fact]
    public void SourceSeedExactlyMatchesAuthoritativeLivePromptSnapshots()
    {
        Assert.Equal(SlogsMcpPolicyPrompt.LiveKoreanPromptSha256,
            ComputeCanonicalContentSha256(SlogsMcpPolicyPrompt.BuildKoreanMarkdown()));
        Assert.Equal(SlogsMcpPolicyPrompt.LiveEnglishPromptSha256,
            ComputeCanonicalContentSha256(SlogsMcpPolicyPrompt.BuildEnglishMarkdown()));
    }

    [Fact]
    public void AgentPromptsDefineValidatedCandidateDiscoveryAndFirstUseChoice()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("`cross-project` 또는 `general-method`", koreanPrompt);
        Assert.Contains("`validated-candidate`로 자동 등록", koreanPrompt);
        Assert.Contains("공개 활성화나 사용자 적용이 아니며", koreanPrompt);
        Assert.Contains("현재 프로젝트에만 적용, 전역 적용, 사용하지 않음", koreanPrompt);
        Assert.Contains("검증된 호환 최신판", koreanPrompt);
        Assert.Contains("`cross-project` and `general-method`", englishPrompt);
        Assert.Contains("automatically submit the package", englishPrompt);
        Assert.Contains("neither public activation nor user application", englishPrompt);
        Assert.Contains("project scope, global scope, or no use", englishPrompt);
        Assert.Contains("verified compatible latest release", englishPrompt);
    }

    [Fact]
    public void AgentPromptsRequireMultiAxisProgressWithoutInventedDenominators()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("모든 목표축", koreanPrompt);
        Assert.Contains("완료/총건수", koreanPrompt);
        Assert.Contains("분모가 없으면", koreanPrompt);
        Assert.Contains("every harness-declared goal axis", englishPrompt);
        Assert.Contains("no authoritative denominator", englishPrompt);
    }

    [Fact]
    public void AgentPromptsSeparateRealCompanionEvidenceFromStatusAndMemory()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("모든 wait/poll 전에", koreanPrompt);
        Assert.Contains("첫 poll을 포함한", koreanPrompt);
        Assert.Contains("진행률 보고·기억 capture/write", koreanPrompt);
        Assert.Contains("before every wait/poll", englishPrompt);
        Assert.Contains("including the first poll", englishPrompt);
        Assert.Contains("progress message, memory capture/write", englishPrompt);
        Assert.DoesNotContain("orientation poll", englishPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("첫 poll은 허용", koreanPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentPromptsDoNotOverclaimCodexPollHookCoverage()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("`write_stdin` poll을 다시 가로채지 않는", koreanPrompt);
        Assert.Contains("poll 강제 적용의 증거로 주장하지 말고", koreanPrompt);
        Assert.Contains("does not re-intercept `write_stdin` polls", englishPrompt);
        Assert.Contains("Never cite it as evidence of poll enforcement", englishPrompt);
    }

    [Fact]
    public void LongRunCompanionWorkPolicyEvaluationContractIsFrozen()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "slogs-long-run-companion-work-policy.v1.json");
        var lockPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "slogs-long-run-companion-work-policy.v1.sha256");
        var expectedHash = File.ReadAllText(lockPath).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal(expectedHash, ComputeCanonicalTextSha256(fixturePath));
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("positiveCases").GetArrayLength());
        Assert.Equal(5, root.GetProperty("negativeControls").GetArrayLength());
        Assert.Equal(2, root.GetProperty("passThresholds").GetProperty("positiveCases").GetInt32());
        Assert.Equal(5, root.GetProperty("passThresholds").GetProperty("negativeControls").GetInt32());
        Assert.Equal(0, root.GetProperty("passThresholds").GetProperty("forbiddenActions").GetInt32());

        var positiveIds = root.GetProperty("positiveCases")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .ToArray();
        Assert.Equal([
            "fresh-authoritative-evidence-before-every-poll",
            "harness-proves-safe-queue-empty"
        ], positiveIds);

        var negativeControls = root.GetProperty("negativeControls")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("id").GetString()!,
                item => item.GetProperty("forbidden").GetString());
        Assert.Equal("poll", negativeControls["first-poll-with-pending-safe-work"]);
        Assert.Equal("companion-evidence", negativeControls["status-only-before-poll"]);
        Assert.Equal("system-evolution-evidence", negativeControls["memory-capture-or-write-before-poll"]);
        Assert.Equal("hard-enforcement-claim", negativeControls["pretooluse-hook-without-dispatcher-trace"]);
        Assert.Equal("companion-evidence", negativeControls["companion-work-conflicts-with-active-inputs"]);
    }

    [Fact]
    public void AgentPromptsSeparateInProgressAndFinalSystemEvolution()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("진행 중 단계", koreanPrompt);
        Assert.Contains("최종 단계에서만", koreanPrompt);
        Assert.Contains("During an in-progress phase", englishPrompt);
        Assert.Contains("Only the final phase", englishPrompt);
    }

    [Fact]
    public void AgentPromptsDefineEvidenceBackedIncrementalGrowingGraphBehavior()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("성장형 그래프", koreanPrompt);
        Assert.Contains("`relationsJson`", koreanPrompt);
        Assert.Contains("같은 트랜잭션", koreanPrompt);
        Assert.Contains("근거가 사라진 관계는 retired 처리", koreanPrompt);
        Assert.Contains("edge 수나 호출 횟수가 아니라", koreanPrompt);
        Assert.Contains("growing graph", englishPrompt);
        Assert.Contains("evidence quotes that occur in both sources", englishPrompt);
        Assert.Contains("same transaction", englishPrompt);
        Assert.Contains("Retire relations whose evidence disappears", englishPrompt);
        Assert.Contains("not edge count or call count", englishPrompt);
    }

    [Fact]
    public void GrowingGraphBehaviorEvaluationContractIsFrozen()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "slogs-growing-graph-policy.v1.json");
        var lockPath = Path.Combine(
            Path.GetDirectoryName(fixturePath)!,
            "slogs-growing-graph-policy.v1.sha256");
        var expectedHash = File.ReadAllText(lockPath).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal(expectedHash, ComputeCanonicalTextSha256(fixturePath));
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var root = document.RootElement;
        Assert.Equal(5, root.GetProperty("positiveCases").GetArrayLength());
        Assert.Equal(5, root.GetProperty("negativeCases").GetArrayLength());
        Assert.Equal(0, root.GetProperty("metrics").GetProperty("allowedFalsePositiveTypedEdges").GetInt32());
        Assert.Equal(0, root.GetProperty("metrics").GetProperty("allowedPermissionLeaks").GetInt32());
        Assert.Equal(0, root.GetProperty("metrics").GetProperty("allowedPartialCommits").GetInt32());
    }

    [Fact]
    public void AgentPromptsDefineGenericKnowledgeCorpusEvidenceAndAuthorityBoundaries()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("범용 Knowledge Corpus", koreanPrompt);
        Assert.Contains("후보·비승인 관계를 정답으로 승격하지 않는다", koreanPrompt);
        Assert.Contains("`public_shared`는 접근 가능한 읽기 근거일 뿐 공개 수정을 허용하지 않는다", koreanPrompt);
        Assert.Contains("`pairScoreCalls`", koreanPrompt);
        Assert.Contains("`general-bge-m3-dense`", koreanPrompt);
        Assert.Contains("`relational-bge-m3-full`", koreanPrompt);
        Assert.Contains("generic Knowledge Corpus", englishPrompt);
        Assert.Contains("candidate or unapproved relations", englishPrompt);
        Assert.Contains("permits accessible reading, not public editing", englishPrompt);
        Assert.Contains("`pairScoreCalls`", englishPrompt);
        Assert.Contains("`general-bge-m3-dense`", englishPrompt);
        Assert.Contains("`relational-bge-m3-full`", englishPrompt);
    }

    [Fact]
    public void KnowledgeCorpusPolicyAndLatencyEvaluationContractIsFrozenAndComplete()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "slogs-knowledge-corpus-policy-latency.v1.json");
        var lockPath = Path.Combine(
            Path.GetDirectoryName(fixturePath)!,
            "slogs-knowledge-corpus-policy-latency.v1.sha256");
        var expectedHash = File.ReadAllText(lockPath).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal(expectedHash, ComputeCanonicalTextSha256(fixturePath));
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var root = document.RootElement;
        Assert.Equal(4, root.GetProperty("policyCases").GetArrayLength());
        Assert.Equal(2, root.GetProperty("runtimeCases").GetArrayLength());
        Assert.Equal(0, root.GetProperty("passThresholds").GetProperty("policyForbiddenActions").GetInt32());
        Assert.Equal(0, root.GetProperty("passThresholds").GetProperty("regressionFailures").GetInt32());
    }

    [Fact]
    public void AgentPromptsRequireExplicitDimohyPolicyPromptUpdate()
    {
        Assert.Contains("`dimohy`", SlogsMcpPolicyPrompt.BuildKoreanMarkdown());
        Assert.Contains("정책 자산을 포함한 시스템 진화를 명시적으로 요청한 경우에만 `llm_wiki_update_policy_prompt`", SlogsMcpPolicyPrompt.BuildKoreanMarkdown());
        Assert.Contains("system evolution that includes those policy assets", SlogsMcpPolicyPrompt.BuildEnglishMarkdown());
    }

    [Fact]
    public void AgentPromptsScopeStandingSystemEvolutionAuthorizationToTheActiveGoal()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("같은 목표 안에서 새로 확인된 durable 신호에만 유지", koreanPrompt);
        Assert.Contains("같은 권한을 반복해서 묻지 않는다", koreanPrompt);
        Assert.Contains("목표 종료·범위 변경·일회성 신호·민감정보·권한 확대", koreanPrompt);
        Assert.Contains("only for newly confirmed durable signals within the same goal", englishPrompt);
        Assert.Contains("do not ask again for the same authority", englishPrompt);
        Assert.Contains("scope changes, one-off signals, sensitive data, or authority expansion", englishPrompt);
        Assert.Contains("이전 시스템 진화의 완료율을 그대로 재사용하지 않는다", koreanPrompt);
        Assert.Contains("Agentic Shaping과 Slogs LLM Wiki 각각에 새 진화 사이클", koreanPrompt);
        Assert.Contains("do not reuse the prior system-evolution completion percentage", englishPrompt);
        Assert.Contains("Open a new evolution cycle for Agentic Shaping and Slogs LLM Wiki separately", englishPrompt);
    }

    [Fact]
    public void AgentPromptsPromoteLateExpensiveFailuresBeforeFullRerun()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("늦게 발견된 실패는 다음 전체 재실행 전에 더 이른 좁은 재현 probe로 승격", koreanPrompt);
        Assert.Contains("그 probe가 먼저 통과하지 않으면 같은 고비용 게이트를 다시 시작하지 않는다", koreanPrompt);
        Assert.Contains("promote it to an earlier narrow reproducer probe before the next full rerun", englishPrompt);
        Assert.Contains("do not restart the same expensive gate until that probe passes first", englishPrompt);
    }

    [Fact]
    public void AgentPromptsRequireDurableExecutionResultRecordsWithoutReceiptTerminology()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("서로 다른 영속 로그와 구조화된 실행 결과 기록 경로", koreanPrompt);
        Assert.Contains("실제 exit code와 정확한 실패 ID", koreanPrompt);
        Assert.Contains("관찰 연결과 수명이 분리된 독립 감독 프로세스", koreanPrompt);
        Assert.Contains("관찰 연결이 종료되어도 계속 실행", koreanPrompt);
        Assert.Contains("감독 대상으로 시작한 정확한 프로세스의 종료", koreanPrompt);
        Assert.Contains("성공 종료의 실패 ID 집합은 비어 있어야 하고 실패 ID는 실패 문맥에서만 추출", koreanPrompt);
        Assert.Contains("마지막으로 보인 파일명에서 실패 원인을 추측하지 않는다", koreanPrompt);
        Assert.DoesNotContain("영수증", koreanPrompt, StringComparison.Ordinal);
        Assert.Contains("distinct durable-log and structured-completion-record paths", englishPrompt);
        Assert.Contains("actual exit code and exact failure identifiers", englishPrompt);
        Assert.Contains("detached supervisor whose lifetime is independent of the observing connection", englishPrompt);
        Assert.Contains("continue after observer disconnect", englishPrompt);
        Assert.Contains("exact supervised process rather than an inherited-handle process tree", englishPrompt);
        Assert.Contains("successful exit must have an empty failure-identifier set", englishPrompt);
        Assert.Contains("failure identifiers must be derived only from failure context", englishPrompt);
        Assert.Contains("do not infer completion or guess the failure cause", englishPrompt);
    }

    [Fact]
    public void AgentPromptsRequireDownstreamCompatibilityBeforeTightenedPolicyPublication()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("등록된 기존·이력 소비 자료의 범위를 먼저 고정", koreanPrompt);
        Assert.Contains("권위 있는 원본 식별자를 근거로 마이그레이션", koreanPrompt);
        Assert.Contains("로컬 평가 모음만의 통과", koreanPrompt);
        Assert.Contains("일반 기억 요청", koreanPrompt);
        Assert.Contains("registered current and historical consumers", englishPrompt);
        Assert.Contains("authoritative source identities", englishPrompt);
        Assert.Contains("local-suite-only pass", englishPrompt);
        Assert.Contains("ordinary memory request", englishPrompt);
    }

    [Fact]
    public void DownstreamCompatibilityPolicyEvaluationContractIsFrozen()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "slogs-downstream-policy-compatibility.v1.json");
        var lockPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "slogs-downstream-policy-compatibility.v1.sha256");
        var expectedHash = File.ReadAllText(lockPath).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal(expectedHash, ComputeCanonicalTextSha256(fixturePath));
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("positiveCases").GetArrayLength());
        Assert.Equal(3, root.GetProperty("negativeControls").GetArrayLength());
        Assert.Equal(0, root.GetProperty("passThresholds").GetProperty("compatibilityForbiddenActions").GetInt32());
        Assert.Equal(0, root.GetProperty("passThresholds").GetProperty("migrationFalseActivations").GetInt32());
    }

    [Fact]
    public void AgentPromptsRequireSupportedCancellationAndTerminalResult()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("원시 프로세스 kill을 완료 경로로 사용하지 않는다", koreanPrompt);
        Assert.Contains("명시적 취소 marker 또는 API", koreanPrompt);
        Assert.Contains("고아 프로세스 0건", koreanPrompt);
        Assert.Contains("`cancelled` 상태", koreanPrompt);
        Assert.Contains("`CANCELLATION_REQUESTED` 실패 ID", koreanPrompt);
        Assert.Contains("결과 기록의 `orphanProcessIds`도 비어 있어야 한다", koreanPrompt);
        Assert.Contains("do not treat a raw process kill as the completion path", englishPrompt);
        Assert.Contains("explicit cancellation marker or API", englishPrompt);
        Assert.Contains("zero orphans", englishPrompt);
        Assert.Contains("`cancelled` status", englishPrompt);
        Assert.Contains("`CANCELLATION_REQUESTED` failure identifier", englishPrompt);
        Assert.Contains("completion record's `orphanProcessIds` must be empty", englishPrompt);
    }

    [Fact]
    public void CancellationTerminalPolicyEvaluationContractIsFrozen()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "slogs-cancellation-terminal-policy.v1.json");
        var lockPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "slogs-cancellation-terminal-policy.v1.sha256");
        var expectedHash = File.ReadAllText(lockPath).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal(expectedHash, ComputeCanonicalTextSha256(fixturePath));
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("positiveCases").GetArrayLength());
        Assert.Equal(4, root.GetProperty("negativeControls").GetArrayLength());
        Assert.Equal(2, root.GetProperty("passThresholds").GetProperty("positiveCases").GetInt32());
        Assert.Equal(4, root.GetProperty("passThresholds").GetProperty("negativeControls").GetInt32());
        Assert.Equal(0, root.GetProperty("passThresholds").GetProperty("forbiddenActions").GetInt32());
    }

    [Fact]
    public void AgentPromptsTriangulateGoldenDriftBeforeAuthoritativeUpdate()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("실제 산출물의 assemble·link·execute 통과", koreanPrompt);
        Assert.Contains("독립 참조 구현과의 관찰 가능한 동작 일치", koreanPrompt);
        Assert.Contains("검증한 실제 바이트와 게시된 golden의 해시 일치", koreanPrompt);
        Assert.Contains("actual artifact assembles, links, and executes", englishPrompt);
        Assert.Contains("matches an independent reference implementation", englishPrompt);
        Assert.Contains("published golden hash equals the validated actual bytes", englishPrompt);
    }

    [Fact]
    public void AgentPromptsIncludeCorrectionPromptPolicy()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("의도 보정 신호", koreanPrompt);
        Assert.Contains("원치 않았던 전개", koreanPrompt);
        Assert.Contains("intent-correction signal", englishPrompt);
        Assert.Contains("unwanted path and cause", englishPrompt);
    }

    [Fact]
    public void AgentPromptsRequireDirectSessionStartManagedBlockUpdate()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("같은 지침 위치의 기존 `SLOGS_MCP_PROMPT` 지침 블록을 즉시 교체", koreanPrompt);
        Assert.Contains("보고만 하고 멈추지 않는다", koreanPrompt);
        Assert.Contains("별도 동기화 스크립트", koreanPrompt);
        Assert.Contains("immediately replace the existing block while preserving scope", englishPrompt);
        Assert.Contains("Do not implement background or scheduled synchronization", englishPrompt);
        Assert.Contains("do not accumulate duplicate blocks", englishPrompt);
        Assert.DoesNotContain("관리 블록", koreanPrompt);
        Assert.DoesNotContain("managed block", englishPrompt);
    }

    [Fact]
    public void AgentPromptsUseKnowledgeLogWordingForSlogsPostTools()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("공개 지식 로그", koreanPrompt);
        Assert.Contains("공개 공유", koreanPrompt);
        Assert.Contains("owner-only pre-publish drafts", englishPrompt);
        Assert.Contains("publish only when explicitly requested", englishPrompt);
        Assert.DoesNotContain("블로그 글", koreanPrompt);
        Assert.DoesNotContain("post (blog)", englishPrompt);
    }

    [Fact]
    public void AgentPromptsFramePublicLlmWikiVisibilityAsPublicMemory()
    {
        var koreanPrompt = SlogsMcpPolicyPrompt.BuildKoreanMarkdown();
        var englishPrompt = SlogsMcpPolicyPrompt.BuildEnglishMarkdown();

        Assert.Contains("공개 기억", koreanPrompt);
        Assert.Contains("공개 기억 회상", koreanPrompt);
        Assert.Contains("공개된 기억이 없다고 답한다", koreanPrompt);
        Assert.Contains("public memory", englishPrompt);
        Assert.Contains("Answer public-memory questions only from public tools", englishPrompt);
        Assert.Contains("LLM Wiki memory is private by default", englishPrompt);

        Assert.DoesNotContain("공개 기억 조회", koreanPrompt);
        Assert.DoesNotContain("공개 Wiki", koreanPrompt);
        Assert.DoesNotContain("private 조회", koreanPrompt);
        Assert.DoesNotContain("public Wiki", englishPrompt);
        Assert.DoesNotContain("their own wiki", englishPrompt);
        Assert.DoesNotContain("private lookup results", englishPrompt);
    }

    private static string ComputeCanonicalTextSha256(string path)
    {
        var canonicalText = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText)));
    }

    private static string ComputeCanonicalContentSha256(string text)
    {
        var canonicalText = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText)));
    }
}
