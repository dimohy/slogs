using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class LlmWikiService(IDbContextFactory<SlogsDbContext> dbFactory)
{
    private const int MaxPromptLength = 12_000;
    private const int MaxContentLength = 80_000;
    private const int MaxTitleLength = 120;
    private const int MaxSummaryLength = 240;

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
        var now = DateTime.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
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
            CreatedAt = now,
            UpdatedAt = now
        };

        db.LlmWikiEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return ToEntryResponse(entry);
    }

    public async Task<LlmWikiEntryResponse?> UpdateAsync(
        string ownerUserName,
        string idOrSlug,
        LlmWikiUpdateRequest request,
        CancellationToken cancellationToken = default)
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

        entry.Title = title;
        entry.Summary = DeriveSummary(content, prompt);
        entry.Content = content;
        entry.SourcePrompt = prompt;
        entry.TagsJson = SerializeTags(tags);
        entry.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return ToEntryResponse(entry);
    }

    public async Task<IReadOnlyList<LlmWikiSearchResult>> SearchAsync(
        string ownerUserName,
        string? query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var safeLimit = NormalizeLimit(limit, 20, 100);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(query))
        {
            var recentEntries = await db.LlmWikiEntries
                .AsNoTracking()
                .Where(x => x.OwnerUserName == owner)
                .OrderByDescending(x => x.UpdatedAt)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);

            return recentEntries.Select(ToSearchResult).ToList();
        }

        var searchText = TrimToLength(query, 200);
        var fullTextMatches = await db.LlmWikiEntries
            .FromSql(
                $"""
                SELECT
                    "Id", "OwnerUserName", "Slug", "Title", "Summary", "Content", "SourcePrompt",
                    "TagsJson", "CreatedAt", "UpdatedAt", "LastAccessedAt", "AccessCount"
                FROM "LlmWikiEntries"
                WHERE "OwnerUserName" = {owner}
                  AND "SearchVector" @@ websearch_to_tsquery('simple', {searchText})
                ORDER BY ts_rank_cd("SearchVector", websearch_to_tsquery('simple', {searchText})) DESC,
                         "UpdatedAt" DESC
                LIMIT {safeLimit}
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (fullTextMatches.Count > 0)
        {
            return fullTextMatches.Select(ToSearchResult).ToList();
        }

        var pattern = $"%{EscapeLikePattern(searchText)}%";
        var fallbackMatches = await db.LlmWikiEntries
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner
                && (EF.Functions.ILike(x.Title, pattern)
                    || EF.Functions.ILike(x.Summary, pattern)
                    || EF.Functions.ILike(x.SourcePrompt, pattern)
                    || EF.Functions.ILike(x.Content, pattern)))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);

        return fallbackMatches.Select(ToSearchResult).ToList();
    }

    public async Task<LlmWikiEntryResponse?> GetEntryAsync(
        string ownerUserName,
        string idOrSlug,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var key = idOrSlug.Trim();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var entry = await FindEntryAsync(db, owner, key, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        entry.AccessCount++;
        entry.LastAccessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return ToEntryResponse(entry);
    }

    public async Task<string> BuildLlmsTextAsync(
        string ownerUserName,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var entries = await SearchAsync(ownerUserName, null, NormalizeLimit(limit, 50, 200), cancellationToken);
        var owner = NormalizeUser(ownerUserName);
        var builder = new StringBuilder();
        builder.AppendLine($"# {owner} LLM Wiki");
        builder.AppendLine();
        builder.AppendLine("> User-scoped Slogs LLM Wiki index for prompt memories, preferences, and implementation decisions.");
        builder.AppendLine();
        builder.AppendLine("## Agent Workflow");
        builder.AppendLine();
        builder.AppendLine("- Call `llm_wiki_instructions` once after connecting.");
        builder.AppendLine("- Before creating memory, call `llm_wiki_capture` or `llm_wiki_find_related`.");
        builder.AppendLine("- If a related entry exists, read it and use `llm_wiki_merge` or `llm_wiki_update` with the agent-composed final wording.");
        builder.AppendLine("- Use `llm_wiki_remember` only for genuinely new durable knowledge.");
        builder.AppendLine("- Use `llm_wiki_recall` when the user asks to continue, remember, or apply previous context.");
        builder.AppendLine();
        builder.AppendLine("## Recent Entries");
        builder.AppendLine();

        if (entries.Count == 0)
        {
            builder.AppendLine("- No entries yet.");
            return builder.ToString();
        }

        foreach (var entry in entries)
        {
            var tags = entry.Tags.Count == 0 ? string.Empty : $" Tags: {string.Join(", ", entry.Tags)}.";
            builder.AppendLine($"- [{entry.Title}](llm-wiki://entries/{entry.Id}): {entry.Summary}{tags}");
        }

        return builder.ToString();
    }

    public async Task<IReadOnlyList<LlmWikiTokenResponse>> GetTokensAsync(
        string ownerUserName,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tokens = await db.LlmWikiMcpTokens
            .AsNoTracking()
            .Where(x => x.OwnerUserName == owner)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return tokens
            .Select(x => new LlmWikiTokenResponse(
                x.Id,
                x.Name,
                x.TokenPrefix,
                x.CreatedAt,
                x.LastUsedAt,
                x.RevokedAt is not null))
            .ToList();
    }

    public async Task<LlmWikiTokenCreatedResponse> CreateTokenAsync(
        string ownerUserName,
        string? name,
        CancellationToken cancellationToken = default)
    {
        var owner = NormalizeUser(ownerUserName);
        var displayName = string.IsNullOrWhiteSpace(name)
            ? $"Slogs MCP token {DateTime.UtcNow:yyyy-MM-dd HH:mm}"
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
            CreatedAt = now
        };

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.LlmWikiMcpTokens.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        return new LlmWikiTokenCreatedResponse(record.Id, record.Name, record.TokenPrefix, token, record.CreatedAt);
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
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = HashToken(token.Trim());
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tokenRecord = await db.LlmWikiMcpTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.RevokedAt == null, cancellationToken);
        if (tokenRecord is null)
        {
            return null;
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserName == tokenRecord.OwnerUserName, cancellationToken);
        if (user is null)
        {
            return null;
        }

        tokenRecord.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return new AuthUser
        {
            UserName = user.UserName,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName,
            Email = user.Email,
            ProfileImageUrl = user.ProfileImageUrl,
            Bio = user.Bio,
            RegisteredAt = user.RegisteredAt
        };
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
            builder.AppendLine($"- `{result.Id}` {result.Title}: {result.Summary}{tags}");
        }

        return builder.ToString();
    }

    private static LlmWikiEntryResponse ToEntryResponse(LlmWikiEntryRecord entry)
        => new(
            entry.Id,
            entry.Slug,
            entry.Title,
            entry.Summary,
            entry.Content,
            entry.SourcePrompt,
            DeserializeTags(entry.TagsJson),
            entry.CreatedAt,
            entry.UpdatedAt,
            entry.LastAccessedAt,
            entry.AccessCount);

    private static LlmWikiSearchResult ToSearchResult(LlmWikiEntryRecord entry)
        => new(
            entry.Id,
            entry.Slug,
            entry.Title,
            entry.Summary,
            DeserializeTags(entry.TagsJson),
            entry.UpdatedAt,
            entry.AccessCount);

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
                .FirstOrDefaultAsync(x => x.OwnerUserName == owner && x.Id == id, cancellationToken);
        }

        return await db.LlmWikiEntries
            .FirstOrDefaultAsync(x => x.OwnerUserName == owner && x.Slug == key, cancellationToken);
    }

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

    private static string EscapeLikePattern(string value)
        => value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    private static string TrimToLength(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static int NormalizeLimit(int limit, int defaultValue, int maxValue)
        => Math.Clamp(limit <= 0 ? defaultValue : limit, 1, maxValue);

    private static string NormalizeUser(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
