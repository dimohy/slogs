using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Slogs.Data;

public sealed partial class SlogsMcpPolicyPromptService(IDbContextFactory<SlogsDbContext> dbFactory)
{
    public async Task<SlogsMcpPolicyPromptSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT "Version", "KoreanMarkdown", "EnglishMarkdown", "UpdatedAt", "UpdatedBy"
            FROM "SlogsMcpPolicyPrompt" WHERE "Id" = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Slogs MCP 정책 프롬프트가 초기화되지 않았습니다.");
        }

        return new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4));
    }

    public async Task<SlogsMcpPolicyPromptSnapshot> UpdateAsync(
        string userName,
        string explicitRequest,
        string expectedVersion,
        string koreanMarkdown,
        string englishMarkdown,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(userName, "dimohy", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Slogs MCP 정책 프롬프트는 @dimohy만 수정할 수 있습니다.");
        }

        if (!IsExplicitPromptUpdateRequest(explicitRequest))
        {
            throw new InvalidOperationException("사용자가 Slogs LLM Wiki 프롬프트 수정을 명시적으로 요청한 원문이 필요합니다. 암시적 요청으로는 수정할 수 없습니다.");
        }

        ValidateMarkdown(koreanMarkdown, "한국어");
        ValidateMarkdown(englishMarkdown, "영어");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var readCommand = db.Database.GetDbConnection().CreateCommand();
        readCommand.Transaction = transaction.GetDbTransaction();
        readCommand.CommandText = "SELECT \"Version\" FROM \"SlogsMcpPolicyPrompt\" WHERE \"Id\" = 1 FOR UPDATE;";
        var currentVersion = (string?)await readCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Slogs MCP 정책 프롬프트가 초기화되지 않았습니다.");
        if (!string.Equals(currentVersion, expectedVersion.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"정책 프롬프트 버전이 {currentVersion}(으)로 변경되었습니다. 현재 프롬프트를 다시 읽고 교체본을 작성하세요.");
        }

        var nextVersion = NextVersion(currentVersion, DateTimeOffset.UtcNow);
        var versionedKorean = ReplaceVersion(koreanMarkdown, nextVersion);
        var versionedEnglish = ReplaceVersion(englishMarkdown, nextVersion);

        await using var updateCommand = db.Database.GetDbConnection().CreateCommand();
        updateCommand.Transaction = transaction.GetDbTransaction();
        updateCommand.CommandText = """
            UPDATE "SlogsMcpPolicyPrompt"
            SET "Version" = @version, "KoreanMarkdown" = @korean, "EnglishMarkdown" = @english,
                "UpdatedAt" = @updatedAt, "UpdatedBy" = @updatedBy
            WHERE "Id" = 1;
            """;
        AddParameter(updateCommand, "version", nextVersion);
        AddParameter(updateCommand, "korean", versionedKorean);
        AddParameter(updateCommand, "english", versionedEnglish);
        var updatedAt = DateTimeOffset.UtcNow;
        AddParameter(updateCommand, "updatedAt", updatedAt);
        AddParameter(updateCommand, "updatedBy", userName.ToLowerInvariant());
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(nextVersion, versionedKorean, versionedEnglish, updatedAt, userName.ToLowerInvariant());
    }

    public static bool IsExplicitPromptUpdateRequest(string request)
    {
        var value = request.Trim();
        var namesPolicyOrPrompt = value.Contains("slogs", StringComparison.OrdinalIgnoreCase)
            && value.Contains("llm wiki", StringComparison.OrdinalIgnoreCase)
            && (value.Contains("프롬프트", StringComparison.Ordinal)
                || value.Contains("prompt", StringComparison.OrdinalIgnoreCase)
                || value.Contains("정책", StringComparison.Ordinal)
                || value.Contains("policy", StringComparison.OrdinalIgnoreCase));
        var requestsChange = new[]
        {
            "수정", "변경", "업데이트", "고쳐", "반영", "적용",
            "modify", "update", "change", "apply", "reflect", "incorporate"
        }
            .Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
        return namesPolicyOrPrompt && requestsChange;
    }

    public static string NextVersion(string currentVersion, DateTimeOffset now)
    {
        var date = now.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy.MM.dd");
        var match = VersionRegex().Match(currentVersion);
        var revision = match.Success && match.Groups[1].Value == date
            ? int.Parse(match.Groups[2].Value) + 1
            : 1;
        return $"{date}.{revision}";
    }

    private static void ValidateMarkdown(string markdown, string language)
    {
        if (string.IsNullOrWhiteSpace(markdown) || markdown.Length > 100_000)
        {
            throw new InvalidOperationException($"{language} 정책 프롬프트는 비어 있지 않은 100,000자 이하 Markdown이어야 합니다.");
        }

        if (!markdown.Contains("# Slogs MCP / LLM Wiki Agent Prompt", StringComparison.Ordinal)
            || !PromptVersionRegex().IsMatch(markdown))
        {
            throw new InvalidOperationException($"{language} 정책 프롬프트의 제목 또는 Prompt Version 줄이 올바르지 않습니다.");
        }
    }

    private static string ReplaceVersion(string markdown, string version)
        => PromptVersionRegex().Replace(markdown.Trim() + "\n", $"Prompt Version: {version}", 1);

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    [GeneratedRegex("^(\\d{4}\\.\\d{2}\\.\\d{2})\\.(\\d+)$")]
    private static partial Regex VersionRegex();

    [GeneratedRegex("^Prompt Version: \\S+$", RegexOptions.Multiline)]
    private static partial Regex PromptVersionRegex();
}

public sealed record SlogsMcpPolicyPromptSnapshot(
    string Version,
    string KoreanMarkdown,
    string EnglishMarkdown,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);
