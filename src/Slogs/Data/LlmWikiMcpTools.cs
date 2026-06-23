using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Server;

namespace Slogs.Data;

[McpServerToolType]
public sealed class LlmWikiMcpTools(IHttpContextAccessor httpContextAccessor, LlmWikiService llmWikiService)
{
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
        var response = SlogsMcpPolicyPrompt.BuildMarkdown();
        stopwatch.Stop();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_instructions",
            "policy prompt",
            stopwatch.Elapsed,
            response,
            resultCount: 1);
    }

    [McpServerTool(Name = "llm_wiki_capture")]
    [Description("Start here when considering whether to remember a prompt or coding result. This does not store anything; it returns related memories and storage criteria for read, merge, update, or remember decisions.")]
    public async Task<string> CaptureAsync(
        [Description("The current user prompt, durable preference, decision, coding request, tacit workflow knowledge, or workflow fact being considered for memory.")] string prompt,
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
        builder.AppendLine("- If a related entry below matches, call `llm_wiki_read`, compose the final merged wording, then call `llm_wiki_merge` or `llm_wiki_update`.");
        builder.AppendLine("- Choose an explicit `categoryPath` such as `project/domain/topic` before remember, merge, or update when the project/topic is known.");
        builder.AppendLine("- If none match and the information is durable tacit knowledge, call `llm_wiki_remember` with that `categoryPath`.");
        builder.AppendLine("- Raw prompt/content/title/tags/categoryPath submitted through remember, merge, and update are preserved as Raw Provenance for later audit; do not remove prior raw evidence when composing merged wording.");
        builder.AppendLine("- Durable tacit knowledge means future LLMs can use it to document, automate, reproduce, or make decisions: corrected terminology, judgment criteria, repeatable workflows, operating rules, verified root causes, restart points, hidden prerequisites, or runbook-worthy command flows.");
        builder.AppendLine("- Do not store sensitive information, one-time logs, temporary execution traces, unverified speculation, simple facts recoverable from current files, or intermediate state that only matters in this turn.");
        builder.AppendLine("- Avoid interrupting the user for routine memory choices; ask only when sensitivity or scope is genuinely ambiguous.");
        builder.AppendLine();
        builder.Append(FormatRelatedResults(results));
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_capture",
            "related summary candidates",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            query);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_capture",
            "related summary candidates",
            stopwatch.Elapsed,
            response,
            query,
            requestedLimit: limit,
            effectiveLimit: safeLimit,
            resultCount: results.Count,
            resultIds: results.Select(x => x.Id).ToArray());
    }

    [McpServerTool(Name = "llm_wiki_find_related")]
    [Description("Find related user-scoped LLM Wiki entries before storing or merging memory. Use this before llm_wiki_remember unless llm_wiki_capture was already called.")]
    public async Task<string> FindRelatedAsync(
        [Description("Search text built from the current prompt, proposed memory, tags, and implementation result.")] string query,
        [Description("Maximum number of related entries to return.")] int limit = 5)
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
            "related summary candidates",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            query);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_find_related",
            "related summary candidates",
            stopwatch.Elapsed,
            response,
            query,
            requestedLimit: limit,
            effectiveLimit: safeLimit,
            resultCount: results.Count,
            resultIds: results.Select(x => x.Id).ToArray());
    }

    [McpServerTool(Name = "llm_wiki_search")]
    [Description("Search the authenticated user's LLM Wiki with compact summary results. Start here for broad lookup, candidate selection, category filtering, and low-token retrieval.")]
    public async Task<string> SearchAsync(
        [Description("Search terms. Leave empty to return recent entries.")] string? query = null,
        [Description("Maximum number of entries to return.")] int limit = 10,
        [Description("Optional hierarchical category path. Matching includes descendants.")] string? categoryPath = null,
        [Description("Minimum relevance percent for GraphRAG matches. Raise this when results are too broad or unrelated.")] int minRelevancePercent = 50)
    {
        var user = RequireUser();
        var safeLimit = NormalizeMcpLimit(limit, 10, 10);
        var safeMinRelevancePercent = NormalizeRelevancePercent(minRelevancePercent);
        var stopwatch = Stopwatch.StartNew();
        var results = await llmWikiService.SearchAsync(
            user.UserName,
            query,
            safeLimit,
            minRelevancePercent: safeMinRelevancePercent,
            categoryPath: categoryPath);
        stopwatch.Stop();
        var builder = new StringBuilder();
        builder.Append(LlmWikiService.FormatSearchResultsMarkdown(results));
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_search",
            "compact summaries",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            query,
            categoryPath,
            safeMinRelevancePercent);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_search",
            "compact summaries",
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
    [Description("List recent LLM Wiki entries for the authenticated user.")]
    public async Task<string> RecentAsync(
        [Description("Maximum number of recent entries to return.")] int limit = 10,
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
            "compact recent summaries",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            categoryPath: categoryPath);
        var response = builder.ToString();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_recent",
            "compact recent summaries",
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

    [McpServerTool(Name = "llm_wiki_recall")]
    [Description("Recall compact context memories for a user request. Use when applying prior decisions, preferences, or project context; use llm_wiki_search first when only selecting candidates.")]
    public async Task<string> RecallAsync(
        [Description("What the user wants to recall or the current task context.")] string query,
        [Description("Maximum number of compact context entries to return.")] int limit = 3,
        [Description("Minimum relevance percent for GraphRAG matches. Raise this when results are too broad or unrelated.")] int minRelevancePercent = 50)
    {
        var user = RequireUser();
        var safeLimit = NormalizeMcpLimit(limit, 3, 5);
        var safeMinRelevancePercent = NormalizeRelevancePercent(minRelevancePercent);
        var stopwatch = Stopwatch.StartNew();
        var results = await llmWikiService.SearchAsync(
            user.UserName,
            query,
            safeLimit,
            minRelevancePercent: safeMinRelevancePercent);
        if (results.Count == 0)
        {
            stopwatch.Stop();
            var emptyBuilder = new StringBuilder();
            emptyBuilder.AppendLine("No matching LLM Wiki entries.");
            AppendRetrievalDiagnostics(
                emptyBuilder,
                "llm_wiki_recall",
                "compact context",
                stopwatch.Elapsed,
                0,
                limit,
                safeLimit,
                query,
                minRelevancePercent: safeMinRelevancePercent);
            var emptyResponse = emptyBuilder.ToString();
            return await RecordAuditAndReturnAsync(
                user,
                "llm_wiki_recall",
                "compact context",
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
        builder.AppendLine("Recall returns compact context without Raw Provenance. Use `llm_wiki_read` on a selected id when you need the full entry and provenance.");
        builder.AppendLine();
        foreach (var result in results)
        {
            var entry = await llmWikiService.GetEntryAsync(user.UserName, result.Id.ToString());
            if (entry is null)
            {
                continue;
            }

            builder.AppendLine(FormatRecallEntryMarkdown(entry, result.RelevancePercent).Trim());
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }

        stopwatch.Stop();
        AppendRetrievalDiagnostics(
            builder,
            "llm_wiki_recall",
            "compact context",
            stopwatch.Elapsed,
            results.Count,
            limit,
            safeLimit,
            query,
            minRelevancePercent: safeMinRelevancePercent);
        var response = builder.ToString().TrimEnd();
        return await RecordAuditAndReturnAsync(
            user,
            "llm_wiki_recall",
            "compact context",
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
        => SlogsAuthentication.TryCreateUser(httpContextAccessor.HttpContext?.User)
            ?? throw new InvalidOperationException("Slogs MCP 인증이 필요합니다. Slogs 설정에서 MCP 토큰을 만든 뒤 Authorization: Bearer 토큰으로 연결하세요.");

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
            return "No related LLM Wiki entries found.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Related LLM Wiki Entries");
        builder.AppendLine();
        builder.AppendLine("Read a matching entry with `llm_wiki_read` before merging or updating it.");
        builder.AppendLine();
        builder.Append(LlmWikiService.FormatSearchResultsMarkdown(results));
        return builder.ToString();
    }

    private static int NormalizeMcpLimit(int limit, int defaultValue, int maxValue)
        => Math.Clamp(limit <= 0 ? defaultValue : limit, 1, maxValue);

    private static int NormalizeRelevancePercent(int minRelevancePercent)
        => Math.Clamp(minRelevancePercent, 0, 100);

    private static string FormatRecallEntryMarkdown(LlmWikiEntryResponse entry, int? relevancePercent)
    {
        var builder = new StringBuilder();
        var relevance = relevancePercent is null ? string.Empty : $" ({relevancePercent}% relevance)";
        builder.AppendLine($"## {entry.Title}{relevance}");
        builder.AppendLine();
        builder.AppendLine(entry.Summary);
        builder.AppendLine();
        builder.AppendLine($"- id: {entry.Id}");
        builder.AppendLine($"- slug: {entry.Slug}");
        builder.AppendLine($"- updated: {entry.UpdatedAt:O}");
        builder.AppendLine($"- category: {entry.CategoryPath}");
        if (entry.Tags.Count > 0)
        {
            builder.AppendLine($"- tags: {string.Join(", ", entry.Tags)}");
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
        int? minRelevancePercent = null)
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

        builder.AppendLine($"- results: {resultCount}");
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

        builder.AppendLine($"- elapsedMs: {Math.Round(elapsed.TotalMilliseconds)}");
        builder.AppendLine("- audit: If the top results are unrelated, missing expected entries, too broad, too slow, or too large, refine query/categoryPath/limit/minRelevancePercent and mention the mismatch when it affects the task.");
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
}
