using System.ComponentModel;
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
        [Description("Optional comma-separated tags such as preference, api, ux, or release.")] string? tags = null)
    {
        var user = RequireUser();
        var entry = await llmWikiService.RememberAsync(
            user.UserName,
            new LlmWikiRememberRequest(prompt, content, title, tags));

        return LlmWikiService.FormatEntryMarkdown(entry);
    }

    [McpServerTool(Name = "llm_wiki_instructions")]
    [Description("Read the operating policy for using this user's Slogs LLM Wiki. Call this once after connecting and follow it before storing or recalling memories.")]
    public string GetInstructions()
        => """
        # Slogs LLM Wiki Agent Instructions

        You are the decision-maker for this user's LLM Wiki. Slogs stores, searches, reads, and updates memories, but Slogs does not decide what should be remembered.

        ## Default workflow

        1. When the user provides a durable preference, project rule, deployment fact, account-specific convention, recurring workflow, or an implementation decision, call `llm_wiki_capture` or `llm_wiki_find_related` first.
        2. If a related entry exists, call `llm_wiki_read` before changing anything.
        3. If the new information refines an existing entry, compose a complete merged replacement and call `llm_wiki_merge`.
        4. If the new information corrects an existing entry, compose the corrected replacement and call `llm_wiki_update`.
        5. If no existing entry fits, call `llm_wiki_remember`.
        6. When the user asks to remember, recall, continue previous work, apply a preference, or asks "what did we decide", call `llm_wiki_recall`, `llm_wiki_search`, or `llm_wiki_recent`.

        ## Store

        Store durable knowledge, not raw chat logs. Prefer concise, reusable memory:
        - user preferences and judgment criteria
        - project-specific commands and paths
        - deployment and environment facts
        - decisions that should affect future work
        - restart points after interrupted work

        Do not store secrets, API keys, passwords, one-time codes, private tokens, full logs, or temporary noise.

        ## Merge

        Existing memories should stay coherent. Do not create duplicate entries when a related entry can be updated. `llm_wiki_merge` and `llm_wiki_update` replace the entry with text you provide, so read the existing entry first and send the final merged wording.
        """;

    [McpServerTool(Name = "llm_wiki_capture")]
    [Description("Start here when considering whether to remember a prompt or coding result. This does not store anything; it returns related memories and tells the agent whether to read, merge, update, or remember.")]
    public async Task<string> CaptureAsync(
        [Description("The current user prompt, durable preference, decision, coding request, or workflow fact being considered for memory.")] string prompt,
        [Description("Optional answer, implementation result, or extra context from the current turn.")] string? content = null,
        [Description("Optional comma-separated tags to help search related memory.")] string? tags = null,
        [Description("Maximum number of related entries to return.")] int limit = 5)
    {
        var user = RequireUser();
        var query = BuildRelatedQuery(prompt, content, tags);
        var results = await llmWikiService.SearchAsync(user.UserName, query, NormalizeMcpLimit(limit, 5, 10));

        var builder = new StringBuilder();
        builder.AppendLine("# LLM Wiki Capture Intake");
        builder.AppendLine();
        builder.AppendLine("No memory was created or updated by this tool.");
        builder.AppendLine();
        builder.AppendLine("Next action:");
        builder.AppendLine("- If a related entry below matches, call `llm_wiki_read`, compose the final merged wording, then call `llm_wiki_merge` or `llm_wiki_update`.");
        builder.AppendLine("- If none match and the information is durable, call `llm_wiki_remember`.");
        builder.AppendLine("- If this is one-time or sensitive information, do not store it.");
        builder.AppendLine();
        builder.Append(FormatRelatedResults(results));
        return builder.ToString();
    }

    [McpServerTool(Name = "llm_wiki_find_related")]
    [Description("Find related user-scoped LLM Wiki entries before storing or merging memory. Use this before llm_wiki_remember unless llm_wiki_capture was already called.")]
    public async Task<string> FindRelatedAsync(
        [Description("Search text built from the current prompt, proposed memory, tags, and implementation result.")] string query,
        [Description("Maximum number of related entries to return.")] int limit = 5)
    {
        var user = RequireUser();
        var results = await llmWikiService.SearchAsync(user.UserName, query, NormalizeMcpLimit(limit, 5, 10));
        return FormatRelatedResults(results);
    }

    [McpServerTool(Name = "llm_wiki_search")]
    [Description("Search the authenticated user's LLM Wiki with PostgreSQL full text search.")]
    public async Task<string> SearchAsync(
        [Description("Search terms. Leave empty to return recent entries.")] string? query = null,
        [Description("Maximum number of entries to return.")] int limit = 10)
    {
        var user = RequireUser();
        var results = await llmWikiService.SearchAsync(user.UserName, query, limit);
        return LlmWikiService.FormatSearchResultsMarkdown(results);
    }

    [McpServerTool(Name = "llm_wiki_recent")]
    [Description("List recent LLM Wiki entries for the authenticated user.")]
    public async Task<string> RecentAsync(
        [Description("Maximum number of recent entries to return.")] int limit = 10)
    {
        var user = RequireUser();
        var results = await llmWikiService.SearchAsync(user.UserName, null, limit);
        return LlmWikiService.FormatSearchResultsMarkdown(results);
    }

    [McpServerTool(Name = "llm_wiki_read")]
    [Description("Read one authenticated-user LLM Wiki entry by id or slug.")]
    public async Task<string> ReadAsync(
        [Description("Entry id or slug returned by llm_wiki_search.")] string idOrSlug)
    {
        var user = RequireUser();
        var entry = await llmWikiService.GetEntryAsync(user.UserName, idOrSlug);
        return entry is null
            ? "LLM Wiki entry not found for this user."
            : LlmWikiService.FormatEntryMarkdown(entry);
    }

    [McpServerTool(Name = "llm_wiki_update")]
    [Description("Replace an existing LLM Wiki entry with corrected wording supplied by the agent. Read the entry first, then send the complete replacement prompt/content.")]
    public async Task<string> UpdateAsync(
        [Description("Entry id or slug returned by llm_wiki_search, llm_wiki_find_related, or llm_wiki_read.")] string idOrSlug,
        [Description("Complete corrected Source Prompt text to store. This replaces the previous Source Prompt.")] string prompt,
        [Description("Optional complete corrected Content text. Omit to keep existing content; pass an empty string to clear it.")] string? content = null,
        [Description("Optional corrected title. Omit to keep the current title.")] string? title = null,
        [Description("Optional corrected comma-separated tags. Omit to keep current tags; pass an empty string to clear them.")] string? tags = null)
    {
        var user = RequireUser();
        var entry = await llmWikiService.UpdateAsync(
            user.UserName,
            idOrSlug,
            new LlmWikiUpdateRequest(prompt, content, title, tags));

        return entry is null
            ? "LLM Wiki entry not found for this user."
            : LlmWikiService.FormatEntryMarkdown(entry);
    }

    [McpServerTool(Name = "llm_wiki_merge")]
    [Description("Merge new durable knowledge into an existing LLM Wiki entry. Read the existing entry first, compose final merged wording yourself, then call this tool.")]
    public async Task<string> MergeAsync(
        [Description("Entry id or slug returned by llm_wiki_find_related, llm_wiki_search, or llm_wiki_read.")] string idOrSlug,
        [Description("Complete merged Source Prompt text. The agent must combine old and new knowledge before sending this.")] string mergedPrompt,
        [Description("Optional complete merged Content text. Omit to keep existing content; pass an empty string to clear it.")] string? mergedContent = null,
        [Description("Optional merged title. Omit to keep the current title.")] string? title = null,
        [Description("Optional merged comma-separated tags. Omit to keep current tags; pass an empty string to clear them.")] string? tags = null)
    {
        var user = RequireUser();
        var entry = await llmWikiService.UpdateAsync(
            user.UserName,
            idOrSlug,
            new LlmWikiUpdateRequest(mergedPrompt, mergedContent, title, tags));

        return entry is null
            ? "LLM Wiki entry not found for this user."
            : LlmWikiService.FormatEntryMarkdown(entry);
    }

    [McpServerTool(Name = "llm_wiki_recall")]
    [Description("Recall relevant memories for a user request. Use when the user asks to continue, remember prior decisions, apply preferences, or retrieve project context.")]
    public async Task<string> RecallAsync(
        [Description("What the user wants to recall or the current task context.")] string query,
        [Description("Maximum number of full entries to return.")] int limit = 3)
    {
        var user = RequireUser();
        var results = await llmWikiService.SearchAsync(user.UserName, query, NormalizeMcpLimit(limit, 3, 5));
        if (results.Count == 0)
        {
            return "No matching LLM Wiki entries.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("# LLM Wiki Recall");
        builder.AppendLine();
        foreach (var result in results)
        {
            var entry = await llmWikiService.GetEntryAsync(user.UserName, result.Id.ToString());
            if (entry is null)
            {
                continue;
            }

            builder.AppendLine(LlmWikiService.FormatEntryMarkdown(entry).Trim());
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    [McpServerTool(Name = "llm_wiki_llms_txt")]
    [Description("Return a user-scoped llms.txt style index for the authenticated user's LLM Wiki.")]
    public async Task<string> GetLlmsTextAsync(
        [Description("Maximum number of entries to include.")] int limit = 50)
    {
        var user = RequireUser();
        return await llmWikiService.BuildLlmsTextAsync(user.UserName, limit);
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
}
