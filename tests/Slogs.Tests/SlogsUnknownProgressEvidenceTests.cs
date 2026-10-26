using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class SlogsUnknownProgressEvidenceTests
{
    private static SlogsProgressReportInput Report(string reportEvent = "progress")
    {
        var report = new SlogsProgressReportInput(reportEvent, ["known", "unverified"],
            [new("known", 1, 2, 50, "verified-ledger", "confirmed", 1, 2),
             new("unverified", null, null, null, "pending-ledger", "unverified", null, null,
                 "Current scope and completion count have not been established.")],
            ["Unverified scope remains pending."], "Verify current scope", "Read authoritative evidence",
            false, "long-goal-report", "slogs-llm-wiki", false, null,
            Overall: new(null, null, null, "Unverified scope remains pending."),
            PresentationGroups:
            [new("known", "Known work", ["known"], "in-progress", 1, 2, 50, "Complete remaining work"),
             new("unverified", "Unverified work", ["unverified"], "unknown", null, null, null,
                 "Establish current scope", "Current counts are not available.")]);
        return Render(report);
    }

    private static SlogsProgressReportInput Render(SlogsProgressReportInput report) =>
        report with { RenderedReportText = SlogsProgressReportContract.RenderReport(report) };

    [Theory]
    [InlineData("start")]
    [InlineData("resume")]
    [InlineData("progress")]
    [InlineData("correction")]
    [InlineData("final")]
    public void AllReportEventsPreserveUnknownEvidenceWithoutInventingCounts(string reportEvent)
    {
        var report = Report(reportEvent);
        Assert.True(SlogsProgressReportContract.Evaluate(report).Accepted);
        Assert.Null(report.Axes[1].Completed);
        Assert.Null(report.Axes[1].AuthoritativeCompleted);
        Assert.Null(report.Overall!.Percentage);
        Assert.Contains("미확인/미확인", report.RenderedReportText);
    }

    [Fact]
    public void UnknownEvidenceStillRequiresAnExplanation()
    {
        var report = Report();
        report = Render(report with { Axes = [report.Axes[0], report.Axes[1] with { EvidenceReason = null }] });
        Assert.Contains("progress.evidence.reason.missing", SlogsProgressReportContract.Evaluate(report).FailureIds);
    }

    [Fact]
    public void UnknownEvidenceCannotBeReplacedWithZeroCounts()
    {
        var report = Report();
        report = Render(report with { Axes = [report.Axes[0], report.Axes[1] with
            { Completed = 0, Total = 0, AuthoritativeCompleted = 0, AuthoritativeTotal = 0 }] });
        Assert.Contains("progress.evidence.unverified-counts", SlogsProgressReportContract.Evaluate(report).FailureIds);
    }

    [Fact]
    public void UnknownEvidenceCannotSupportGoalCompletion()
    {
        var report = Render(Report("final") with { ClaimsGoalComplete = true });
        Assert.Contains("progress.completion.unproven", SlogsProgressReportContract.Evaluate(report).FailureIds);
    }

    [Fact]
    public void DisplayCannotOmitUnknownGroups()
    {
        var report = Report() with { RenderedReportText = "Only the completed work is shown." };
        Assert.Contains("progress.display.mismatch", SlogsProgressReportContract.Evaluate(report).FailureIds);
    }

    [Fact]
    public void UnknownEvidenceDoesNotExpandPolicyAuthority()
    {
        var report = Render(Report() with { RequestedScope = "slogs-llm-wiki-policy",
            ActualPolicyAuthority = "codex-hook", ClaimsPolicyChangeComplete = true,
            PolicyEvidenceReference = "local-hook" });
        Assert.Contains("progress.policy.authority.invalid", SlogsProgressReportContract.Evaluate(report).FailureIds);
    }
}
