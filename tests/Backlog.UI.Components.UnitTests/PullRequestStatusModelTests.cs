using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The variant rules of the pull request status line
/// (<c>.devbook/domain/progress/requirements.md#pull-request-status</c>): checks pass only
/// when every one passed, fail once one failed, and run otherwise; approvals are met at
/// the required total; and the words a screen reader hears are the requirement's own.
/// </summary>
public sealed class PullRequestStatusModelTests
{
    private static PullRequestStatusModel Pr(PullRequestChecks? checks = null, PullRequestApprovals? approvals = null, string state = "open") =>
        new(1341, state, checks, approvals);

    /// <summary>Scenario "Every check passed".</summary>
    [Fact]
    public void Every_check_passed_reads_passed_and_14_of_14()
    {
        var pr = Pr(new PullRequestChecks(14, 0, 0));

        Assert.Equal(PullRequestChecksVariant.Passed, pr.ChecksVariant);
        Assert.Equal("14/14", pr.ChecksCount);
        Assert.Equal("14 of 14 checks passed", pr.ChecksSpoken);
    }

    /// <summary>Scenario "Checks still running".</summary>
    [Fact]
    public void Checks_still_running_read_running_and_9_of_14()
    {
        var pr = Pr(new PullRequestChecks(9, 0, 5));

        Assert.Equal(PullRequestChecksVariant.Running, pr.ChecksVariant);
        Assert.Equal("9/14", pr.ChecksCount);
        Assert.Equal("9 of 14 checks running", pr.ChecksSpoken);
    }

    /// <summary>Scenario "A failing check": one failure wins over checks still running.</summary>
    [Fact]
    public void One_failed_check_reads_failing_even_while_others_run()
    {
        var pr = Pr(new PullRequestChecks(2, 1, 11));

        Assert.Equal(PullRequestChecksVariant.Failing, pr.ChecksVariant);
        Assert.Equal("2/14", pr.ChecksCount);
        Assert.Equal("2 of 14 checks failing", pr.ChecksSpoken);
    }

    [Fact]
    public void No_checks_read_or_none_run_leaves_the_checks_mark_out()
    {
        Assert.Equal(PullRequestChecksVariant.None, Pr().ChecksVariant);
        Assert.Equal(PullRequestChecksVariant.None, Pr(new PullRequestChecks(0, 0, 0)).ChecksVariant);
        Assert.Equal(string.Empty, Pr(new PullRequestChecks(0, 0, 0)).ChecksCount);
    }

    /// <summary>Scenario "One of two approvals".</summary>
    [Fact]
    public void One_of_two_approvals_is_short()
    {
        var pr = Pr(approvals: new PullRequestApprovals(1, 2));

        Assert.Equal(PullRequestApprovalsVariant.Short, pr.ApprovalsVariant);
        Assert.Equal("1/2", pr.ApprovalsCount);
        Assert.Equal("1 of 2 approvals", pr.ApprovalsSpoken);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void Approvals_reaching_the_required_total_are_met(int received, int required) =>
        Assert.Equal(PullRequestApprovalsVariant.Met, Pr(approvals: new PullRequestApprovals(received, required)).ApprovalsVariant);

    /// <summary>Scenario "No rule asks for an approval": the received count stands alone.</summary>
    [Fact]
    public void No_required_total_shows_the_received_count_alone()
    {
        var pr = Pr(approvals: new PullRequestApprovals(1, null));

        Assert.Equal(PullRequestApprovalsVariant.NoneRequired, pr.ApprovalsVariant);
        Assert.Equal("1", pr.ApprovalsCount);
        Assert.Equal("1 approval, none required", pr.ApprovalsSpoken);
        Assert.Equal("0 approvals, none required", Pr(approvals: new PullRequestApprovals(0, null)).ApprovalsSpoken);
    }

    /// <summary>A protection rule asking for no review reads as no required total, never a
    /// green 0/0.</summary>
    [Fact]
    public void A_required_total_of_nought_is_no_required_total()
    {
        var pr = Pr(approvals: new PullRequestApprovals(0, 0));

        Assert.Equal(PullRequestApprovalsVariant.NoneRequired, pr.ApprovalsVariant);
        Assert.Equal("0", pr.ApprovalsCount);
    }

    /// <summary>A read that never asked the base branch's rules says how many approvals
    /// there are and nothing about what is required: no verdict, and never "none required".</summary>
    [Theory]
    [InlineData(1, "1 approval")]
    [InlineData(0, "0 approvals")]
    [InlineData(2, "2 approvals")]
    public void A_required_total_that_was_not_read_speaks_the_count_alone(int received, string spoken)
    {
        var pr = Pr(approvals: new PullRequestApprovals(received, null, RequiredRead: false));

        Assert.Equal(PullRequestApprovalsVariant.Received, pr.ApprovalsVariant);
        Assert.Equal(received.ToString(System.Globalization.CultureInfo.InvariantCulture), pr.ApprovalsCount);
        Assert.Equal(spoken, pr.ApprovalsSpoken);
    }

    [Fact]
    public void Approvals_not_read_leave_the_approvals_mark_out()
    {
        Assert.Equal(PullRequestApprovalsVariant.None, Pr().ApprovalsVariant);
        Assert.Equal(string.Empty, Pr().ApprovalsCount);
    }

    [Theory]
    [InlineData("merged", "merged", true)]
    [InlineData("Open", "open", false)]
    [InlineData("draft", "draft", false)]
    [InlineData("closed", "closed", false)]
    [InlineData("bogus", "unknown", false)]
    [InlineData("", "unknown", false)]
    public void The_state_is_one_of_the_gh_badge_slugs(string state, string slug, bool merged)
    {
        var pr = Pr(state: state);

        Assert.Equal(slug, pr.Slug);
        Assert.Equal(merged, pr.IsMerged);
    }
}
