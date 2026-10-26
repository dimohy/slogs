using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;

namespace Slogs.Data;

public sealed record SlogsProgressAxis(
    string Id, int? Completed, int? Total, decimal? Percentage,
    string EvidenceReference, string EvidenceState,
    int? AuthoritativeCompleted, int? AuthoritativeTotal, string? EvidenceReason = null);

public sealed record SlogsProgressReportInput(
    string Event, string[] DeclaredAxes, SlogsProgressAxis[] Axes,
    string[]? FailureClassifications, string CurrentStage, string NextVerification,
    bool ClaimsGoalComplete, string RequestedScope, string ActualPolicyAuthority,
    bool ClaimsPolicyChangeComplete, string? PolicyEvidenceReference, string? RenderedReportText = null,
    SlogsProgressOverall? Overall = null, SlogsProgressPresentationGroup[]? PresentationGroups = null);

public sealed record SlogsProgressPresentationGroup(
    string Id, string Label, string[] AxisIds, string Status,
    long? Completed, long? Total, decimal? Percentage, string RemainingWork, string? UnknownReason = null);

public sealed record SlogsProgressOverall(long? Completed, long? Total, decimal? Percentage, string? UnknownReason = null);

public sealed record SlogsProgressReportEvaluation(bool Accepted, IReadOnlyList<string> FailureIds);

/// <summary>Evaluates the structured report required by the authoritative LLM Wiki policy.
/// Evidence counts are asserted snapshots; callers must obtain them from the cited source.
/// This evaluator cannot authenticate external evidence or guarantee that submitted rendered text
/// is actually emitted by the calling Agent; it rejects evidence reports lacking matching display text.</summary>
public static class SlogsProgressReportContract
{
    public const string PolicyId = "slogs-progress-report-policy";
    public const int SchemaVersion = 2;
    public const int PercentageDecimalPlaces = 2;
    public static readonly IReadOnlyList<string> ReportEvents = Array.AsReadOnly(new[] { "start", "resume", "progress", "correction", "final" });
    public static readonly IReadOnlyList<string> Obligations = Array.AsReadOnly(new[] {
        "all-goal-axes", "grouped-visible-summary", "complete-disjoint-group-coverage", "authoritative-counts", "exact-percentages", "failure-classification",
        "current-stage", "next-verification", "unknown-denominator-no-estimate", "visible-report-matches-evidence" });
    public static string BuildPolicyContractMarkdown() => """
        ```json
        {
          "policyId": "slogs-progress-report-policy",
          "schemaVersion": 2,
          "reportEvents": ["start", "resume", "progress", "correction", "final"],
          "obligations": ["all-goal-axes", "grouped-visible-summary", "complete-disjoint-group-coverage", "authoritative-counts", "exact-percentages", "failure-classification", "current-stage", "next-verification", "unknown-denominator-no-estimate", "visible-report-matches-evidence"],
          "percentageDecimalPlaces": 2,
          "authority": "slogs-llm-wiki",
          "projectValidatorOrCodexHookIsPolicyCompletionEvidence": false
        }
        ```
        """;

    public static IReadOnlyList<string> ValidateMarkdown(string markdown)
    {
        var candidates = Regex.Matches(markdown ?? "", "```json\\s*([\\s\\S]*?)```", RegexOptions.CultureInvariant);
        var contracts = new List<JsonElement>();
        foreach (Match candidate in candidates)
        {
            try
            {
                using var document = JsonDocument.Parse(candidate.Groups[1].Value);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("policyId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == PolicyId)
                    contracts.Add(root.Clone());
            }
            catch (JsonException) { /* Unrelated JSON examples are not policy contracts. Missing policy is reported below. */ }
        }
        if (contracts.Count == 0) return ["progress.policy.contract.missing"];
        if (contracts.Count != 1) return ["progress.policy.contract.duplicate"];
        var contract = contracts[0];
        var errors = new List<string>();
        if (!contract.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != SchemaVersion)
            errors.Add("progress.policy.schema.invalid");
        if (!MatchesArray(contract, "reportEvents", ReportEvents)) errors.Add("progress.policy.events.invalid");
        if (!MatchesArray(contract, "obligations", Obligations)) errors.Add("progress.policy.obligations.invalid");
        if (!contract.TryGetProperty("percentageDecimalPlaces", out var decimals) || decimals.ValueKind != JsonValueKind.Number || !decimals.TryGetInt32(out var places) || places != PercentageDecimalPlaces)
            errors.Add("progress.policy.precision.invalid");
        if (!contract.TryGetProperty("authority", out var authority) || authority.ValueKind != JsonValueKind.String || authority.GetString() != "slogs-llm-wiki"
            || !contract.TryGetProperty("projectValidatorOrCodexHookIsPolicyCompletionEvidence", out var substitute) || substitute.ValueKind != JsonValueKind.False)
            errors.Add("progress.policy.authority.invalid");
        return errors;
    }

    private static bool MatchesArray(JsonElement root, string name, IReadOnlyList<string> expected) =>
        root.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
        && values.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)
        && values.EnumerateArray().Select(item => item.GetString()).SequenceEqual(expected);

