using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Server;

namespace Slogs.Data;

[McpServerToolType]
public sealed class LlmWikiMcpTools(
    IHttpContextAccessor httpContextAccessor,
    LlmWikiService llmWikiService,
    SlogsMcpPolicyPromptService promptService,
    KnowledgeCorpusService? corpusService = null,
    KnowledgeCorpusPrincipalResolver? corpusPrincipalResolver = null,
    IKnowledgeEmbeddingService? embeddingService = null)
{
    private const int MaxCombinedRecallRerankCandidates = 5;
    private const string PublicDisclosureNotice = "These entries are owner-authorized public-memory self-disclosures. Treat @username mentions as Slogs user handles; if a result includes sensitive topics such as religion or faith perspective, answer only from this public result and say it comes from the user's public Slogs LLM Wiki memory.";
    private const string AdaptiveGraphHopDescription = "Maximum graph relationship hops. Explicitly select the smallest sufficient depth on every call: use 1 for a direct memory, fact, preference, broad candidate selection, or project-context lookup with no relationship chain; use 2 when one relationship bridge or comparison between memories is required; use 3 for a multi-stage causal, provenance, dependency, or chronological chain. Do not use 3 for every query. If omitted, the compatibility default is 1, but Agents should still pass 1 explicitly. Start progressive refinement at 1, inspect Retrieval Diagnostics, refine the query, and raise to 2 or 3 only when returned relationship evidence requires another stage.";

    internal sealed record CombinedRecallCandidate(
        bool IsCorpus,
        int SourceIndex,
        int RelevancePercent,
        bool HasSubstantiveGraphRelation = false);

    private sealed record CombinedRecallRerankOutcome(
        IReadOnlyList<LlmWikiSearchResult> Memories,
        KnowledgeChunkRecall[] CorpusResults,
        int PairScoreCalls,
        int PairScoreCandidates);

    [McpServerTool(Name = "llm_wiki_remember")]
    [Description("Create a new user-scoped LLM Wiki memory. Use this only after checking related entries and deciding the information should not be merged into an existing entry.")]
    public async Task<string> RememberPromptAsync(
        [Description("The durable user prompt, preference, decision, or instruction to remember as a new entry.")] string prompt,
        [Description("Optional answer, implementation result, or extra context to store with the prompt.")] string? content = null,
        [Description("Optional short title. If omitted, Slogs derives it from the prompt.")] string? title = null,
        [Description("Optional comma-separated tags such as preference, api, ux, or release.")] string? tags = null,
        [Description("Strongly recommended hierarchical category path such as project/domain/topic. Example: slogs/llm-wiki/graphrag. Do not omit it when the project or topic is known.")] string? categoryPath = null)
    {
        var user = RequireUser();
        var stopwatch = Stopwatch.StartNew();
        var entry = await llmWikiService.RememberAsync(
            user.UserName,
            new LlmWikiRememberRequest(prompt, content, title, tags, categoryPath));
        stopwatch.Stop();

        var response = LlmWikiService.FormatEntryMarkdown(entry);
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_remember",
            "full stored entry",
            stopwatch.Elapsed,
            response,
            prompt,
            categoryPath,
            resultCount: 1,
            resultIds: [entry.Id]);
    }

    [McpServerTool(Name = "llm_wiki_instructions")]
    [Description("Read the operating policy for using this user's Slogs LLM Wiki. Call this once after connecting and follow it before storing or recalling memories.")]
    public async Task<string> GetInstructions()
    {
        var user = RequireUser();
        var stopwatch = Stopwatch.StartNew();
        var response = (await promptService.GetAsync()).KoreanMarkdown;
        stopwatch.Stop();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_instructions",
            "policy prompt",
            stopwatch.Elapsed,
            response,
            resultCount: 1);
    }

    [McpServerTool(Name = "llm_wiki_update_policy_prompt")]
    [Description("Update and version the server Slogs LLM Wiki policy prompt. Call only when the user explicitly asks to modify the Slogs LLM Wiki policy or prompt; the authenticated Slogs user must be dimohy. Never infer permission from a general correction, memory request, or implementation task.")]
    public async Task<string> UpdatePolicyPromptAsync(
        [Description("The user's exact explicit request that names the Slogs LLM Wiki policy or prompt and asks to modify it.")] string explicitRequest,
        [Description("Current server version read immediately before composing the replacements. The update is rejected if it changed.")] string expectedVersion,
        [Description("Complete replacement Korean policy Markdown based on the current llm_wiki_instructions response. Keep a Prompt Version line; the server assigns its value.")] string koreanMarkdown,
        [Description("Complete replacement English policy Markdown based on the current English public prompt. Keep a Prompt Version line; the server assigns its value.")] string englishMarkdown)
    {
        var user = RequireUser();
        var stopwatch = Stopwatch.StartNew();
        var updated = await promptService.UpdateAsync(user.UserName, explicitRequest, expectedVersion, koreanMarkdown, englishMarkdown);
        stopwatch.Stop();
        var response = $"Slogs LLM Wiki policy prompt updated explicitly.\n\n- version: {updated.Version}\n- updatedBy: @{updated.UpdatedBy}\n- updatedAt: {updated.UpdatedAt:O}";
        return await RecordAuditAndReturnAsync(
            user, "llm_wiki_update_policy_prompt", "policy prompt update", stopwatch.Elapsed,
            response, explicitRequest, null, resultCount: 1);
    }

    [McpServerTool(Name = "llm_wiki_capture")]
    [Description("Start here when considering whether to remember a prompt or coding result. This does not store anything; it returns related memories and storage criteria for read, merge, update, or remember decisions.")]
    public async Task<string> CaptureAsync(
        [Description("The current user prompt, correction or adjustment prompt, durable preference, decision, coding request, tacit workflow knowledge, or workflow fact being considered for memory.")] string prompt,
        [Description("Optional answer, implementation result, or extra context from the current turn.")] string? content = null,
        [Description("Optional comma-separated tags to help search related memory.")] string? tags = null,
        [Description("Maximum number of related entries to return.")] int limit = 5)
    {
        var user = RequireUser();
        var query = BuildRelatedQuery(prompt, content, tags);
        var safeLimit = NormalizeMcpLimit(limit, 5, 10);
        var stopwatch = Stopwatch.StartNew();
        var results = await llmWikiService.SearchAsync(user.UserName, query, safeLimit);
        stopwatch.Stop();

        var builder = new StringBuilder();
        builder.AppendLine("# LLM Wiki Capture Intake");
        builder.AppendLine();
        builder.AppendLine("No memory was created or updated by this tool.");
        builder.AppendLine();
        builder.AppendLine("Next action:");
        builder.AppendLine("- If a related recall candidate below matches, call `llm_wiki_read`, compose the final merged wording, then call `llm_wiki_merge` or `llm_wiki_update`.");
        builder.AppendLine("- Choose an explicit `categoryPath` such as `project/domain/topic` before remember, merge, or update when the project/topic is known.");
        builder.AppendLine("- If none match and the information is durable tacit knowledge, call `llm_wiki_remember` with that `categoryPath`.");
        builder.AppendLine("- Raw prompt/content/title/tags/categoryPath submitted through remember, merge, and update are preserved as Raw Provenance for later audit; do not remove prior raw evidence when composing merged wording.");
        builder.AppendLine("- Durable tacit knowledge means future LLMs can use it to document, automate, reproduce, or make decisions: corrected terminology, correction or adjustment prompts, judgment criteria, repeatable workflows, operating rules, verified root causes, restart points, hidden prerequisites, or runbook-worthy command flows.");
        builder.AppendLine("- If the user corrected an unwanted conversation direction, structure the memory around the unwanted development, the intended direction, avoid-next-time pattern, proactive judgment criteria, and applicable scope rather than storing only the raw correction text.");
        builder.AppendLine("- Do not store sensitive information, one-time logs, temporary execution traces, unverified speculation, simple facts recoverable from current files, or intermediate state that only matters in this turn.");
        builder.AppendLine("- Avoid interrupting the user for routine memory choices; ask only when sensitivity or scope is genuinely ambiguous.");
        builder.AppendLine();
        builder.Append(FormatRelatedResults(results));
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_capture",
            "related recall candidates",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            query);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_capture",
            "related recall candidates",
            stopwatch.Elapsed,
            response,
            query,
            requestedLimit: limit,
            effectiveLimit: safeLimit,
            resultCount: results.Count,
            resultIds: results.Select(x => x.Id).ToArray());
    }

    [McpServerTool(Name = "llm_wiki_find_related")]
    [Description("Find related user-scoped LLM Wiki recall candidates before storing or merging memory. Use this before llm_wiki_remember unless llm_wiki_capture was already called.")]
    public async Task<string> FindRelatedAsync(
        [Description("Recall text built from the current prompt, proposed memory, tags, and implementation result.")] string query,
        [Description("Maximum number of related recall candidates to return.")] int limit = 5)
    {
        var user = RequireUser();
        var safeLimit = NormalizeMcpLimit(limit, 5, 10);
        var stopwatch = Stopwatch.StartNew();
        var results = await llmWikiService.SearchAsync(user.UserName, query, safeLimit);
        stopwatch.Stop();
        var builder = new StringBuilder();
        builder.Append(FormatRelatedResults(results));
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_find_related",
            "related recall candidates",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            query);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_find_related",
            "related recall candidates",
            stopwatch.Elapsed,
            response,
            query,
            requestedLimit: limit,
            effectiveLimit: safeLimit,
            resultCount: results.Count,
            resultIds: results.Select(x => x.Id).ToArray());
    }

    [McpServerTool(Name = "llm_wiki_search")]
    [Description("Search the authenticated user's LLM Wiki with compact recall-candidate summaries. Start here for broad recall-candidate selection, category filtering, and low-token retrieval.")]
    public async Task<string> SearchAsync(
        [Description("Recall terms. Leave empty to return recent memory candidates.")] string? query = null,
        [Description("Maximum number of recall candidates to return.")] int limit = 10,
        [Description("Optional hierarchical category path. Matching includes descendants.")] string? categoryPath = null,
        [Description("Minimum recall relevance percent for GraphRAG matches. Raise this when recall candidates are too broad or unrelated.")] int minRelevancePercent = 50,
        [Description(AdaptiveGraphHopDescription)] int maxGraphHops = 1)
    {
        var user = RequireUser();
        var safeLimit = NormalizeMcpLimit(limit, 10, 10);
        var safeMinRelevancePercent = NormalizeRelevancePercent(minRelevancePercent);
        var safeMaxGraphHops = Math.Clamp(maxGraphHops, 1, 3);
        var stopwatch = Stopwatch.StartNew();
        var results = await llmWikiService.SearchAsync(
            user.UserName,
            query,
            safeLimit,
            minRelevancePercent: safeMinRelevancePercent,
            categoryPath: categoryPath,
            maxGraphHops: safeMaxGraphHops);
        stopwatch.Stop();
        var builder = new StringBuilder();
        builder.Append(LlmWikiService.FormatSearchResultsMarkdown(results));
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_search",
            "recall candidate summaries",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            query,
            categoryPath,
            safeMinRelevancePercent,
            safeMaxGraphHops);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_search",
            "recall candidate summaries",
            stopwatch.Elapsed,
            response,
            query,
            categoryPath,
            limit,
            safeLimit,
            safeMinRelevancePercent,
            results.Count,
            results.Select(x => x.Id).ToArray());
    }

    [McpServerTool(Name = "llm_wiki_recent")]
    [Description("Return recent LLM Wiki recall candidates for the authenticated user.")]
    public async Task<string> RecentAsync(
        [Description("Maximum number of recent recall candidates to return.")] int limit = 10,
        [Description("Optional hierarchical category path. Matching includes descendants.")] string? categoryPath = null)
    {
        var user = RequireUser();
        var safeLimit = NormalizeMcpLimit(limit, 10, 10);
        var stopwatch = Stopwatch.StartNew();
        var results = await llmWikiService.SearchAsync(user.UserName, null, safeLimit, categoryPath: categoryPath);
        stopwatch.Stop();
        var builder = new StringBuilder();
        builder.Append(LlmWikiService.FormatSearchResultsMarkdown(results));
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_recent",
            "recent recall candidates",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            categoryPath: categoryPath);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_recent",
            "recent recall candidates",
            stopwatch.Elapsed,
            response,
            categoryPath: categoryPath,
            requestedLimit: limit,
            effectiveLimit: safeLimit,
            resultCount: results.Count,
            resultIds: results.Select(x => x.Id).ToArray());
    }

    [McpServerTool(Name = "llm_wiki_read")]
    [Description("Read one authenticated-user LLM Wiki entry by id or slug.")]
    public async Task<string> ReadAsync(
        [Description("Entry id or slug returned by llm_wiki_search.")] string idOrSlug)
    {
        var user = RequireUser();
        var stopwatch = Stopwatch.StartNew();
        var entry = await llmWikiService.GetEntryAsync(user.UserName, idOrSlug);
        stopwatch.Stop();
        var response = entry is null
            ? "LLM Wiki entry not found for this user."
            : LlmWikiService.FormatEntryMarkdown(entry);
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_read",
            "full entry",
            stopwatch.Elapsed,
            response,
            idOrSlug,
            resultCount: entry is null ? 0 : 1,
            resultIds: entry is null ? [] : [entry.Id]);
    }

    [McpServerTool(Name = "llm_wiki_update")]
    [Description("Replace an existing LLM Wiki entry with corrected wording supplied by the agent. Read the entry first, then send the complete replacement prompt/content.")]
    public async Task<string> UpdateAsync(
        [Description("Entry id or slug returned by llm_wiki_search, llm_wiki_find_related, or llm_wiki_read.")] string idOrSlug,
        [Description("Complete corrected Source Prompt text to store. This replaces the previous Source Prompt.")] string prompt,
        [Description("Optional complete corrected Content text. Omit to keep existing content; pass an empty string to clear it.")] string? content = null,
        [Description("Optional corrected title. Omit to keep the current title.")] string? title = null,
        [Description("Optional corrected comma-separated tags. Omit to keep current tags; pass an empty string to clear them.")] string? tags = null,
        [Description("Corrected hierarchical category path. Pass it when the current category is vague or the project/topic is known. Omit only to keep the existing category.")] string? categoryPath = null)
    {
        var user = RequireUser();
        var stopwatch = Stopwatch.StartNew();
        var entry = await llmWikiService.UpdateAsync(
            user.UserName,
            idOrSlug,
            new LlmWikiUpdateRequest(prompt, content, title, tags, categoryPath));
        stopwatch.Stop();

        var response = entry is null
            ? "LLM Wiki entry not found for this user."
            : LlmWikiService.FormatEntryMarkdown(entry);
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_update",
            "full updated entry",
            stopwatch.Elapsed,
            response,
            prompt,
            categoryPath,
            resultCount: entry is null ? 0 : 1,
            resultIds: entry is null ? [] : [entry.Id]);
    }

    [McpServerTool(Name = "llm_wiki_merge")]
    [Description("Merge new durable knowledge into an existing LLM Wiki entry. Read the existing entry first, compose final merged wording yourself, then call this tool.")]
    public async Task<string> MergeAsync(
        [Description("Entry id or slug returned by llm_wiki_find_related, llm_wiki_search, or llm_wiki_read.")] string idOrSlug,
        [Description("Complete merged Source Prompt text. The agent must combine old and new knowledge before sending this.")] string mergedPrompt,
        [Description("Optional complete merged Content text. Omit to keep existing content; pass an empty string to clear it.")] string? mergedContent = null,
        [Description("Optional merged title. Omit to keep the current title.")] string? title = null,
        [Description("Optional merged comma-separated tags. Omit to keep current tags; pass an empty string to clear them.")] string? tags = null,
        [Description("Merged hierarchical category path. Pass it when the merged memory should move into a clearer project/domain/topic path. Omit only to keep the existing category.")] string? categoryPath = null)
    {
        var user = RequireUser();
        var stopwatch = Stopwatch.StartNew();
        var entry = await llmWikiService.UpdateAsync(
            user.UserName,
            idOrSlug,
            new LlmWikiUpdateRequest(mergedPrompt, mergedContent, title, tags, categoryPath),
            sourceAction: "merge");
        stopwatch.Stop();

        var response = entry is null
            ? "LLM Wiki entry not found for this user."
            : LlmWikiService.FormatEntryMarkdown(entry);
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_merge",
            "full merged entry",
            stopwatch.Elapsed,
            response,
            mergedPrompt,
            categoryPath,
            resultCount: entry is null ? 0 : 1,
            resultIds: entry is null ? [] : [entry.Id]);
    }

    [McpServerTool(Name = "llm_wiki_make_public")]
    [Description("Make matching authenticated-user LLM Wiki entries public. Use only after the user explicitly asks to disclose that topic to everyone.")]
    public async Task<string> MakePublicAsync(
        [Description("The user's explicit publication request, such as '내 종교 및 신앙관을 모든 사람이 알 수 있게 해줘'.")] string explicitRequest,
        [Description("Recall terms for the owned LLM Wiki entries to publish. Use the topic named in the explicit request.")] string query,
        [Description("Maximum number of matching entries to publish.")] int limit = 5,
        [Description("Optional hierarchical category path. Matching includes descendants.")] string? categoryPath = null,
        [Description("Minimum recall relevance percent for GraphRAG matches. Raise this when recall candidates are too broad or unrelated.")] int minRelevancePercent = 50)
    {
        var user = RequireUser();
        var safeLimit = NormalizeMcpLimit(limit, 5, 10);
        var safeMinRelevancePercent = NormalizeRelevancePercent(minRelevancePercent);
        var stopwatch = Stopwatch.StartNew();
        var entries = await llmWikiService.PublishMatchingEntriesAsync(
            user.UserName,
            explicitRequest,
            query,
            safeLimit,
            safeMinRelevancePercent,
            categoryPath);
        stopwatch.Stop();

        var builder = new StringBuilder();
        if (entries.Count == 0)
        {
            builder.AppendLine("No matching owned LLM Wiki recall candidates were found, so nothing was made public.");
        }
        else
        {
            builder.AppendLine("# LLM Wiki Public Sharing Updated");
            builder.AppendLine();
            builder.AppendLine("The entries below are public. Other authenticated Slogs MCP users can read their current Source Prompt and Content through public LLM Wiki tools. Raw Provenance remains private.");
            builder.AppendLine();
            foreach (var entry in entries)
            {
                var tags = entry.Tags.Count == 0 ? string.Empty : $" Memory clues: {string.Join(", ", entry.Tags)}.";
                var publishedAt = entry.PublishedAt is null ? string.Empty : $" PublishedAt: {entry.PublishedAt:O}.";
                builder.AppendLine($"- `{entry.Id}` [{entry.CategoryPath}] {entry.Title}.{publishedAt}{tags}");
            }
        }

        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_make_public",
            "public memory-sharing update",
            stopwatch.Elapsed,
            entries.Count,
            limit,
            safeLimit,
            query,
            categoryPath,
            safeMinRelevancePercent);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_make_public",
            "public memory-sharing update",
            stopwatch.Elapsed,
            response,
            query,
            categoryPath,
            limit,
            safeLimit,
            safeMinRelevancePercent,
            entries.Count,
            entries.Select(x => x.Id).ToArray());
    }

    [McpServerTool(Name = "llm_wiki_recall")]
    [Description("Recall compact memory context for a user request. Use when applying prior decisions, preferences, or project context; use llm_wiki_search first when only selecting candidates.")]
    public async Task<string> RecallAsync(
        [Description("What the user wants to recall or the current task context.")] string query,
        [Description("Maximum number of compact memory-context entries to return.")] int limit = 3,
        [Description("Minimum recall relevance percent for GraphRAG matches. Raise this when recall candidates are too broad or unrelated.")] int minRelevancePercent = 50,
        [Description(AdaptiveGraphHopDescription)] int maxGraphHops = 1)
    {
        var user = RequireUser();
        var safeLimit = NormalizeMcpLimit(limit, 3, 5);
        var safeMinRelevancePercent = NormalizeRelevancePercent(minRelevancePercent);
        var safeMaxGraphHops = Math.Clamp(maxGraphHops, 1, 3);
        var stopwatch = Stopwatch.StartNew();
        var pairScoreCalls = 0;
        var pairScoreCandidates = 0;
        IReadOnlyList<LlmWikiSearchResult> results;
        KnowledgeChunkRecall[] corpusResults;
        if (corpusService is null || corpusPrincipalResolver is null)
        {
            results = await llmWikiService.SearchAsync(
                user.UserName,
                query,
                safeLimit,
                minRelevancePercent: safeMinRelevancePercent,
                maxGraphHops: safeMaxGraphHops);
            corpusResults = [];
        }
        else
        {
            var corpusActor = await corpusPrincipalResolver.ResolveAsync(user);
            var corpusRecallLimit = CalculateCorpusRecallLimit(safeLimit, safeMaxGraphHops);
            var hasSingleExplicitLocator = KnowledgeCorpusService.HasSingleExplicitLocatorQuery(query);
            var useCombinedReranking = !hasSingleExplicitLocator &&
                KnowledgeRecallRouting.ShouldUseFullFunctionReranking(
                    safeMaxGraphHops,
                    requested: true,
                    supported: embeddingService?.SupportsFullFunctionReranking == true);
            var corpusTask = corpusService.RecallAsync(
                corpusActor,
                query,
                corpusRecallLimit,
                safeMaxGraphHops,
                applyFullFunctionReranking: !useCombinedReranking);
            if (hasSingleExplicitLocator)
            {
                corpusResults = (await corpusTask)
                    .Where(item => item.RelevancePercent >= safeMinRelevancePercent)
                    .ToArray();
                results = corpusResults.Length == 0
                    ? await llmWikiService.SearchAsync(
                        user.UserName,
                        query,
                        safeLimit,
                        minRelevancePercent: safeMinRelevancePercent,
                        maxGraphHops: safeMaxGraphHops)
                    : [];
            }
            else
            {
                var memoryTask = llmWikiService.SearchAsync(
                    user.UserName,
                    query,
                    safeLimit,
                    minRelevancePercent: useCombinedReranking ? 0 : safeMinRelevancePercent,
                    maxGraphHops: safeMaxGraphHops,
                    applyFullFunctionReranking: !useCombinedReranking);
                corpusResults = (await corpusTask).ToArray();
                results = await memoryTask;
                if (useCombinedReranking)
                {
                    var reranked = await RerankCombinedRecallCandidatesAsync(
                        user.UserName,
                        query,
                        results,
                        corpusResults,
                        httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
                    results = reranked.Memories
                        .Where(item => (item.RelevancePercent ?? 0) >= safeMinRelevancePercent)
                        .ToArray();
                    corpusResults = reranked.CorpusResults
                        .Where(item => item.RelevancePercent >= safeMinRelevancePercent)
                        .ToArray();
                    pairScoreCalls = reranked.PairScoreCalls;
                    pairScoreCandidates = reranked.PairScoreCandidates;
                }
                else
                {
                    corpusResults = corpusResults
                        .Where(item => item.RelevancePercent >= safeMinRelevancePercent)
                        .ToArray();
                }
            }
        }
        var selectedCandidates = SelectCombinedRecallCandidates(
            results,
            corpusResults,
            safeLimit,
            preferSubstantiveGraphRelations: safeMaxGraphHops > 1);
        var selectedMemoryIndexes = selectedCandidates
            .Where(candidate => !candidate.IsCorpus)
            .Select(candidate => candidate.SourceIndex)
            .ToHashSet();
        var selectedCorpusIndexes = selectedCandidates
            .Where(candidate => candidate.IsCorpus)
            .Select(candidate => candidate.SourceIndex)
            .ToHashSet();
        var selectedResults = results
            .Where((_, index) => selectedMemoryIndexes.Contains(index))
            .ToArray();
        var selectedCorpusResults = corpusResults
            .Where((_, index) => selectedCorpusIndexes.Contains(index))
            .ToArray();
        var totalResultCount = selectedCandidates.Count;
        if (totalResultCount == 0)
        {
            stopwatch.Stop();
            var emptyBuilder = new StringBuilder();
            emptyBuilder.AppendLine("No matching LLM Wiki recall candidates.");
            AppendRetrievalDiagnostics(
                emptyBuilder,
                "llm_wiki_recall",
                "compact recall context",
                stopwatch.Elapsed,
                0,
                limit,
                safeLimit,
                query,
                minRelevancePercent: safeMinRelevancePercent,
                maxGraphHops: safeMaxGraphHops,
                retrievalProfile: KnowledgeRecallRouting.GetProfile(safeMaxGraphHops),
                pairScoreCalls: pairScoreCalls,
                pairScoreCandidates: pairScoreCandidates);
            var emptyResponse = emptyBuilder.ToString();
            return await RecordAuditAndReturnAsync(
                user,
                "llm_wiki_recall",
                "compact recall context",
                stopwatch.Elapsed,
                emptyResponse,
                query,
                requestedLimit: limit,
                effectiveLimit: safeLimit,
                minRelevancePercent: safeMinRelevancePercent);
        }

        var builder = new StringBuilder();
        builder.AppendLine("# LLM Wiki Recall");
        builder.AppendLine();
        builder.AppendLine("Recall returns compact personal-memory context and accessible large-corpus evidence. Use `llm_wiki_read` on a selected personal-memory candidate when you need its full entry and provenance.");
        builder.AppendLine();
        var entriesById = selectedResults.Length == 0
            ? new Dictionary<Guid, LlmWikiEntryResponse>()
            : await llmWikiService.GetEntriesAsync(
                user.UserName,
                selectedResults.Select(x => x.Id).ToArray(),
                recordAccess: true);

        void AppendMemoryResults()
        {
            foreach (var result in selectedResults)
            {
                if (!entriesById.TryGetValue(result.Id, out var entry))
                {
                    continue;
                }

                builder.AppendLine(FormatRecallEntryMarkdown(
                    entry,
                    result.RelevancePercent,
                    result.GraphDepth,
                    result.GraphScore,
                    result.SemanticPath).Trim());
                builder.AppendLine();
                builder.AppendLine("---");
                builder.AppendLine();
            }
        }

        void AppendCorpusResults()
        {
            if (selectedCorpusResults.Length == 0)
            {
                return;
            }

            builder.AppendLine(KnowledgeCorpusMcpTools.FormatRecall(selectedCorpusResults));
            builder.AppendLine();
        }

        if (selectedCandidates[0].IsCorpus)
        {
            AppendCorpusResults();
            AppendMemoryResults();
        }
        else
        {
            AppendMemoryResults();
            AppendCorpusResults();
        }

        stopwatch.Stop();
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_recall",
            "compact recall context",
            stopwatch.Elapsed,
            totalResultCount,
            limit,
            safeLimit,
            query,
            minRelevancePercent: safeMinRelevancePercent,
            maxGraphHops: safeMaxGraphHops,
            retrievalProfile: KnowledgeRecallRouting.GetProfile(safeMaxGraphHops),
            pairScoreCalls: pairScoreCalls,
            pairScoreCandidates: pairScoreCandidates);
        var response = builder.ToString().TrimEnd();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_recall",
            "compact recall context",
            stopwatch.Elapsed,
            response,
            query,
            requestedLimit: limit,
            effectiveLimit: safeLimit,
            minRelevancePercent: safeMinRelevancePercent,
            resultCount: totalResultCount,
            resultIds: selectedResults.Select(x => x.Id).ToArray());
    }

    internal static IReadOnlyList<CombinedRecallCandidate> SelectCombinedRecallCandidates(
        IReadOnlyList<LlmWikiSearchResult> memories,
        IReadOnlyList<KnowledgeChunkRecall> corpusResults,
        int limit,
        bool preferSubstantiveGraphRelations = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        return memories
            .Select((memory, index) => new CombinedRecallCandidate(
                false,
                index,
                memory.RelevancePercent ?? 0))
            .Concat(corpusResults.Select((chunk, index) => new CombinedRecallCandidate(
                true,
                index,
                chunk.RelevancePercent,
                chunk.Relations.Any(IsSubstantiveGraphRelation))))
            .OrderByDescending(candidate => preferSubstantiveGraphRelations && candidate.HasSubstantiveGraphRelation)
            .ThenByDescending(candidate => candidate.RelevancePercent)
            .ThenBy(candidate => candidate.IsCorpus)
            .ThenBy(candidate => candidate.SourceIndex)
            .Take(limit)
            .ToArray();
    }

    private static bool IsSubstantiveGraphRelation(KnowledgeRelationRecall relation)
        => !string.Equals(relation.RelationType, "contains_passage", StringComparison.OrdinalIgnoreCase);

    internal static int CalculateCorpusRecallLimit(int responseLimit, int maxGraphHops)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(responseLimit, 1);
        return maxGraphHops > 1 ? Math.Max(responseLimit, 10) : responseLimit;
    }

    internal static IReadOnlyList<CombinedRecallCandidate> SelectCombinedRerankCandidates(
        IReadOnlyList<LlmWikiSearchResult> memories,
        IReadOnlyList<KnowledgeChunkRecall> corpusResults)
    {
        var memoryCandidates = memories
            .Select((memory, index) => new CombinedRecallCandidate(false, index, memory.RelevancePercent ?? 0))
            .ToArray();
        var corpusCandidates = corpusResults
            .Select((chunk, index) => new CombinedRecallCandidate(
                true,
                index,
                chunk.RelevancePercent,
                chunk.Relations.Any(IsSubstantiveGraphRelation)))
            .OrderByDescending(candidate => candidate.HasSubstantiveGraphRelation)
            .ThenByDescending(candidate => candidate.RelevancePercent)
            .ThenBy(candidate => candidate.SourceIndex)
            .ToArray();
        var hasSubstantiveCorpusEvidence = corpusCandidates.Any(candidate => candidate.HasSubstantiveGraphRelation);
        var memoryQuota = hasSubstantiveCorpusEvidence ? 1 : 3;
        var corpusQuota = MaxCombinedRecallRerankCandidates - memoryQuota;
        var selected = memoryCandidates.Take(memoryQuota)
            .Concat(corpusCandidates.Take(corpusQuota))
            .ToList();
        if (selected.Count < MaxCombinedRecallRerankCandidates)
        {
            var selectedKeys = selected
                .Select(candidate => (candidate.IsCorpus, candidate.SourceIndex))
                .ToHashSet();
            selected.AddRange(memoryCandidates
                .Concat(corpusCandidates)
                .Where(candidate => !selectedKeys.Contains((candidate.IsCorpus, candidate.SourceIndex)))
                .OrderByDescending(candidate => candidate.RelevancePercent)
                .Take(MaxCombinedRecallRerankCandidates - selected.Count));
        }
        return selected;
    }

    private async Task<CombinedRecallRerankOutcome> RerankCombinedRecallCandidatesAsync(
        string ownerUserName,
        string query,
        IReadOnlyList<LlmWikiSearchResult> memories,
        KnowledgeChunkRecall[] corpusResults,
        CancellationToken cancellationToken)
    {
        if (embeddingService is null || !embeddingService.SupportsFullFunctionReranking)
        {
            return new(memories, corpusResults, 0, 0);
        }

        var candidates = SelectCombinedRerankCandidates(memories, corpusResults);
        if (candidates.Count == 0)
        {
            return new(memories, corpusResults, 0, 0);
        }
        var memoryIds = candidates
            .Where(candidate => !candidate.IsCorpus)
            .Select(candidate => memories[candidate.SourceIndex].Id)
            .ToArray();
        var entriesById = memoryIds.Length == 0
            ? new Dictionary<Guid, LlmWikiEntryResponse>()
            : await llmWikiService.GetEntriesAsync(
                ownerUserName,
                memoryIds,
                recordAccess: false,
                cancellationToken);
        var passages = candidates.Select(candidate => candidate.IsCorpus
                ? BuildCorpusRerankPassage(corpusResults[candidate.SourceIndex])
                : entriesById.TryGetValue(memories[candidate.SourceIndex].Id, out var entry)
                    ? LlmWikiService.BuildBgeM3CombinedRerankDocument(entry)
                    : throw new InvalidOperationException("Combined recall rerank memory source is missing."))
            .ToArray();
        var scores = await embeddingService.ScorePairsAsync(query, passages, cancellationToken);
        if (scores.Count != candidates.Count)
        {
            throw new InvalidOperationException(
                $"Combined recall BGE-M3 rerank count mismatch: scores={scores.Count}, candidates={candidates.Count}.");
        }

        var rerankedMemories = new List<LlmWikiSearchResult>(candidates.Count);
        var rerankedCorpus = new List<KnowledgeChunkRecall>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var relevance = CalculateCombinedRerankRelevance(candidate, scores[index].Combined);
            if (candidate.IsCorpus)
            {
                rerankedCorpus.Add(corpusResults[candidate.SourceIndex] with
                {
                    RelevancePercent = relevance
                });
            }
            else
            {
                rerankedMemories.Add(memories[candidate.SourceIndex] with
                {
                    RelevancePercent = relevance
                });
            }
        }

        return new(rerankedMemories, rerankedCorpus.ToArray(), 1, candidates.Count);
    }

    internal static int CalculateCombinedRerankRelevance(CombinedRecallCandidate candidate, float combinedScore)
    {
        var reranked = (int)Math.Round(Math.Clamp(
            (combinedScore * 0.8f) + ((candidate.RelevancePercent / 100f) * 0.2f),
            0f,
            1f) * 100f);
        return candidate.HasSubstantiveGraphRelation
            ? Math.Max(candidate.RelevancePercent, reranked)
            : reranked;
    }

    private static string BuildCorpusRerankPassage(KnowledgeChunkRecall chunk)
        => $"domain: {chunk.Domain}\ndocument: {chunk.DocumentTitle}\nlocator: {chunk.StartLocator}..{chunk.EndLocator}\n{chunk.Text}";

    [McpServerTool(Name = "llm_wiki_public_search")]
    [Description("Search owner-authorized public-memory recall candidates published by a specified Slogs user such as @dimohy. When the user's question mentions @username and asks about that user's public memory context, use that handle as ownerUserName and the remaining topic words as query. Use for public self-disclosed sensitive topics such as religion or faith perspective. This never returns private entries or Raw Provenance.")]
    public async Task<string> PublicSearchAsync(
        [Description("Target public LLM Wiki owner. Accepts handles like @dimohy or dimohy; @username in the user prompt should be passed here.")] string ownerUserName,
        [Description("Recall terms from the rest of the user's question after removing the @username handle. Leave empty to return recent public-memory candidates.")] string? query = null,
        [Description("Maximum number of public-memory recall candidates to return.")] int limit = 10,
        [Description("Optional hierarchical category path. Matching includes descendants.")] string? categoryPath = null,
        [Description("Minimum recall relevance percent for GraphRAG matches. Raise this when recall candidates are too broad or unrelated.")] int minRelevancePercent = 50,
        [Description(AdaptiveGraphHopDescription)] int maxGraphHops = 1)
    {
        var user = RequireUser();
        var targetOwner = RequirePublicOwner(ownerUserName);
        var safeLimit = NormalizeMcpLimit(limit, 10, 10);
        var safeMinRelevancePercent = NormalizeRelevancePercent(minRelevancePercent);
        var safeMaxGraphHops = Math.Clamp(maxGraphHops, 1, 3);
        var stopwatch = Stopwatch.StartNew();
        var results = await llmWikiService.SearchPublicAsync(
            targetOwner,
            query,
            safeLimit,
            minRelevancePercent: safeMinRelevancePercent,
            categoryPath: categoryPath,
            maxGraphHops: safeMaxGraphHops);
        stopwatch.Stop();

        var builder = new StringBuilder();
        builder.AppendLine($"# {FormatPublicOwner(targetOwner)} Public Memory Recall Candidates");
        builder.AppendLine();
        builder.AppendLine("Only public entries are included. Raw Provenance is not exposed.");
        builder.AppendLine(PublicDisclosureNotice);
        builder.AppendLine();
        builder.Append(LlmWikiService.FormatSearchResultsMarkdown(results));
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_public_search",
            "public memory recall candidates",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            query,
            categoryPath,
            safeMinRelevancePercent,
            safeMaxGraphHops);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_public_search",
            "public memory recall candidates",
            stopwatch.Elapsed,
            response,
            query,
            categoryPath,
            limit,
            safeLimit,
            safeMinRelevancePercent,
            results.Count,
            results.Select(x => x.Id).ToArray());
    }

    [McpServerTool(Name = "llm_wiki_public_list")]
    [Description("Return owner-authorized public-memory entries published by a specified Slogs user such as @dimohy. Use when a prompt asks for @username's public memory flow. This never returns private entries.")]
    public async Task<string> PublicListAsync(
        [Description("Target public LLM Wiki owner. Accepts handles like @dimohy or dimohy; @username in the user prompt should be passed here.")] string ownerUserName,
        [Description("Maximum number of public-memory recall candidates to return.")] int limit = 10,
        [Description("Optional hierarchical category path. Matching includes descendants.")] string? categoryPath = null)
    {
        var user = RequireUser();
        var targetOwner = RequirePublicOwner(ownerUserName);
        var safeLimit = NormalizeMcpLimit(limit, 10, 50);
        var stopwatch = Stopwatch.StartNew();
        var results = await llmWikiService.SearchPublicAsync(targetOwner, null, safeLimit, categoryPath: categoryPath);
        stopwatch.Stop();

        var builder = new StringBuilder();
        builder.AppendLine($"# {FormatPublicOwner(targetOwner)} Public Memory Flow");
        builder.AppendLine();
        builder.AppendLine("Only public entries are included. Raw Provenance is not exposed.");
        builder.AppendLine(PublicDisclosureNotice);
        builder.AppendLine();
        builder.Append(LlmWikiService.FormatSearchResultsMarkdown(results));
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_public_list",
            "public memory flow",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            FormatPublicOwner(targetOwner),
            categoryPath);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_public_list",
            "public memory flow",
            stopwatch.Elapsed,
            response,
            FormatPublicOwner(targetOwner),
            categoryPath,
            limit,
            safeLimit,
            resultCount: results.Count,
            resultIds: results.Select(x => x.Id).ToArray());
    }

    [McpServerTool(Name = "llm_wiki_public_read")]
    [Description("Read one owner-authorized public-memory entry by Slogs owner handle and id or slug. Use returned public content as answerable public self-disclosure, including religion or faith perspective when present. This never returns private entries or Raw Provenance.")]
    public async Task<string> PublicReadAsync(
        [Description("Target public LLM Wiki owner. Accepts handles like @dimohy or dimohy; @username in the user prompt should be passed here.")] string ownerUserName,
        [Description("Public entry id or slug returned by llm_wiki_public_search or llm_wiki_public_list.")] string idOrSlug)
    {
        var user = RequireUser();
        var targetOwner = RequirePublicOwner(ownerUserName);
        var stopwatch = Stopwatch.StartNew();
        var entry = await llmWikiService.GetPublicEntryAsync(targetOwner, idOrSlug);
        stopwatch.Stop();
        var response = entry is null
            ? $"Public memory entry not found for {FormatPublicOwner(targetOwner)}."
            : LlmWikiService.FormatPublicEntryMarkdown(targetOwner, entry);
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_public_read",
            "public memory entry",
            stopwatch.Elapsed,
            response,
            idOrSlug,
            resultCount: entry is null ? 0 : 1,
            resultIds: entry is null ? [] : [entry.Id]);
    }

    [McpServerTool(Name = "llm_wiki_public_recall")]
    [Description("Recall compact owner-authorized public-memory context from a specified Slogs user such as @dimohy. When the user's question mentions @username, treat it as a Slogs handle, pass it as ownerUserName, and use the remaining words as query. Use for questions about another user's public beliefs, religion, faith perspective, preferences, or published context. Do not infer beyond returned public entries.")]
    public async Task<string> PublicRecallAsync(
        [Description("Target public LLM Wiki owner. Accepts handles like @dimohy or dimohy; @username in the user prompt should be passed here.")] string ownerUserName,
        [Description("What public context to recall from the target user's LLM Wiki, usually the remaining topic words after removing @username from the prompt.")] string query,
        [Description("Maximum number of compact public-memory context entries to return.")] int limit = 3,
        [Description("Minimum recall relevance percent for GraphRAG matches. Raise this when recall candidates are too broad or unrelated.")] int minRelevancePercent = 50,
        [Description(AdaptiveGraphHopDescription)] int maxGraphHops = 1)
    {
        var user = RequireUser();
        var targetOwner = RequirePublicOwner(ownerUserName);
        var safeLimit = NormalizeMcpLimit(limit, 3, 5);
        var safeMinRelevancePercent = NormalizeRelevancePercent(minRelevancePercent);
        var safeMaxGraphHops = Math.Clamp(maxGraphHops, 1, 3);
        var stopwatch = Stopwatch.StartNew();
        var results = await llmWikiService.SearchPublicAsync(
            targetOwner,
            query,
            safeLimit,
            minRelevancePercent: safeMinRelevancePercent,
            maxGraphHops: safeMaxGraphHops);
        if (results.Count == 0)
        {
            stopwatch.Stop();
            var emptyBuilder = new StringBuilder();
            emptyBuilder.AppendLine($"No matching public memory recall candidates for {FormatPublicOwner(targetOwner)}.");
            AppendRetrievalDiagnostics(
                emptyBuilder,
                "llm_wiki_public_recall",
                "public memory context",
                stopwatch.Elapsed,
                0,
                limit,
                safeLimit,
                query,
                minRelevancePercent: safeMinRelevancePercent,
                maxGraphHops: safeMaxGraphHops);
            var emptyResponse = emptyBuilder.ToString();
            return await RecordAuditAndReturnAsync(
                user,
                "llm_wiki_public_recall",
                "public memory context",
                stopwatch.Elapsed,
                emptyResponse,
                query,
                requestedLimit: limit,
                effectiveLimit: safeLimit,
                minRelevancePercent: safeMinRelevancePercent);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"# {FormatPublicOwner(targetOwner)} Public Memory Recall");
        builder.AppendLine();
        builder.AppendLine("Recall returns compact public context without Raw Provenance. Private entries are not included.");
        builder.AppendLine(PublicDisclosureNotice);
        builder.AppendLine();
        var entriesById = await llmWikiService.GetPublicEntriesAsync(
            targetOwner,
            results.Select(x => x.Id).ToArray(),
            recordAccess: true);
        foreach (var result in results)
        {
            if (!entriesById.TryGetValue(result.Id, out var entry))
            {
                continue;
            }

            builder.AppendLine(FormatRecallEntryMarkdown(
                entry,
                result.RelevancePercent,
                result.GraphDepth,
                result.GraphScore,
                result.SemanticPath).Trim());
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }

        stopwatch.Stop();
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_public_recall",
            "public memory context",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            query,
            minRelevancePercent: safeMinRelevancePercent,
            maxGraphHops: safeMaxGraphHops);
        var response = builder.ToString().TrimEnd();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_public_recall",
            "public memory context",
            stopwatch.Elapsed,
            response,
            query,
            requestedLimit: limit,
            effectiveLimit: safeLimit,
            minRelevancePercent: safeMinRelevancePercent,
            resultCount: results.Count,
            resultIds: results.Select(x => x.Id).ToArray());
    }

    [McpServerTool(Name = "llm_wiki_llms_txt")]
    [Description("Return a user-scoped llms.txt style index for the authenticated user's LLM Wiki.")]
    public async Task<string> GetLlmsTextAsync(
        [Description("Maximum number of entries to include.")] int limit = 50)
    {
        var user = RequireUser();
        var safeLimit = NormalizeMcpLimit(limit, 50, 200);
        var stopwatch = Stopwatch.StartNew();
        var response = await llmWikiService.BuildLlmsTextAsync(user.UserName, safeLimit);
        stopwatch.Stop();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_llms_txt",
            "llms.txt index",
            stopwatch.Elapsed,
            response,
            requestedLimit: limit,
            effectiveLimit: safeLimit);
    }

    [McpServerTool(Name = "llm_wiki_categories")]
    [Description("List the authenticated user's hierarchical LLM Wiki categories with counts and depth.")]
    public async Task<string> CategoriesAsync()
    {
        var user = RequireUser();
        var stopwatch = Stopwatch.StartNew();
        var categories = await llmWikiService.GetCategoriesAsync(user.UserName);
        stopwatch.Stop();
        if (categories.Count == 0)
        {
            return await RecordAuditAndReturnAsync(
                user,
                "llm_wiki_categories",
                "category list",
                stopwatch.Elapsed,
                "No LLM Wiki categories.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("# LLM Wiki Categories");
        builder.AppendLine();
        foreach (var category in categories)
        {
            builder.AppendLine($"- {category.CategoryPath}: {category.Count} entries, depth {category.CategoryDepth}");
        }

        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_categories",
            "category list",
            stopwatch.Elapsed,
            response,
            resultCount: categories.Count);
    }

    private async Task<string> RecordAuditAndReturnAsync(
        AuthUser user,
        string toolName,
        string responseMode,
        TimeSpan elapsed,
        string response,
        string? query = null,
        string? categoryPath = null,
        int? requestedLimit = null,
        int? effectiveLimit = null,
        int? minRelevancePercent = null,
        int resultCount = 0,
        IReadOnlyList<Guid>? resultIds = null)
    {
        var elapsedMs = elapsed.TotalMilliseconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(0, (int)Math.Round(elapsed.TotalMilliseconds));
        await llmWikiService.RecordMcpAuditAsync(
            user.UserName,
            new LlmWikiMcpAuditRequest(
                toolName,
                responseMode,
                query,
                categoryPath,
                requestedLimit,
                effectiveLimit,
                minRelevancePercent,
                resultCount,
                resultIds ?? [],
                elapsedMs,
                response.Length));
        return response;
    }

    private AuthUser RequireUser()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (string.Equals(
                principal?.FindFirst(OrganizationClaimTypes.ActorKind)?.Value,
                OrganizationActorKinds.Service,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "조직 서비스 토큰은 개인 LLM Wiki를 사용할 수 없습니다. org_wiki_* 도구를 사용하세요.");
        }

        return SlogsAuthentication.TryCreateUser(principal)
            ?? throw new InvalidOperationException("Slogs MCP 인증이 필요합니다. Slogs 설정에서 MCP 토큰을 만든 뒤 Authorization: Bearer 토큰으로 연결하세요.");
    }

    private static string BuildRelatedQuery(string prompt, string? content, string? tags)
        => string.Join(
            Environment.NewLine,
            new[] { prompt, content, tags }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim()));

    private static string FormatRelatedResults(IReadOnlyList<LlmWikiSearchResult> results)
    {
        if (results.Count == 0)
        {
            return "No related LLM Wiki recall candidates found.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Related LLM Wiki Recall Candidates");
        builder.AppendLine();
        builder.AppendLine("Read a matching recall candidate with `llm_wiki_read` before merging or updating it.");
        builder.AppendLine();
        builder.Append(LlmWikiService.FormatSearchResultsMarkdown(results));
        return builder.ToString();
    }

    private static int NormalizeMcpLimit(int limit, int defaultValue, int maxValue)
        => Math.Clamp(limit <= 0 ? defaultValue : limit, 1, maxValue);

    private static int NormalizeRelevancePercent(int minRelevancePercent)
        => Math.Clamp(minRelevancePercent, 0, 100);

    private static string FormatRecallEntryMarkdown(
        LlmWikiEntryResponse entry,
        int? relevancePercent,
        int graphDepth,
        double graphScore,
        string semanticPath)
    {
        var builder = new StringBuilder();
        var relevance = relevancePercent is null ? string.Empty : $" ({relevancePercent}% recall relevance)";
        builder.AppendLine($"## {entry.Title}{relevance}");
        builder.AppendLine();
        builder.AppendLine(entry.Summary);
        builder.AppendLine();
        builder.AppendLine($"- id: {entry.Id}");
        builder.AppendLine($"- slug: {entry.Slug}");
        builder.AppendLine($"- graphDepth: {graphDepth}");
        builder.AppendLine($"- graphScore: {graphScore:F4}");
        if (!string.IsNullOrWhiteSpace(semanticPath))
        {
            builder.AppendLine($"- semanticPath: {semanticPath}");
        }
        builder.AppendLine($"- updated: {entry.UpdatedAt:O}");
        builder.AppendLine($"- memoryVisibility: {(entry.IsPublic ? "public memory" : "private memory")}");
        if (entry.PublishedAt is not null)
        {
            builder.AppendLine($"- publishedAt: {entry.PublishedAt:O}");
        }

        builder.AppendLine($"- category: {entry.CategoryPath}");
        if (entry.Tags.Count > 0)
        {
            builder.AppendLine($"- memoryClues: {string.Join(", ", entry.Tags)}");
        }

        builder.AppendLine();
        builder.AppendLine("### Source Prompt");
        builder.AppendLine();
        builder.AppendLine(TrimForMcp(entry.SourcePrompt, 1_600));

        if (!string.IsNullOrWhiteSpace(entry.Content))
        {
            builder.AppendLine();
            builder.AppendLine("### Content");
            builder.AppendLine();
            builder.AppendLine(TrimForMcp(entry.Content, 2_400));
        }

        return builder.ToString();
    }

    private static void AppendRetrievalDiagnostics(
        StringBuilder builder,
        string toolName,
        string responseMode,
        TimeSpan elapsed,
        int resultCount,
        int requestedLimit,
        int effectiveLimit,
        string? query = null,
        string? categoryPath = null,
        int? minRelevancePercent = null,
        int? maxGraphHops = null,
        string? retrievalProfile = null,
        int? pairScoreCalls = null,
        int? pairScoreCandidates = null)
    {
        builder.AppendLine();
        builder.AppendLine("## Retrieval Diagnostics");
        builder.AppendLine();
        builder.AppendLine($"- tool: `{toolName}`");
        builder.AppendLine($"- responseMode: {responseMode}");
        if (!string.IsNullOrWhiteSpace(query))
        {
            builder.AppendLine($"- query: {TrimForMcp(query, 240).ReplaceLineEndings(" ")}");
        }

        builder.AppendLine($"- recallCandidates: {resultCount}");
        builder.AppendLine($"- requestedLimit: {requestedLimit}");
        builder.AppendLine($"- effectiveLimit: {effectiveLimit}");
        if (!string.IsNullOrWhiteSpace(categoryPath))
        {
            builder.AppendLine($"- categoryPath: {categoryPath.Trim()}");
        }

        if (minRelevancePercent is not null)
        {
            builder.AppendLine($"- minRelevancePercent: {minRelevancePercent}");
        }

        if (maxGraphHops is not null)
        {
            builder.AppendLine($"- maxGraphHops: {maxGraphHops}");
        }

        if (!string.IsNullOrWhiteSpace(retrievalProfile))
        {
            builder.AppendLine($"- retrievalProfile: {retrievalProfile}");
        }

        if (pairScoreCalls is not null)
        {
            builder.AppendLine($"- pairScoreCalls: {pairScoreCalls}");
        }

        if (pairScoreCandidates is not null)
        {
            builder.AppendLine($"- pairScoreCandidates: {pairScoreCandidates}");
        }

        builder.AppendLine($"- elapsedMs: {Math.Round(elapsed.TotalMilliseconds)}");
        builder.AppendLine("- audit: If the top recall candidates are unrelated, missing expected memory, too broad, too slow, or too large, refine query/categoryPath/limit/minRelevancePercent/maxGraphHops and mention the mismatch when it affects the task.");
    }

    private static string TrimForMcp(string value, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return $"{trimmed[..maxLength].TrimEnd()}... [truncated; call `llm_wiki_read` for the full entry]";
    }

    private static string RequirePublicOwner(string ownerUserName)
    {
        var owner = (ownerUserName ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new InvalidOperationException("공개 기억 검색에는 @dimohy 같은 대상 슬로거 @name이 필요합니다.");
        }

        return owner;
    }

    private static string FormatPublicOwner(string ownerUserName)
        => $"@{RequirePublicOwner(ownerUserName)}";
}
