using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Slogs.Data;

public sealed record LlmWikiMcpAuditRequest(
    string ToolName,
    string ResponseMode,
    string? Query,
    string? CategoryPath,
    int? RequestedLimit,
    int? EffectiveLimit,
    int? MinRelevancePercent,
    int ResultCount,
    IReadOnlyList<Guid> ResultIds,
    int ElapsedMs,
    int ResponseChars,
    bool Succeeded = true);

public sealed class LlmWikiService(
    IDbContextFactory<SlogsDbContext> dbFactory,
    EmbeddingGemmaService embeddingService)
{
    private const int MaxPromptLength = 12_000;
    private const int MaxContentLength = 80_000;
    private const int MaxTitleLength = 120;
    private const int MaxSummaryLength = 240;
    private const int MaxCategoryPathLength = 240;
    private const int MaxRawMetadataLength = 2_000;
    private const int MaxCategorySegmentLength = 48;
    private const int MaxEmbeddingContentLength = 18_000;
    private const int MaxGraphNodesPerEntry = 120;
    private const int MaxGraphNodeLength = 120;
    private const int MaxAuditInlineLength = 240;
    private const int MaxAuditResultIds = 5;
    private const int MaxSearchIndexRefreshPerQuery = 4;
    private const string SearchIndexVersion = "2026-06-27-public-sharing-v1";
    private static readonly string[] AllowedTokenScopes =
    [
        SlogsTokenScopes.Mcp,
        SlogsTokenScopes.ObsidianSync
    ];
    private static readonly string[] KoreanParticleSuffixes =
    [
        "으로",
        "에서",
        "에게",
        "부터",
        "까지",
        "처럼",
        "보다",
        "라고",
        "이라",
        "하며",
        "하고",
        "의",
        "은",
        "는",
        "이",
        "가",
        "을",
        "를",
        "와",
        "과",
        "로",
        "에",
        "께",
        "도",
        "만"
    ];

    public async Task<LlmWikiEntryResponse> RememberAsync(
        string ownerUserName,
        LlmWikiRememberRequest request,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var prompt = TrimToLength(request.Prompt, MaxPromptLength);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException("LLM Wiki 저장에는 로그인 사용자와 프롬프트가 필요합니다.");
        }

        var content = TrimToLength(request.Content, MaxContentLength);
        var title = DeriveTitle(request.Title, prompt, content);
        var tags = ParseTags(request.Tags);
        var categoryPath = NormalizeCategoryPath(request.CategoryPath, tags);
        var categoryDepth = GetCategoryDepth(categoryPath);
        var embeddingDocument = BuildEmbeddingDocument(title, prompt, content, tags, categoryPath);
        var embedding = await embeddingService.EmbedDocumentAsync(embeddingDocument, cancellationToken);
        var contentHash = ComputeSearchContentHash(embeddingDocument);
        var graphNodes = BuildGraphNodes(title, prompt, content, tags, categoryPath);
        var now = DateTime.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var slug = await CreateUniqueSlugAsync(db, owner, title, cancellationToken);
        var entry = new LlmWikiEntryRecord
        {
            Id = Guid.NewGuid(),
            OwnerUserName = owner,
            Slug = slug,
            Title = title,
            Summary = DeriveSummary(content, prompt),
            Content = content,
            SourcePrompt = prompt,
            TagsJson = SerializeTags(tags),
            CategoryPath = categoryPath,
            CategoryDepth = categoryDepth,
            CreatedAt = now,
            UpdatedAt = now
        };
        entry.Sources.Add(CreateSourceRecord(
            entry.Id,
            owner,
            "remember",
            request.Prompt,
            request.Content,
            request.Title,
            request.Tags,
            request.CategoryPath,
            now));

        db.LlmWikiEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await StoreEntrySearchIndexAsync(db, entry.Id, owner, contentHash, embedding, graphNodes, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToEntryResponse(entry);
    }

    public async Task<LlmWikiEntryResponse?> UpdateAsync(
        string ownerUserName,
        string idOrSlug,
        LlmWikiUpdateRequest request,
        CancellationToken cancellationToken = default,
        string sourceAction = "update")
    {
        var owner = NormalizeUser(ownerUserName);
        var prompt = TrimToLength(request.Prompt, MaxPromptLength);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException("LLM Wiki 갱신에는 로그인 사용자와 통합된 프롬프트가 필요합니다.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entry = await FindEntryAsync(db, owner, idOrSlug, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var content = request.Content is null
            ? entry.Content
            : TrimToLength(request.Content, MaxContentLength);
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? entry.Title
            : TrimToLength(CleanInlineText(request.Title), MaxTitleLength);
        var tags = request.Tags is null
            ? DeserializeTags(entry.TagsJson)
            : ParseTags(request.Tags);
        var categoryPath = request.CategoryPath is null
            ? NormalizeCategoryPath(entry.CategoryPath, tags)
            : NormalizeCategoryPath(request.CategoryPath, tags);
        var categoryDepth = GetCategoryDepth(categoryPath);
        var embeddingDocument = BuildEmbeddingDocument(title, prompt, content, tags, categoryPath);
        var embedding = await embeddingService.EmbedDocumentAsync(embeddingDocument, cancellationToken);
        var contentHash = ComputeSearchContentHash(embeddingDocument);
        var graphNodes = BuildGraphNodes(title, prompt, content, tags, categoryPath);
        var now = DateTime.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        entry.Title = title;
        entry.Summary = DeriveSummary(content, prompt);
        entry.Content = content;
        entry.SourcePrompt = prompt;
        entry.TagsJson = SerializeTags(tags);
        entry.CategoryPath = categoryPath;
        entry.CategoryDepth = categoryDepth;
        entry.UpdatedAt = now;
        var source = CreateSourceRecord(
            entry.Id,
            owner,
            sourceAction,
            request.Prompt,
            request.Content,
            request.Title,
            request.Tags,
            request.CategoryPath,
            now);
        source.Entry = entry;
        db.LlmWikiEntrySources.Add(source);

        await db.SaveChangesAsync(cancellationToken);
        await StoreEntrySearchIndexAsync(db, entry.Id, owner, contentHash, embedding, graphNodes, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToEntryResponse(entry);
    }

    public async Task<IReadOnlyList<LlmWikiEntryResponse>> PublishMatchingEntriesAsync(
        string ownerUserName,
        string explicitRequest,
        string query,
        int limit = 5,
        int minRelevancePercent = 50,
        string? categoryPath = null,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var authorizationText = TrimToLength(explicitRequest, MaxPromptLength);
        var searchText = TrimToLength(query, 400);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(authorizationText) || string.IsNullOrWhiteSpace(searchText))
        {
            throw new InvalidOperationException("LLM Wiki 공개 전환에는 로그인 사용자, 명시적 공개 요청, 검색어가 필요합니다.");
        }

        var safeLimit = NormalizeLimit(limit, 5, 10);
        var safeMinRelevancePercent = Math.Clamp(minRelevancePercent, 0, 100);
        var matches = await SearchAsync(
            owner,
            searchText,
            safeLimit,
            minRelevancePercent: safeMinRelevancePercent,
            categoryPath: categoryPath,
            cancellationToken: cancellationToken);
        if (matches.Count == 0)
        {
            return [];
        }

        var matchOrder = matches
            .Select((match, index) => new { match.Id, index })
            .ToDictionary(x => x.Id, x => x.index);
        var matchedIds = matchOrder.Keys.ToArray();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entries = await db.LlmWikiEntries
            .Include(x => x.Sources)
            .Where(x => x.OwnerUserName == owner && matchedIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (entries.Count == 0)
        {
            return [];
        }

        var now = DateTime.UtcNow;
        foreach (var entry in entries)
        {
            if (entry.IsPublic)
            {
                entry.PublishedAt ??= now;
                continue;
            }

            entry.IsPublic = true;
            entry.PublishedAt = now;
            var source = CreateSourceRecord(
                entry.Id,
                owner,
                "publish",
                authorizationText,
                $"Matched explicit public-share query: {searchText}",
                entry.Title,
                string.Join(", ", DeserializeTags(entry.TagsJson)),
                entry.CategoryPath,
                now);
            source.Entry = entry;
            db.LlmWikiEntrySources.Add(source);
        }

        await db.SaveChangesAsync(cancellationToken);

        return entries
            .OrderBy(x => matchOrder[x.Id])
            .Select(entry => ToEntryResponse(entry))
            .ToList();
    }

    public Task<IReadOnlyList<LlmWikiSearchResult>> SearchAsync(
        string ownerUserName,
        string? query,
        int limit = 20,
        int offset = 0,
        int minRelevancePercent = 50,
        string? categoryPath = null,
        CancellationToken cancellationToken = default)
        => SearchCoreAsync(ownerUserName, query, limit, offset, minRelevancePercent, categoryPath, publicOnly: false, cancellationToken);

    public Task<IReadOnlyList<LlmWikiSearchResult>> SearchPublicAsync(
        string ownerUserName,
        string? query,
        int limit = 20,
        int offset = 0,
        int minRelevancePercent = 50,
        string? categoryPath = null,
        CancellationToken cancellationToken = default)
        => SearchCoreAsync(ownerUserName, query, limit, offset, minRelevancePercent, categoryPath, publicOnly: true, cancellationToken);

    private async Task<IReadOnlyList<LlmWikiSearchResult>> SearchCoreAsync(
        string ownerUserName,
        string? query,
        int limit,
        int offset,
        int minRelevancePercent,
        string? categoryPath,
        bool publicOnly,
        CancellationToken cancellationToken)
    {
        var owner = NormalizeUser(ownerUserName);
        var safeLimit = NormalizeLimit(limit, 20, 100);
        var safeOffset = Math.Clamp(offset, 0, 10_000);
        var safeMinRelevancePercent = Math.Clamp(minRelevancePercent, 0, 100);
        var normalizedCategoryPath = NormalizeCategoryFilter(categoryPath);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerEntries = db.LlmWikiEntries
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner);
        if (publicOnly)
        {
            ownerEntries = ownerEntries.Where(x => x.IsPublic);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            var recentQuery = FilterByCategory(
                ownerEntries,
                normalizedCategoryPath);
            var orderedRecentQuery = publicOnly
                ? recentQuery.OrderByDescending(x => x.PublishedAt ?? x.UpdatedAt)
                : recentQuery.OrderByDescending(x => x.UpdatedAt);
            var recentEntries = await orderedRecentQuery
                .Skip(safeOffset)
                .Take(safeLimit)
                .Select(x => new LlmWikiSearchProjection(
                    x.Id,
                    x.Slug,
                    x.Title,
                    x.Summary,
                    x.TagsJson,
                    x.CategoryPath,
                    x.CategoryDepth,
                    x.UpdatedAt,
                    x.AccessCount,
                    x.IsPublic,
                    x.PublishedAt))
                .ToListAsync(cancellationToken);

            return recentEntries.Select(entry => ToSearchResult(entry)).ToList();
        }

        var searchText = TrimToLength(query, 400);
        await EnsureOwnerSearchIndexAsync(db, owner, publicOnly, cancellationToken);
        var rankedEntries = await SearchGraphAsync(
            db,
            owner,
            searchText,
            safeLimit,
            safeOffset,
            safeMinRelevancePercent,
            normalizedCategoryPath,
            publicOnly,
            cancellationToken);
        if (rankedEntries.Count == 0)
        {
            return [];
        }

        var rankedIds = rankedEntries.Select(x => x.Id).ToArray();
        var rankedQuery = db.LlmWikiEntries
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner && rankedIds.Contains(x.Id));
        if (publicOnly)
        {
            rankedQuery = rankedQuery.Where(x => x.IsPublic);
        }

        var entries = await rankedQuery
            .Select(x => new LlmWikiSearchProjection(
                x.Id,
                x.Slug,
                x.Title,
                x.Summary,
                x.TagsJson,
                x.CategoryPath,
                x.CategoryDepth,
                x.UpdatedAt,
                x.AccessCount,
                x.IsPublic,
                x.PublishedAt))
            .ToListAsync(cancellationToken);
        var entryById = entries.ToDictionary(x => x.Id);
        var relevanceById = rankedEntries.ToDictionary(x => x.Id, x => x.RelevancePercent);

        return rankedIds
            .Where(entryById.ContainsKey)
            .Select(id => ToSearchResult(entryById[id], relevanceById[id]))
            .ToList();
    }

    public Task<IReadOnlyDictionary<Guid, LlmWikiEntryResponse>> GetEntriesAsync(
        string ownerUserName,
        IReadOnlyCollection<Guid> entryIds,
        bool recordAccess = false,
        CancellationToken cancellationToken = default)
        => GetEntriesCoreAsync(ownerUserName, entryIds, recordAccess, publicOnly: false, includeSources: true, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, LlmWikiEntryResponse>> GetPublicEntriesAsync(
        string ownerUserName,
        IReadOnlyCollection<Guid> entryIds,
        bool recordAccess = false,
        CancellationToken cancellationToken = default)
        => GetEntriesCoreAsync(ownerUserName, entryIds, recordAccess, publicOnly: true, includeSources: false, cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, LlmWikiEntryResponse>> GetEntriesCoreAsync(
        string ownerUserName,
        IReadOnlyCollection<Guid> entryIds,
        bool recordAccess,
        bool publicOnly,
        bool includeSources,
        CancellationToken cancellationToken)
    {
        var owner = NormalizeUser(ownerUserName);
        var ids = entryIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();
        if (string.IsNullOrWhiteSpace(owner) || ids.Length == 0)
        {
            return new Dictionary<Guid, LlmWikiEntryResponse>();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<LlmWikiEntryRecord> query = db.LlmWikiEntries
            .Include(x => x.Sources)
            .Where(x => x.OwnerUserName == owner && ids.Contains(x.Id));
        if (publicOnly)
        {
            query = query.Where(x => x.IsPublic);
        }

        var entries = recordAccess
            ? await query.ToListAsync(cancellationToken)
            : await query.AsNoTracking().ToListAsync(cancellationToken);

        if (recordAccess && entries.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var entry in entries)
            {
                entry.AccessCount++;
                entry.LastAccessedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return entries.ToDictionary(x => x.Id, entry => ToEntryResponse(entry, includeSources));
    }

    public Task<IReadOnlyList<LlmWikiCategorySummary>> GetCategoriesAsync(
        string ownerUserName,
        CancellationToken cancellationToken = default)
        => GetCategoriesCoreAsync(ownerUserName, publicOnly: false, cancellationToken);

    public Task<IReadOnlyList<LlmWikiCategorySummary>> GetPublicCategoriesAsync(
        string ownerUserName,
        CancellationToken cancellationToken = default)
        => GetCategoriesCoreAsync(ownerUserName, publicOnly: true, cancellationToken);

    private async Task<IReadOnlyList<LlmWikiCategorySummary>> GetCategoriesCoreAsync(
        string ownerUserName,
        bool publicOnly,
        CancellationToken cancellationToken)
    {
        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.LlmWikiEntries
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner);
        if (publicOnly)
        {
            query = query.Where(x => x.IsPublic);
        }

        var entries = await query
            .Select(x => new { x.CategoryPath, x.UpdatedAt })
            .ToListAsync(cancellationToken);

        var categoryMap = new Dictionary<string, LlmWikiCategoryAccumulator>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var segments = GetCategorySegments(NormalizeCategoryFilter(entry.CategoryPath));
            if (segments.Count == 0)
            {
                segments = ["general"];
            }

            for (var i = 1; i <= segments.Count; i++)
            {
                var path = string.Join("/", segments.Take(i));
                if (!categoryMap.TryGetValue(path, out var category))
                {
                    category = new LlmWikiCategoryAccumulator(path, i);
                    categoryMap[path] = category;
                }

                category.Count++;
                if (entry.UpdatedAt > category.UpdatedAt)
                {
                    category.UpdatedAt = entry.UpdatedAt;
                }
            }
        }

        return categoryMap.Values
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(x => new LlmWikiCategorySummary(x.Path, x.Depth, x.Count, x.UpdatedAt))
            .ToList();
    }

    public Task<LlmWikiEntryResponse?> GetEntryAsync(
        string ownerUserName,
        string idOrSlug,
        CancellationToken cancellationToken = default)
        => GetEntryCoreAsync(ownerUserName, idOrSlug, publicOnly: false, includeSources: true, cancellationToken);

    public Task<LlmWikiEntryResponse?> GetPublicEntryAsync(
        string ownerUserName,
        string idOrSlug,
        CancellationToken cancellationToken = default)
        => GetEntryCoreAsync(ownerUserName, idOrSlug, publicOnly: true, includeSources: false, cancellationToken);

    private async Task<LlmWikiEntryResponse?> GetEntryCoreAsync(
        string ownerUserName,
        string idOrSlug,
        bool publicOnly,
        bool includeSources,
        CancellationToken cancellationToken)
    {
        var owner = NormalizeUser(ownerUserName);
        var key = idOrSlug.Trim();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var entry = await FindEntryAsync(db, owner, key, cancellationToken);

        if (entry is null || (publicOnly && !entry.IsPublic))
        {
            return null;
        }

        entry.AccessCount++;
        entry.LastAccessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return ToEntryResponse(entry, includeSources);
    }

    public async Task<string> BuildLlmsTextAsync(
        string ownerUserName,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var entries = await SearchAsync(ownerUserName, null, NormalizeLimit(limit, 50, 200), cancellationToken: cancellationToken);
        var categories = await GetCategoriesAsync(ownerUserName, cancellationToken);
        var owner = NormalizeUser(ownerUserName);
        var provenanceByEntryId = await LoadProvenanceSummariesAsync(owner, entries.Select(x => x.Id).ToArray(), cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine($"# {owner} LLM Wiki");
        builder.AppendLine();
        builder.AppendLine("> User-scoped Slogs LLM Wiki index for prompt memories, preferences, and implementation decisions.");
        builder.AppendLine();
        builder.AppendLine("## Agent Workflow");
        builder.AppendLine();
        builder.AppendLine($"- Korean Slogs MCP prompt: {SlogsMcpPolicyPrompt.DefaultKoreanPublicUrl}");
        builder.AppendLine($"- English Slogs MCP prompt: {SlogsMcpPolicyPrompt.DefaultEnglishPublicUrl}");
        builder.AppendLine($"- Prompt version URL: {SlogsMcpPolicyPrompt.DefaultVersionUrl}");
        builder.AppendLine($"- Slogs MCP endpoint: {SlogsMcpPolicyPrompt.DefaultMcpUrl}");
        builder.AppendLine("- When installing the copied prompt, ask for the Slogs MCP key first, then ask whether to configure the policy and MCP connection globally, for the current project, or only for the current session.");
        builder.AppendLine("- Apply the prompt and MCP connection to the same selected scope unless the user explicitly chooses otherwise.");
        builder.AppendLine("- Never write the MCP token value to prompt files, docs, logs, responses, or LLM Wiki; use the Agent's secure secret or environment mechanism when available.");
        builder.AppendLine("- Call `llm_wiki_instructions` once after connecting.");
        builder.AppendLine("- Before creating memory, call `llm_wiki_capture` or `llm_wiki_find_related`.");
        builder.AppendLine("- If a related entry exists, read it and use `llm_wiki_merge` or `llm_wiki_update` with the agent-composed final wording.");
        builder.AppendLine("- Keep raw provenance verifiable: remember, merge, and update requests are preserved as Raw Provenance so retrieval quality and merge quality can be audited later.");
        builder.AppendLine("- LLM Wiki entries are private by default; use `llm_wiki_make_public` only after the owner explicitly asks to disclose a specific topic.");
        builder.AppendLine("- Use `llm_wiki_public_list`, `llm_wiki_public_search`, `llm_wiki_public_read`, or `llm_wiki_public_recall` to access another user's public entries; public tools never return Raw Provenance.");
        builder.AppendLine("- Treat `@username` mentions in user questions as Slogs user handles. For questions such as `@dimohy의 신앙관` or `@dimohy의 공개된 LLM Wiki 목록`, call the matching public lookup tool with `ownerUserName` set to that handle and the remaining topic words as the query.");
        builder.AppendLine("- If public lookup tools return no results for an `@username` question, say that no public Slogs LLM Wiki information was found for that topic.");
        builder.AppendLine("- Entries returned by public lookup tools are owner-authorized public self-disclosures. If a public result includes sensitive topics such as religion or faith perspective, answer only from that public result and scope the answer as public Slogs LLM Wiki information.");
        builder.AppendLine("- Do not infer sensitive information that public tools did not return, and do not substitute private recall/search results for another user's public disclosure.");
        builder.AppendLine("- Before remember, merge, or update, choose an explicit `categoryPath` such as `project/domain/topic` when the project or topic is known.");
        builder.AppendLine("- Use `llm_wiki_remember` only for genuinely new durable tacit knowledge.");
        builder.AppendLine("- Before finishing meaningful work, quietly check whether the turn produced tacit knowledge that future LLMs can document, automate, reproduce, or use for decisions.");
        builder.AppendLine("- Store corrected terminology, judgment criteria, repeatable workflows, operating rules, verified root causes, restart points, hidden prerequisites, and runbook-worthy command flows.");
        builder.AppendLine("- Do not store sensitive information, one-time logs, temporary execution traces, unverified speculation, simple facts recoverable from current files, or turn-only intermediate state.");
        builder.AppendLine("- Use `llm_wiki_recall` when the user asks to continue, remember, or apply previous context.");
        builder.AppendLine();
        builder.AppendLine("## Category Policy");
        builder.AppendLine();
        builder.AppendLine("- `categoryPath` is the primary structure; tags are secondary labels.");
        builder.AppendLine("- Prefer 2-4 slash-separated segments such as `slogs/llm-wiki/graphrag`, `slogs/deployment/wasm-aot`, or `preference/coding-policy/slogs`.");
        builder.AppendLine("- Do not use vague paths such as `general`, `misc`, or `notes` when the project/domain/topic is known.");
        builder.AppendLine();
        builder.AppendLine("## Categories");
        builder.AppendLine();

        if (categories.Count == 0)
        {
            builder.AppendLine("- No categories yet.");
        }
        else
        {
            foreach (var category in categories)
            {
                builder.AppendLine($"- {category.CategoryPath}: {category.Count} entries, depth {category.CategoryDepth}");
            }
        }

        builder.AppendLine();

        if (entries.Count == 0)
        {
            builder.AppendLine("## Recent Entries");
            builder.AppendLine();
            builder.AppendLine("- No entries yet.");
            return builder.ToString();
        }

        foreach (var group in entries.GroupBy(x => x.CategoryPath).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"## {group.Key}");
            builder.AppendLine();
            foreach (var entry in group)
            {
                var tags = entry.Tags.Count == 0 ? string.Empty : $" Tags: {string.Join(", ", entry.Tags)}.";
                var provenance = provenanceByEntryId.TryGetValue(entry.Id, out var provenanceSummary)
                    ? $" Provenance: {provenanceSummary}."
                    : " Provenance: none.";
                builder.AppendLine($"- [{entry.Title}](llm-wiki://entries/{entry.Id}): {entry.Summary}{tags}{provenance}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    public async Task RecordMcpAuditAsync(
        string ownerUserName,
        LlmWikiMcpAuditRequest request,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var toolName = TrimToLength(CleanAuditInlineText(request.ToolName), 80);
        var responseMode = TrimToLength(CleanAuditInlineText(request.ResponseMode), 80);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(toolName))
        {
            throw new InvalidOperationException("LLM Wiki MCP 감사 기록에는 로그인 사용자와 도구 이름이 필요합니다.");
        }

        var normalizedQuery = NormalizeAuditQuery(request.Query);
        var queryPreview = TrimToLength(CleanAuditInlineText(normalizedQuery), MaxAuditInlineLength);
        var queryHash = string.IsNullOrWhiteSpace(normalizedQuery) ? string.Empty : ComputeAuditHash(normalizedQuery);
        var categoryPath = TrimToLength(CleanAuditInlineText(request.CategoryPath), MaxCategoryPathLength);
        var resultIds = request.ResultIds
            .Where(x => x != Guid.Empty)
            .Take(MaxAuditResultIds)
            .Select(x => x.ToString())
            .ToArray();
        var resultIdsJson = JsonSerializer.Serialize(resultIds, GetJsonTypeInfo<string[]>());

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            INSERT INTO "LlmWikiMcpAudits"
                ("Id", "OwnerUserName", "ToolName", "ResponseMode", "QueryHash", "QueryPreview",
                 "CategoryPath", "RequestedLimit", "EffectiveLimit", "MinRelevancePercent",
                 "ResultCount", "ResultIdsJson", "ElapsedMs", "ResponseChars", "Succeeded", "CreatedAt")
            VALUES
                (@id, @owner, @toolName, @responseMode, @queryHash, @queryPreview,
                 @categoryPath, @requestedLimit, @effectiveLimit, @minRelevancePercent,
                 @resultCount, @resultIdsJson, @elapsedMs, @responseChars, @succeeded, @createdAt);
            """;
        command.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("toolName", toolName));
        command.Parameters.Add(new NpgsqlParameter("responseMode", responseMode));
        command.Parameters.Add(new NpgsqlParameter("queryHash", queryHash));
        command.Parameters.Add(new NpgsqlParameter("queryPreview", queryPreview));
        command.Parameters.Add(new NpgsqlParameter("categoryPath", categoryPath));
        command.Parameters.Add(new NpgsqlParameter("requestedLimit", request.RequestedLimit is int requestedLimit ? requestedLimit : (object)DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("effectiveLimit", request.EffectiveLimit is int effectiveLimit ? effectiveLimit : (object)DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("minRelevancePercent", request.MinRelevancePercent is int minRelevancePercent ? minRelevancePercent : (object)DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("resultCount", Math.Max(0, request.ResultCount)));
        command.Parameters.Add(new NpgsqlParameter("resultIdsJson", NpgsqlDbType.Jsonb) { Value = resultIdsJson });
        command.Parameters.Add(new NpgsqlParameter("elapsedMs", Math.Clamp(request.ElapsedMs, 0, int.MaxValue)));
        command.Parameters.Add(new NpgsqlParameter("responseChars", Math.Clamp(request.ResponseChars, 0, int.MaxValue)));
        command.Parameters.Add(new NpgsqlParameter("succeeded", request.Succeeded));
        command.Parameters.Add(new NpgsqlParameter("createdAt", DateTime.UtcNow));

        await EnsureConnectionOpenAsync(db, cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> LoadProvenanceSummariesAsync(
        string owner,
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var sources = await db.LlmWikiEntrySources
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner && entryIds.Contains(x.EntryId))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.EntryId, x.Action })
            .ToListAsync(cancellationToken);

        return sources
            .GroupBy(x => x.EntryId)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var actions = x
                        .Select(source => source.Action)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    return $"{x.Count()} raw record{(x.Count() == 1 ? string.Empty : "s")} ({string.Join(", ", actions)})";
                });
    }

    public async Task<IReadOnlyList<LlmWikiTokenResponse>> GetTokensAsync(
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tokens = await db.LlmWikiMcpTokens
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return tokens
            .Select(x => new LlmWikiTokenResponse(
                x.Id,
                x.Name,
                x.TokenPrefix,
                DeserializeTokenScopes(x.ScopesJson),
                x.CreatedAt,
                x.LastUsedAt,
                x.RevokedAt is not null))
            .ToList();
    }

    public async Task<LlmWikiTokenCreatedResponse> CreateTokenAsync(
        string ownerUserName,
        string? name,
        IReadOnlyList<string>? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var tokenScopes = NormalizeTokenScopes(scopes);
        var displayName = string.IsNullOrWhiteSpace(name)
            ? $"Slogs {FormatTokenScopesForName(tokenScopes)} token {DateTime.UtcNow:yyyy-MM-dd HH:mm}"
            : TrimToLength(name, 80);
        var token = $"slogs_mcp_{CreateTokenSecret()}";
        var now = DateTime.UtcNow;
        var record = new LlmWikiMcpTokenRecord
        {
            Id = Guid.NewGuid(),
            OwnerUserName = owner,
            Name = displayName,
            TokenHash = HashToken(token),
            TokenPrefix = token[..Math.Min(token.Length, 18)],
            ScopesJson = SerializeTokenScopes(tokenScopes),
            CreatedAt = now
        };

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.LlmWikiMcpTokens.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        return new LlmWikiTokenCreatedResponse(record.Id, record.Name, record.TokenPrefix, token, tokenScopes, record.CreatedAt);
    }

    public async Task<bool> RevokeTokenAsync(
        string ownerUserName,
        Guid tokenId,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var token = await db.LlmWikiMcpTokens
            .FirstOrDefaultAsync(x => x.OwnerUserName == owner && x.Id == tokenId, cancellationToken);
        if (token is null)
        {
            return false;
        }

        token.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AuthUser?> AuthenticateMcpTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var result = await AuthenticateBearerTokenAsync(token, SlogsTokenScopes.Mcp, cancellationToken);
        return result.User;
    }

    public async Task<SlogsBearerTokenAuthenticationResult> AuthenticateBearerTokenAsync(
        string token,
        string requiredScope,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return SlogsBearerTokenAuthenticationResult.Invalid;
        }

        var tokenHash = HashToken(token.Trim());
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tokenRecord = await db.LlmWikiMcpTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.RevokedAt == null, cancellationToken);
        if (tokenRecord is null)
        {
            return SlogsBearerTokenAuthenticationResult.Invalid;
        }

        var scopes = DeserializeTokenScopes(tokenRecord.ScopesJson);
        if (!HasTokenScope(scopes, requiredScope))
        {
            return SlogsBearerTokenAuthenticationResult.Forbidden;
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserName == tokenRecord.OwnerUserName, cancellationToken);
        if (user is null)
        {
            return SlogsBearerTokenAuthenticationResult.Invalid;
        }

        tokenRecord.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var authUser = new AuthUser
        {
            UserName = user.UserName,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName,
            Email = user.Email,
            ProfileImageUrl = user.ProfileImageUrl,
            Bio = user.Bio,
            RegisteredAt = user.RegisteredAt
        };
        return SlogsBearerTokenAuthenticationResult.Success(authUser);
    }

    public static string FormatEntryMarkdown(LlmWikiEntryResponse entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {entry.Title}");
        builder.AppendLine();
        builder.AppendLine(entry.Summary);
        builder.AppendLine();
        builder.AppendLine($"- id: {entry.Id}");
        builder.AppendLine($"- slug: {entry.Slug}");
        builder.AppendLine($"- updated: {entry.UpdatedAt:O}");
        builder.AppendLine($"- visibility: {(entry.IsPublic ? "public" : "private")}");
        if (entry.PublishedAt is not null)
        {
            builder.AppendLine($"- publishedAt: {entry.PublishedAt:O}");
        }

        builder.AppendLine($"- category: {entry.CategoryPath}");
        builder.AppendLine($"- categoryDepth: {entry.CategoryDepth}");
        if (entry.Tags.Count > 0)
        {
            builder.AppendLine($"- tags: {string.Join(", ", entry.Tags)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Source Prompt");
        builder.AppendLine();
        builder.AppendLine(entry.SourcePrompt);

        if (!string.IsNullOrWhiteSpace(entry.Content))
        {
            builder.AppendLine();
            builder.AppendLine("## Content");
            builder.AppendLine();
            builder.AppendLine(entry.Content);
        }

        if (entry.Sources.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Raw Provenance");
            builder.AppendLine();
            var index = 1;
            foreach (var source in entry.Sources.OrderBy(x => x.CreatedAt))
            {
                builder.AppendLine($"### {index}. {source.Action} at {source.CreatedAt:O}");
                builder.AppendLine();
                if (!string.IsNullOrWhiteSpace(source.Title))
                {
                    builder.AppendLine($"- title: {source.Title}");
                }

                if (!string.IsNullOrWhiteSpace(source.Tags))
                {
                    builder.AppendLine($"- tags: {source.Tags}");
                }

                if (!string.IsNullOrWhiteSpace(source.CategoryPath))
                {
                    builder.AppendLine($"- categoryPath: {source.CategoryPath}");
                }

                builder.AppendLine();
                builder.AppendLine("#### Raw Prompt");
                builder.AppendLine();
                builder.AppendLine(source.Prompt);
                if (source.Content is not null)
                {
                    builder.AppendLine();
                    builder.AppendLine("#### Raw Content");
                    builder.AppendLine();
                    builder.AppendLine(source.Content);
                }

                builder.AppendLine();
                index++;
            }
        }

        return builder.ToString();
    }

    public static string FormatPublicEntryMarkdown(string ownerUserName, LlmWikiEntryResponse entry)
    {
        var owner = NormalizeUser(ownerUserName);
        var builder = new StringBuilder();
        builder.AppendLine($"# @{owner} Public LLM Wiki: {entry.Title}");
        builder.AppendLine();
        builder.AppendLine("This entry is an owner-authorized public self-disclosure. If it includes sensitive topics such as religion or faith perspective, answer only from this public entry and say it comes from the user's public Slogs LLM Wiki.");
        builder.AppendLine();
        builder.AppendLine(entry.Summary);
        builder.AppendLine();
        builder.AppendLine($"- owner: @{owner}");
        builder.AppendLine($"- id: {entry.Id}");
        builder.AppendLine($"- slug: {entry.Slug}");
        builder.AppendLine($"- updated: {entry.UpdatedAt:O}");
        builder.AppendLine("- visibility: public");
        if (entry.PublishedAt is not null)
        {
            builder.AppendLine($"- publishedAt: {entry.PublishedAt:O}");
        }

        builder.AppendLine($"- category: {entry.CategoryPath}");
        builder.AppendLine($"- categoryDepth: {entry.CategoryDepth}");
        if (entry.Tags.Count > 0)
        {
            builder.AppendLine($"- tags: {string.Join(", ", entry.Tags)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Source Prompt");
        builder.AppendLine();
        builder.AppendLine(entry.SourcePrompt);

        if (!string.IsNullOrWhiteSpace(entry.Content))
        {
            builder.AppendLine();
            builder.AppendLine("## Content");
            builder.AppendLine();
            builder.AppendLine(entry.Content);
        }

        return builder.ToString();
    }

    public static string FormatSearchResultsMarkdown(IReadOnlyList<LlmWikiSearchResult> results)
    {
        if (results.Count == 0)
        {
            return "No matching LLM Wiki entries.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("# LLM Wiki Search Results");
        builder.AppendLine();

        foreach (var result in results)
        {
            var tags = result.Tags.Count == 0 ? string.Empty : $" Tags: {string.Join(", ", result.Tags)}.";
            var relevance = result.RelevancePercent is null ? string.Empty : $" ({result.RelevancePercent}% relevance)";
            var visibility = result.IsPublic ? " public" : string.Empty;
            builder.AppendLine($"- `{result.Id}` [{result.CategoryPath}]{visibility} {result.Title}{relevance}: {result.Summary}{tags}");
        }

        return builder.ToString();
    }

    private static LlmWikiEntryResponse ToEntryResponse(LlmWikiEntryRecord entry, bool includeSources = true)
        => new(
            entry.Id,
            entry.Slug,
            entry.Title,
            entry.Summary,
            entry.Content,
            entry.SourcePrompt,
            DeserializeTags(entry.TagsJson),
            entry.CategoryPath,
            entry.CategoryDepth,
            entry.CreatedAt,
            entry.UpdatedAt,
            entry.LastAccessedAt,
            entry.AccessCount,
            entry.IsPublic,
            entry.PublishedAt,
            includeSources
                ? entry.Sources
                    .OrderBy(x => x.CreatedAt)
                    .Select(ToSourceResponse)
                    .ToList()
                : []);

    private static LlmWikiSourceResponse ToSourceResponse(LlmWikiEntrySourceRecord source)
        => new(
            source.Id,
            source.Action,
            source.Prompt,
            source.Content,
            source.Title,
            source.Tags,
            source.CategoryPath,
            source.CreatedAt);

    private static LlmWikiSearchResult ToSearchResult(LlmWikiEntryRecord entry, int? relevancePercent = null)
        => new(
            entry.Id,
            entry.Slug,
            entry.Title,
            entry.Summary,
            DeserializeTags(entry.TagsJson),
            entry.CategoryPath,
            entry.CategoryDepth,
            entry.UpdatedAt,
            entry.AccessCount,
            entry.IsPublic,
            entry.PublishedAt,
            relevancePercent);

    private static LlmWikiSearchResult ToSearchResult(LlmWikiSearchProjection entry, int? relevancePercent = null)
        => new(
            entry.Id,
            entry.Slug,
            entry.Title,
            entry.Summary,
            DeserializeTags(entry.TagsJson),
            entry.CategoryPath,
            entry.CategoryDepth,
            entry.UpdatedAt,
            entry.AccessCount,
            entry.IsPublic,
            entry.PublishedAt,
            relevancePercent);

    private async Task EnsureOwnerSearchIndexAsync(
        SlogsDbContext db,
        string owner,
        bool publicOnly,
        CancellationToken cancellationToken)
    {
        var staleEntryIds = await LoadEntryIdsRequiringSearchIndexAsync(
            db,
            owner,
            MaxSearchIndexRefreshPerQuery,
            publicOnly,
            cancellationToken);
        if (staleEntryIds.Count == 0)
        {
            return;
        }

        var entriesQuery = db.LlmWikiEntries
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner && staleEntryIds.Contains(x.Id));
        if (publicOnly)
        {
            entriesQuery = entriesQuery.Where(x => x.IsPublic);
        }

        var entries = await entriesQuery.ToListAsync(cancellationToken);
        if (entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            var tags = DeserializeTags(entry.TagsJson);
            var categoryPath = NormalizeCategoryPath(entry.CategoryPath, tags);
            var embeddingDocument = BuildEmbeddingDocument(entry.Title, entry.SourcePrompt, entry.Content, tags, categoryPath);
            var contentHash = ComputeSearchContentHash(embeddingDocument);
            var embedding = await embeddingService.EmbedDocumentAsync(embeddingDocument, cancellationToken);
            var graphNodes = BuildGraphNodes(entry.Title, entry.SourcePrompt, entry.Content, tags, categoryPath);
            await StoreEntrySearchIndexAsync(db, entry.Id, owner, contentHash, embedding, graphNodes, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<Guid>> LoadEntryIdsRequiringSearchIndexAsync(
        SlogsDbContext db,
        string owner,
        int limit,
        bool publicOnly,
        CancellationToken cancellationToken)
    {
        var safeLimit = NormalizeLimit(limit, MaxSearchIndexRefreshPerQuery, MaxSearchIndexRefreshPerQuery);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT e."Id"
            FROM "LlmWikiEntries" AS e
            LEFT JOIN "LlmWikiEntryEmbeddings" AS idx
                ON idx."EntryId" = e."Id"
            WHERE e."OwnerUserName" = @owner
              AND (@publicOnly = FALSE OR e."IsPublic" = TRUE)
              AND (
                  idx."EntryId" IS NULL
                  OR idx."OwnerUserName" <> @owner
                  OR idx."Model" <> @model
                  OR idx."Dimensions" <> @dimensions
                  OR idx."IndexVersion" <> @indexVersion
                  OR idx."UpdatedAt" < e."UpdatedAt"
              )
            ORDER BY
                CASE
                    WHEN idx."EntryId" IS NULL THEN 0
                    WHEN idx."UpdatedAt" < e."UpdatedAt" THEN 1
                    ELSE 2
                END,
                e."UpdatedAt" DESC
            LIMIT @limit;
            """;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("publicOnly", publicOnly));
        command.Parameters.Add(new NpgsqlParameter("model", embeddingService.Model));
        command.Parameters.Add(new NpgsqlParameter("dimensions", embeddingService.Dimensions));
        command.Parameters.Add(new NpgsqlParameter("indexVersion", SearchIndexVersion));
        command.Parameters.Add(new NpgsqlParameter("limit", safeLimit));

        await EnsureConnectionOpenAsync(db, cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<Guid>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetGuid(0));
        }

        return results;
    }

    private async Task StoreEntrySearchIndexAsync(
        SlogsDbContext db,
        Guid entryId,
        string owner,
        string contentHash,
        IReadOnlyList<float> embedding,
        IReadOnlyList<LlmWikiGraphNode> graphNodes,
        CancellationToken cancellationToken)
    {
        var vectorLiteral = ToVectorLiteral(embedding);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO "LlmWikiEntryEmbeddings"
                    ("EntryId", "OwnerUserName", "Model", "Dimensions", "ContentHash", "IndexVersion", "Embedding", "UpdatedAt")
                VALUES
                    (@entryId, @owner, @model, @dimensions, @contentHash, @indexVersion, CAST(@embedding AS vector), @updatedAt)
                ON CONFLICT ("EntryId") DO UPDATE SET
                    "OwnerUserName" = EXCLUDED."OwnerUserName",
                    "Model" = EXCLUDED."Model",
                    "Dimensions" = EXCLUDED."Dimensions",
                    "ContentHash" = EXCLUDED."ContentHash",
                    "IndexVersion" = EXCLUDED."IndexVersion",
                    "Embedding" = EXCLUDED."Embedding",
                    "UpdatedAt" = EXCLUDED."UpdatedAt";

                DELETE FROM "LlmWikiEntryGraphNodes"
                WHERE "EntryId" = @entryId;
                """;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new NpgsqlParameter("entryId", entryId));
            command.Parameters.Add(new NpgsqlParameter("owner", owner));
            command.Parameters.Add(new NpgsqlParameter("model", embeddingService.Model));
            command.Parameters.Add(new NpgsqlParameter("dimensions", embeddingService.Dimensions));
            command.Parameters.Add(new NpgsqlParameter("contentHash", contentHash));
            command.Parameters.Add(new NpgsqlParameter("indexVersion", SearchIndexVersion));
            command.Parameters.Add(new NpgsqlParameter("embedding", vectorLiteral));
            command.Parameters.Add(new NpgsqlParameter("updatedAt", DateTime.UtcNow));

            await EnsureConnectionOpenAsync(db, cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (graphNodes.Count == 0)
        {
            return;
        }

        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO "LlmWikiEntryGraphNodes"
                    ("EntryId", "OwnerUserName", "NodeKey", "NodeText", "NodeType", "Weight")
                SELECT @entryId, @owner, nodes."NodeKey", nodes."NodeText", nodes."NodeType", nodes."Weight"
                FROM unnest(@nodeKeys, @nodeTexts, @nodeTypes, @nodeWeights)
                    AS nodes("NodeKey", "NodeText", "NodeType", "Weight");
                """;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new NpgsqlParameter("entryId", entryId));
            command.Parameters.Add(new NpgsqlParameter("owner", owner));
            command.Parameters.Add(new NpgsqlParameter("nodeKeys", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = graphNodes.Select(x => x.Key).ToArray()
            });
            command.Parameters.Add(new NpgsqlParameter("nodeTexts", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = graphNodes.Select(x => x.Text).ToArray()
            });
            command.Parameters.Add(new NpgsqlParameter("nodeTypes", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = graphNodes.Select(x => x.Type).ToArray()
            });
            command.Parameters.Add(new NpgsqlParameter("nodeWeights", NpgsqlDbType.Array | NpgsqlDbType.Double)
            {
                Value = graphNodes.Select(x => x.Weight).ToArray()
            });

            await EnsureConnectionOpenAsync(db, cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<LlmWikiRankedEntry>> SearchGraphAsync(
        SlogsDbContext db,
        string owner,
        string searchText,
        int limit,
        int offset,
        int minRelevancePercent,
        string categoryPath,
        bool publicOnly,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = await embeddingService.EmbedQueryAsync(searchText, cancellationToken);
        var queryNodeKeys = BuildQueryGraphNodeKeys(searchText, categoryPath);
        var queryVector = ToVectorLiteral(queryEmbedding);
        var categoryPrefix = string.IsNullOrWhiteSpace(categoryPath) ? string.Empty : $"{categoryPath}/%";
        var seedLimit = Math.Max((offset + limit) * 10, 100);

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            WITH filtered_entries AS (
                SELECT "Id", "UpdatedAt"
                FROM "LlmWikiEntries"
                WHERE "OwnerUserName" = @owner
                  AND (@publicOnly = FALSE OR "IsPublic" = TRUE)
                  AND (
                      @categoryPath = ''
                      OR "CategoryPath" = @categoryPath
                      OR "CategoryPath" LIKE @categoryPrefix
                  )
            ),
            vector_seed AS (
                SELECT
                    e."Id",
                    1 - (idx."Embedding" <=> CAST(@queryVector AS vector)) AS vector_score
                FROM filtered_entries AS e
                INNER JOIN "LlmWikiEntryEmbeddings" AS idx
                    ON idx."EntryId" = e."Id"
                WHERE idx."OwnerUserName" = @owner
                  AND idx."Model" = @model
                  AND idx."Dimensions" = @dimensions
                ORDER BY idx."Embedding" <=> CAST(@queryVector AS vector)
                LIMIT @seedLimit
            ),
            query_graph AS (
                SELECT
                    nodes."EntryId" AS "Id",
                    SUM(nodes."Weight") AS graph_score
                FROM "LlmWikiEntryGraphNodes" AS nodes
                INNER JOIN filtered_entries AS e
                    ON e."Id" = nodes."EntryId"
                WHERE nodes."OwnerUserName" = @owner
                  AND nodes."NodeKey" = ANY(@queryNodeKeys)
                GROUP BY nodes."EntryId"
            ),
            lexical_match AS (
                SELECT
                    nodes."EntryId" AS "Id",
                    LEAST(
                        SUM(
                            CASE nodes."NodeType"
                                WHEN 'title-phrase' THEN 0.85
                                WHEN 'title-term' THEN 0.55
                                WHEN 'tag' THEN 0.45
                                WHEN 'category-path' THEN 0.36
                                WHEN 'category-term' THEN 0.30
                                WHEN 'prompt-phrase' THEN 0.28
                                WHEN 'prompt-term' THEN 0.22
                                WHEN 'content-phrase' THEN 0.18
                                WHEN 'content-term' THEN 0.14
                                ELSE 0.0
                            END
                        ),
                        1.15
                    ) AS lexical_score
                FROM "LlmWikiEntryGraphNodes" AS nodes
                INNER JOIN filtered_entries AS e
                    ON e."Id" = nodes."EntryId"
                WHERE nodes."OwnerUserName" = @owner
                  AND nodes."NodeKey" = ANY(@queryNodeKeys)
                GROUP BY nodes."EntryId"
            ),
            seed_graph_nodes AS (
                SELECT nodes."NodeKey"
                FROM "LlmWikiEntryGraphNodes" AS nodes
                INNER JOIN vector_seed AS seed
                    ON seed."Id" = nodes."EntryId"
                WHERE nodes."OwnerUserName" = @owner
                GROUP BY nodes."NodeKey"
                ORDER BY MAX(seed.vector_score) DESC, SUM(nodes."Weight") DESC, nodes."NodeKey"
                LIMIT 200
            ),
            expanded_graph AS (
                SELECT
                    nodes."EntryId" AS "Id",
                    SUM(nodes."Weight") * 0.25 AS graph_score
                FROM "LlmWikiEntryGraphNodes" AS nodes
                INNER JOIN seed_graph_nodes AS seed_nodes
                    ON seed_nodes."NodeKey" = nodes."NodeKey"
                INNER JOIN filtered_entries AS e
                    ON e."Id" = nodes."EntryId"
                WHERE nodes."OwnerUserName" = @owner
                GROUP BY nodes."EntryId"
            ),
            combined AS (
                SELECT
                    "Id",
                    vector_score,
                    0::double precision AS query_graph_score,
                    0::double precision AS expanded_graph_score,
                    0::double precision AS lexical_score
                FROM vector_seed
                UNION ALL
                SELECT
                    "Id",
                    0::double precision AS vector_score,
                    graph_score AS query_graph_score,
                    0::double precision AS expanded_graph_score,
                    0::double precision AS lexical_score
                FROM query_graph
                UNION ALL
                SELECT
                    "Id",
                    0::double precision AS vector_score,
                    0::double precision AS query_graph_score,
                    0::double precision AS expanded_graph_score,
                    lexical_score
                FROM lexical_match
                UNION ALL
                SELECT
                    "Id",
                    0::double precision AS vector_score,
                    0::double precision AS query_graph_score,
                    graph_score AS expanded_graph_score,
                    0::double precision AS lexical_score
                FROM expanded_graph
            ),
            ranked AS (
                SELECT
                    "Id",
                    MAX(vector_score) AS vector_score,
                    SUM(query_graph_score) AS query_graph_score,
                    SUM(expanded_graph_score) AS expanded_graph_score,
                    SUM(lexical_score) AS lexical_score,
                    MAX(vector_score) * 0.90
                        + LEAST(SUM(query_graph_score), 16) / 18.0
                        + LEAST(SUM(expanded_graph_score), 10) / 90.0
                        + SUM(lexical_score) AS rank_score
                FROM combined
                GROUP BY "Id"
            ),
            scored AS (
                SELECT
                    ranked."Id",
                    ranked.rank_score,
                    ROUND(LEAST(GREATEST(ranked.rank_score / 1.60, 0), 1) * 100)::integer AS relevance_percent
                FROM ranked
            )
            SELECT scored."Id", scored.relevance_percent
            FROM scored
            INNER JOIN filtered_entries AS e
                ON e."Id" = scored."Id"
            WHERE scored.relevance_percent >= @minRelevancePercent
            ORDER BY scored.rank_score DESC, e."UpdatedAt" DESC
            OFFSET @offset
            LIMIT @limit;
            """;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("publicOnly", publicOnly));
        command.Parameters.Add(new NpgsqlParameter("model", embeddingService.Model));
        command.Parameters.Add(new NpgsqlParameter("dimensions", embeddingService.Dimensions));
        command.Parameters.Add(new NpgsqlParameter("queryVector", queryVector));
        command.Parameters.Add(new NpgsqlParameter("categoryPath", categoryPath));
        command.Parameters.Add(new NpgsqlParameter("categoryPrefix", categoryPrefix));
        command.Parameters.Add(new NpgsqlParameter("queryNodeKeys", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = queryNodeKeys
        });
        command.Parameters.Add(new NpgsqlParameter("seedLimit", seedLimit));
        command.Parameters.Add(new NpgsqlParameter("offset", offset));
        command.Parameters.Add(new NpgsqlParameter("limit", limit));
        command.Parameters.Add(new NpgsqlParameter("minRelevancePercent", minRelevancePercent));

        await EnsureConnectionOpenAsync(db, cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<LlmWikiRankedEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new LlmWikiRankedEntry(reader.GetGuid(0), reader.GetInt32(1)));
        }

        return entries;
    }

    private static async Task EnsureConnectionOpenAsync(SlogsDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }
    }

    private static string BuildEmbeddingDocument(
        string title,
        string prompt,
        string content,
        IReadOnlyList<string> tags,
        string categoryPath)
    {
        var tagText = tags.Count == 0 ? "none" : string.Join(", ", tags);
        var text = string.Join(
            Environment.NewLine,
            new[]
            {
                $"title: {CleanInlineText(title)} | text:",
                $"category: {categoryPath}",
                $"tags: {tagText}",
                CleanInlineText(prompt),
                CleanInlineText(content)
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return TrimToLength(text, MaxEmbeddingContentLength);
    }

    private static string ComputeSearchContentHash(string embeddingDocument)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{SearchIndexVersion}\n{embeddingDocument}"))).ToLowerInvariant();

    private static IQueryable<LlmWikiEntryRecord> FilterByCategory(
        IQueryable<LlmWikiEntryRecord> query,
        string categoryPath)
    {
        if (string.IsNullOrWhiteSpace(categoryPath))
        {
            return query;
        }

        var categoryPrefix = $"{categoryPath}/";
        return query.Where(x => x.CategoryPath == categoryPath || x.CategoryPath.StartsWith(categoryPrefix));
    }

    private static string NormalizeCategoryPath(string? value, IReadOnlyList<string> tags)
    {
        var explicitSegments = ParseCategorySegments(value);
        if (explicitSegments.Count > 0)
        {
            return TrimToLength(string.Join("/", explicitSegments), MaxCategoryPathLength);
        }

        var tagSegments = tags
            .Select(CleanCategorySegment)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        return tagSegments.Length == 0
            ? "general"
            : TrimToLength(string.Join("/", tagSegments), MaxCategoryPathLength);
    }

    private static string NormalizeCategoryFilter(string? value)
    {
        var segments = ParseCategorySegments(value);
        return segments.Count == 0
            ? string.Empty
            : TrimToLength(string.Join("/", segments), MaxCategoryPathLength);
    }

    private static IReadOnlyList<string> ParseCategorySegments(string? value)
        => (value ?? string.Empty)
            .Split(['/', '\\', '>', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanCategorySegment)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(6)
            .ToList();

    private static IReadOnlyList<string> GetCategorySegments(string categoryPath)
        => categoryPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static int GetCategoryDepth(string categoryPath)
        => Math.Max(1, GetCategorySegments(categoryPath).Count);

    private static string CleanCategorySegment(string value)
    {
        var builder = new StringBuilder();
        var previousDash = false;
        foreach (var character in value.Normalize(NormalizationForm.FormKC).Trim().TrimStart('#').ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-')
            {
                builder.Append(character);
                previousDash = character == '-';
                continue;
            }

            if (char.IsWhiteSpace(character) && !previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return TrimToLength(builder.ToString().Trim('-'), MaxCategorySegmentLength);
    }

    private static IReadOnlyList<LlmWikiGraphNode> BuildGraphNodes(
        string title,
        string prompt,
        string content,
        IReadOnlyList<string> tags,
        string categoryPath)
    {
        var nodes = new Dictionary<string, LlmWikiGraphNode>(StringComparer.Ordinal);

        var categorySegments = GetCategorySegments(categoryPath);
        for (var i = 0; i < categorySegments.Count; i++)
        {
            AddGraphNode(nodes, "category-term", categorySegments[i], categorySegments[i], 3.5);
            var prefix = string.Join(" ", categorySegments.Take(i + 1));
            AddGraphNode(nodes, "category-path", prefix, string.Join("/", categorySegments.Take(i + 1)), 4.5);
        }

        foreach (var tag in tags)
        {
            AddGraphNode(nodes, "tag", NormalizeGraphToken(tag), tag, 4.0);
        }

        var titleTokens = ExtractGraphTokens(title).Take(20).ToArray();
        AddGraphPhrases(nodes, titleTokens, "title-phrase", 4.0);
        foreach (var token in titleTokens)
        {
            AddGraphNode(nodes, "title-term", token, token, 3.0);
        }

        var promptTokens = ExtractGraphTokens(prompt).Take(56).ToArray();
        AddGraphPhrases(nodes, promptTokens.Take(20).ToArray(), "prompt-phrase", 2.2);
        foreach (var token in promptTokens)
        {
            AddGraphNode(nodes, "prompt-term", token, token, 1.6);
        }

        var contentTokens = ExtractGraphTokens(content).Take(56).ToArray();
        AddGraphPhrases(nodes, contentTokens.Take(20).ToArray(), "content-phrase", 1.4);
        foreach (var token in contentTokens)
        {
            AddGraphNode(nodes, "content-term", token, token, 1.0);
        }

        return nodes.Values
            .OrderByDescending(x => x.Weight)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Take(MaxGraphNodesPerEntry)
            .ToList();
    }

    private static void AddGraphPhrases(
        Dictionary<string, LlmWikiGraphNode> nodes,
        IReadOnlyList<string> tokens,
        string type,
        double weight)
    {
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            AddGraphNode(nodes, type, $"{tokens[i]} {tokens[i + 1]}", $"{tokens[i]} {tokens[i + 1]}", weight);
        }
    }

    private static string[] BuildQueryGraphNodeKeys(string searchText, string categoryPath)
    {
        var queryTokens = ExtractGraphTokens(searchText)
            .Distinct(StringComparer.Ordinal)
            .Take(24)
            .ToArray();
        var keys = BuildGraphNodes(searchText, searchText, searchText, queryTokens, categoryPath)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var token in queryTokens)
        {
            keys.Add($"category-term:{token}");
        }

        for (var i = 0; i < queryTokens.Length - 1; i++)
        {
            keys.Add($"category-path:{queryTokens[i]} {queryTokens[i + 1]}");
        }

        return keys.ToArray();
    }

    private static void AddGraphNode(
        Dictionary<string, LlmWikiGraphNode> nodes,
        string type,
        string token,
        string text,
        double weight)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var normalizedToken = TrimToLength(token, MaxGraphNodeLength);
        var key = $"{type}:{normalizedToken}";
        if (nodes.TryGetValue(key, out var existing))
        {
            nodes[key] = existing with { Weight = Math.Max(existing.Weight, weight) };
            return;
        }

        nodes[key] = new LlmWikiGraphNode(
            key,
            TrimToLength(text, MaxGraphNodeLength),
            type,
            weight);
    }

    private static IEnumerable<string> ExtractGraphTokens(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                continue;
            }

            foreach (var token in FlushGraphToken(builder))
            {
                yield return token;
            }
        }

        foreach (var token in FlushGraphToken(builder))
        {
            yield return token;
        }
    }

    private static IEnumerable<string> FlushGraphToken(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            yield break;
        }

        var token = NormalizeGraphToken(builder.ToString());
        builder.Clear();

        if (token.Length >= 2)
        {
            yield return token;
        }
    }

    private static string NormalizeGraphToken(string value)
    {
        var token = value.Normalize(NormalizationForm.FormKC).Trim().TrimStart('#').ToLowerInvariant();
        foreach (var suffix in KoreanParticleSuffixes)
        {
            if (token.Length > suffix.Length + 1
                && token.EndsWith(suffix, StringComparison.Ordinal)
                && ShouldStripKoreanParticleSuffix(token, suffix))
            {
                token = token[..^suffix.Length];
                break;
            }
        }

        return TrimToLength(token, MaxGraphNodeLength);
    }

    private static bool ShouldStripKoreanParticleSuffix(string token, string suffix)
    {
        var stemLastCharacterIndex = token.Length - suffix.Length - 1;
        if (stemLastCharacterIndex < 0)
        {
            return false;
        }

        var stemLastCharacter = token[stemLastCharacterIndex];
        return suffix switch
        {
            "이" or "은" or "을" or "과" => HasHangulFinalConsonant(stemLastCharacter),
            "가" or "는" or "를" or "와" => !HasHangulFinalConsonant(stemLastCharacter),
            _ => true
        };
    }

    private static bool HasHangulFinalConsonant(char character)
    {
        const int hangulSyllableStart = 0xAC00;
        const int hangulSyllableEnd = 0xD7A3;
        if (character < hangulSyllableStart || character > hangulSyllableEnd)
        {
            return false;
        }

        return ((character - hangulSyllableStart) % 28) != 0;
    }

    private static string ToVectorLiteral(IReadOnlyList<float> values)
    {
        var builder = new StringBuilder(values.Count * 12);
        builder.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(values[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static async Task<string> CreateUniqueSlugAsync(
        SlogsDbContext db,
        string owner,
        string title,
        CancellationToken cancellationToken)
    {
        var baseSlug = CreateSlug(title);
        var slug = baseSlug;
        var suffix = 2;

        while (await db.LlmWikiEntries.AnyAsync(
            x => x.OwnerUserName == owner && x.Slug == slug,
            cancellationToken))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private static async Task<LlmWikiEntryRecord?> FindEntryAsync(
        SlogsDbContext db,
        string owner,
        string idOrSlug,
        CancellationToken cancellationToken)
    {
        var key = idOrSlug.Trim();
        if (Guid.TryParse(key, out var id))
        {
            return await db.LlmWikiEntries
                .Include(x => x.Sources)
                .FirstOrDefaultAsync(x => x.OwnerUserName == owner && x.Id == id, cancellationToken);
        }

        return await db.LlmWikiEntries
            .Include(x => x.Sources)
            .FirstOrDefaultAsync(x => x.OwnerUserName == owner && x.Slug == key, cancellationToken);
    }

    private static LlmWikiEntrySourceRecord CreateSourceRecord(
        Guid entryId,
        string owner,
        string action,
        string prompt,
        string? content,
        string? title,
        string? tags,
        string? categoryPath,
        DateTime createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            EntryId = entryId,
            OwnerUserName = owner,
            Action = NormalizeSourceAction(action),
            Prompt = TrimToLength(prompt, MaxPromptLength),
            Content = content is null ? null : TrimToLength(content, MaxContentLength),
            Title = title is null ? null : TrimToLength(title, MaxTitleLength),
            Tags = tags is null ? null : TrimToLength(tags, MaxRawMetadataLength),
            CategoryPath = categoryPath is null ? null : TrimToLength(categoryPath, MaxCategoryPathLength),
            CreatedAt = createdAt
        };

    private static string NormalizeSourceAction(string action)
        => action switch
        {
            "remember" => "remember",
            "update" => "update",
            "merge" => "merge",
            "publish" => "publish",
            "legacy-baseline" => "legacy-baseline",
            _ => throw new InvalidOperationException($"Unsupported LLM Wiki source action: {action}")
        };

    private static string CreateSlug(string value)
    {
        var builder = new StringBuilder();
        var previousDash = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousDash = false;
                continue;
            }

            if (!previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = $"wiki-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        return slug.Length <= 140 ? slug : slug[..140].Trim('-');
    }

    private static string DeriveTitle(string? explicitTitle, string prompt, string content)
    {
        var source = string.IsNullOrWhiteSpace(explicitTitle)
            ? FirstNonEmptyLine(prompt) ?? FirstNonEmptyLine(content) ?? "LLM Wiki entry"
            : explicitTitle;
        return TrimToLength(CleanInlineText(source), MaxTitleLength);
    }

    private static string DeriveSummary(string content, string prompt)
    {
        var source = FirstNonEmptyLine(content) ?? FirstNonEmptyLine(prompt) ?? "Prompt memory";
        return TrimToLength(CleanInlineText(source), MaxSummaryLength);
    }

    private static string? FirstNonEmptyLine(string? value)
        => value?
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string CleanInlineText(string value)
    {
        var cleaned = value
            .Replace("#", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Trim();

        return string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static IReadOnlyList<string> ParseTags(string? rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
        {
            return [];
        }

        char[] separators = rawTags.Contains(',')
            || rawTags.Contains(';')
            || rawTags.Contains('\n')
            ? [',', ';', '\r', '\n']
            : [' ', '\t'];

        return rawTags
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().TrimStart('#').ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static string SerializeTags(IReadOnlyList<string> tags)
        => JsonSerializer.Serialize(tags.ToArray(), GetJsonTypeInfo<string[]>());

    private static IReadOnlyList<string> DeserializeTags(string tagsJson)
        => JsonSerializer.Deserialize(tagsJson, GetJsonTypeInfo<string[]>()) ?? [];

    private static IReadOnlyList<string> NormalizeTokenScopes(IReadOnlyList<string>? scopes)
    {
        var normalized = (scopes is null || scopes.Count == 0
                ? SlogsTokenScopes.DefaultMcpScopes
                : scopes)
            .Select(scope => scope.Trim().ToLowerInvariant())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
        {
            normalized = [SlogsTokenScopes.Mcp];
        }

        foreach (var scope in normalized)
        {
            if (!AllowedTokenScopes.Contains(scope, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("slogsTokenScopeInvalid");
            }
        }

        return normalized;
    }

    private static IReadOnlyList<string> DeserializeTokenScopes(string? scopesJson)
    {
        if (string.IsNullOrWhiteSpace(scopesJson))
        {
            return SlogsTokenScopes.DefaultMcpScopes;
        }

        try
        {
            return NormalizeTokenScopes(JsonSerializer.Deserialize(scopesJson, GetJsonTypeInfo<string[]>()) ?? []);
        }
        catch (JsonException)
        {
            return SlogsTokenScopes.DefaultMcpScopes;
        }
    }

    private static string SerializeTokenScopes(IReadOnlyList<string> scopes)
        => JsonSerializer.Serialize(NormalizeTokenScopes(scopes).ToArray(), GetJsonTypeInfo<string[]>());

    private static bool HasTokenScope(IReadOnlyList<string> scopes, string requiredScope)
        => scopes.Contains(requiredScope.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    private static string FormatTokenScopesForName(IReadOnlyList<string> scopes)
        => scopes.Contains(SlogsTokenScopes.ObsidianSync, StringComparer.Ordinal)
            ? "Obsidian sync"
            : "MCP";

    private static JsonTypeInfo<T> GetJsonTypeInfo<T>()
        => (JsonTypeInfo<T>?)SlogsJsonSerializerContext.Default.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"JSON metadata for {typeof(T).FullName} is not registered.");

    private static string CreateTokenSecret()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(tokenBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string ComputeAuditHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeAuditQuery(string? value)
        => (value ?? string.Empty).ReplaceLineEndings("\n").Trim();

    private static string CleanAuditInlineText(string? value)
        => (value ?? string.Empty).ReplaceLineEndings(" ").Trim();

    private static string TrimToLength(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static int NormalizeLimit(int limit, int defaultValue, int maxValue)
        => Math.Clamp(limit <= 0 ? defaultValue : limit, 1, maxValue);

    private static string NormalizeUser(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimStart('@').ToLowerInvariant();

    private sealed record LlmWikiSearchProjection(
        Guid Id,
        string Slug,
        string Title,
        string Summary,
        string TagsJson,
        string CategoryPath,
        int CategoryDepth,
        DateTime UpdatedAt,
        int AccessCount,
        bool IsPublic,
        DateTime? PublishedAt);

    private sealed record LlmWikiGraphNode(string Key, string Text, string Type, double Weight);

    private sealed record LlmWikiRankedEntry(Guid Id, int RelevancePercent);

    private sealed class LlmWikiCategoryAccumulator(string path, int depth)
    {
        public string Path { get; } = path;

        public int Depth { get; } = depth;

        public int Count { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.MinValue;
    }
}

public sealed record SlogsBearerTokenAuthenticationResult(AuthUser? User, bool IsScopeAllowed)
{
    public static SlogsBearerTokenAuthenticationResult Invalid { get; } = new(null, true);

    public static SlogsBearerTokenAuthenticationResult Forbidden { get; } = new(null, false);

    public static SlogsBearerTokenAuthenticationResult Success(AuthUser user) => new(user, true);
}