    public static SlogsProgressReportEvaluation EvaluateJson(string reportJson)
    {
        try
        {
            var report = JsonSerializer.Deserialize(reportJson, SlogsProgressReportJsonContext.Default.SlogsProgressReportInput);
            return report is null ? new(false, ["progress.report.invalid"]) : Evaluate(report);
        }
        catch (JsonException) { return new(false, ["progress.report.invalid"]); }
    }

    public static string? RenderReportJson(string reportJson)
    {
        try
        {
            var report = JsonSerializer.Deserialize(reportJson, SlogsProgressReportJsonContext.Default.SlogsProgressReportInput);
            return report is null ? null : RenderReport(report);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Returns the exact report block that must be present in the submitted user response.</summary>
    public static string RenderReport(SlogsProgressReportInput report)
    {
        static string Cell(string? value) => (value ?? "").Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("|", "&#124;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        static string Count(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "미확인";
        var result = new StringBuilder("<!-- SLOGS_PROGRESS_REPORT:BEGIN -->\n\n| 작업 | 진행 상태 | 남은 일 |\n| --- | ---: | --- |\n");
        foreach (var group in report.PresentationGroups ?? [])
        {
            if (group is null) continue;
            var percentage = group.Percentage is { } value ? value.ToString("F2", CultureInfo.InvariantCulture) + "%" : "미확인";
            result.Append("| ").Append(Cell(group.Label)).Append(" | ").Append(Count(group.Completed)).Append('/').Append(Count(group.Total))
                .Append(" · ").Append(percentage);
            if (!string.IsNullOrWhiteSpace(group.UnknownReason)) result.Append(" — ").Append(Cell(group.UnknownReason));
            result.Append(" | ").Append(Cell(group.RemainingWork)).Append(" |\n");
        }
        var classifications = report.FailureClassifications is { Length: > 0 } values ? string.Join(", ", values) : "없음";
        var overall = report.Overall;
        result.Append("\n총 진행률: ").Append(overall?.Completed?.ToString(CultureInfo.InvariantCulture) ?? "미확인")
            .Append('/').Append(overall?.Total?.ToString(CultureInfo.InvariantCulture) ?? "미확인")
            .Append(" (").Append(overall?.Percentage is { } overallPercent ? overallPercent.ToString("F2", CultureInfo.InvariantCulture) + "%" : "미확인")
            .Append(')');
        if (!string.IsNullOrWhiteSpace(overall?.UnknownReason)) result.Append(" — ").Append(Cell(overall.UnknownReason));
        result.Append('\n');
        result.Append("\n실패 분류: ").Append(Cell(classifications)).Append("\n\n현재 단계: ").Append(Cell(report.CurrentStage))
            .Append("\n\n다음 검증: ").Append(Cell(report.NextVerification)).Append("\n\n<!-- SLOGS_PROGRESS_REPORT:END -->");
        return result.ToString();
    }

    public static SlogsProgressReportEvaluation Evaluate(SlogsProgressReportInput report)
    {
        var failures = new HashSet<string>(StringComparer.Ordinal);
        void Fail(string id) => failures.Add(id);
        if (!ReportEvents.Contains(report.Event)) Fail("progress.event.invalid");
        var declared = report.DeclaredAxes ?? [];
        var axes = report.Axes ?? [];
        if (declared.Length == 0 || declared.Any(string.IsNullOrWhiteSpace) || declared.Distinct(StringComparer.Ordinal).Count() != declared.Length)
            Fail("progress.axes.declaration.invalid");
        if (axes.Any(axis => axis is null) || axes.Where(axis => axis is not null).Select(axis => axis.Id).Distinct(StringComparer.Ordinal).Count() != axes.Length)
            Fail("progress.axis.duplicate");
        if (declared.Any(id => !axes.Any(axis => axis?.Id == id))) Fail("progress.axis.missing");
        if (axes.Any(axis => axis is not null && !declared.Contains(axis.Id, StringComparer.Ordinal))) Fail("progress.axis.undeclared");
        ValidateGroups(report, Fail);
        foreach (var axis in axes.Where(axis => axis is not null))
        {
            if (axis.Completed < 0 || axis.Total < 0 || axis.Total is { } total && axis.Completed > total
                || axis.AuthoritativeCompleted < 0 || axis.AuthoritativeTotal < 0 || axis.AuthoritativeTotal is { } sourceTotal && axis.AuthoritativeCompleted > sourceTotal)
                Fail("progress.counts.invalid");
            if (axis.Completed != axis.AuthoritativeCompleted || axis.Total != axis.AuthoritativeTotal) Fail("progress.counts.stale");
            if (axis.EvidenceState == "unverified")
            {
                if (axis.Completed is not null || axis.Total is not null || axis.Percentage is not null || axis.AuthoritativeCompleted is not null || axis.AuthoritativeTotal is not null)
                    Fail("progress.evidence.unverified-counts");
                if (string.IsNullOrWhiteSpace(axis.EvidenceReason)) Fail("progress.evidence.reason.missing");
                // Unknown evidence stays unknown during every report event.
                // Null counts, an explicit reason, and incomplete goal claims
                // are enforced independently; an event must not force estimates.
                continue;
            }
            if (axis.Completed is null || axis.AuthoritativeCompleted is null) Fail("progress.counts.missing");
            if (string.IsNullOrWhiteSpace(axis.EvidenceReference)) Fail("progress.evidence.missing");
            if (axis.EvidenceState is not ("confirmed" or "draft")) Fail("progress.evidence.state.invalid");
            if (axis.Completed > 0 && axis.EvidenceState != "confirmed") Fail("progress.evidence.unconfirmed");
            if (axis.Total is null)
            {
                if (axis.Percentage is not null) Fail("progress.percentage.unknown-denominator");
            }
            else if (axis.Total == 0)
            {
                if (axis.Percentage is not null) Fail("progress.percentage.zero-denominator");
            }
            else if (axis.Percentage is null) Fail("progress.percentage.missing");
            else if (axis.Completed is not null && axis.Percentage != decimal.Round(100m * axis.Completed.Value / axis.Total.Value, PercentageDecimalPlaces, MidpointRounding.AwayFromZero))
                Fail("progress.percentage.incorrect");
        }
        if (report.FailureClassifications is null || report.FailureClassifications.Any(string.IsNullOrWhiteSpace)) Fail("progress.failures.missing");
        if (string.IsNullOrWhiteSpace(report.CurrentStage)) Fail("progress.stage.missing");
        if (string.IsNullOrWhiteSpace(report.NextVerification)) Fail("progress.verification.missing");
        if (report.Overall is not { } overall) Fail("progress.overall.missing");
        else if (axes.Length == 0 || axes.Any(axis => axis is null || axis.Total is null || axis.Completed is null || axis.EvidenceState != "confirmed"))
        {
            if (overall.Completed is not null || overall.Total is not null || overall.Percentage is not null)
                Fail("progress.overall.estimated");
            if (string.IsNullOrWhiteSpace(overall.UnknownReason)) Fail("progress.overall.reason.missing");
        }
        else
        {
            var completedSum = axes.Sum(axis => (long)axis.Completed!.Value);
            var totalSum = axes.Sum(axis => (long)axis.Total!.Value);
            var percentage = totalSum > 0 ? Math.Round((decimal)completedSum * 100 / totalSum, PercentageDecimalPlaces, MidpointRounding.AwayFromZero) : (decimal?)null;
            if (overall.Completed != completedSum || overall.Total != totalSum || overall.Percentage != percentage)
                Fail("progress.overall.mismatch");
            if (!string.IsNullOrWhiteSpace(overall.UnknownReason)) Fail("progress.overall.reason.invalid");
        }
        if (string.IsNullOrWhiteSpace(report.RenderedReportText)) Fail("progress.display.missing");
        else if (!report.RenderedReportText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal)
            .Contains(RenderReport(report), StringComparison.Ordinal)) Fail("progress.display.mismatch");
        if (report.ClaimsGoalComplete && (declared.Length == 0 || axes.Length != declared.Length || axes.Any(axis => axis is null || axis.Total is null || axis.Completed != axis.Total || axis.EvidenceState != "confirmed")
            || report.FailureClassifications is null || report.FailureClassifications.Length > 0 || failures.Count > 0)) Fail("progress.completion.unproven");
        if (report.RequestedScope is not ("slogs-llm-wiki-policy" or "long-goal-report")) Fail("progress.scope.expanded");
        if (report.ActualPolicyAuthority != "slogs-llm-wiki") Fail("progress.policy.authority.invalid");
        if (report.ClaimsPolicyChangeComplete && report.RequestedScope != "slogs-llm-wiki-policy") Fail("progress.policy.scope.invalid");
        if (report.ClaimsPolicyChangeComplete && string.IsNullOrWhiteSpace(report.PolicyEvidenceReference)) Fail("progress.policy.evidence.missing");
        return new(failures.Count == 0, failures.Order(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateGroups(SlogsProgressReportInput report, Action<string> fail)
    {
        var groups = report.PresentationGroups ?? [];
        var axes = (report.Axes ?? []).Where(a => a is not null).ToArray();
        if (groups.Length == 0) { fail("progress.groups.missing"); return; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var coverage = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (group is null) { fail("progress.group.invalid"); continue; }
            if (string.IsNullOrWhiteSpace(group.Id) || !ids.Add(group.Id)
                || string.IsNullOrWhiteSpace(group.Label) || string.IsNullOrWhiteSpace(group.RemainingWork)) fail("progress.group.invalid");
            var members = group.AxisIds ?? [];
            if (members.Length == 0) fail("progress.group.coverage.invalid");
            foreach (var id in members)
                if (string.IsNullOrWhiteSpace(id) || !coverage.Add(id) || !axes.Any(a => a.Id == id)) fail("progress.group.coverage.invalid");
            var selected = axes.Where(a => members.Contains(a.Id, StringComparer.Ordinal)).ToArray();
            var known = selected.Length > 0 && selected.All(a => a.Completed is not null && a.Total is not null && a.EvidenceState == "confirmed");
            if (!known)
            {
                if (group.Completed is not null || group.Total is not null || group.Percentage is not null) fail("progress.group.estimated");
                if (string.IsNullOrWhiteSpace(group.UnknownReason)) fail("progress.group.reason.missing");
                if (group.Status != "unknown") fail("progress.group.status.invalid");
                continue;
            }
            var completed = selected.Sum(a => (long)a.Completed!.Value);
            var total = selected.Sum(a => (long)a.Total!.Value);
            var percentage = total > 0 ? decimal.Round(100m * completed / total, PercentageDecimalPlaces, MidpointRounding.AwayFromZero) : (decimal?)null;
            if (group.Completed != completed || group.Total != total || group.Percentage != percentage) fail("progress.group.counts.mismatch");
            if (!string.IsNullOrWhiteSpace(group.UnknownReason)) fail("progress.group.reason.invalid");
            if (group.Status != (completed == total ? "completed" : "in-progress")) fail("progress.group.status.invalid");
        }
        if (axes.Any(a => !coverage.Contains(a.Id))) fail("progress.group.coverage.invalid");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SlogsProgressReportInput))]
internal partial class SlogsProgressReportJsonContext : JsonSerializerContext;
