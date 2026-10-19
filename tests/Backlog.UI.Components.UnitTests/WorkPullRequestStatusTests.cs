using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The pull request status line on a work-link card
/// (<c>.devbook/domain/progress/features.md#pull-request-status</c>): the card, the two
/// columns of <see cref="WorkLinks"/>, and the task panel's <see cref="WorkLinksPanel"/>.
/// </summary>
public sealed class WorkPullRequestStatusTests
{
    private static readonly PullRequestStatusModel Running = new(
        1214,
        "open",
        new PullRequestChecks(Passed: 9, Failed: 0, Pending: 5),
        new PullRequestApprovals(Received: 1, Required: 2));

    private static WorkPullRequest Pull(PullRequestStatusModel? status, IntegrationArtifactState state = IntegrationArtifactState.Open) =>
        new("JSdotNet/Backlog#1214", 1214, "Show the status line", state,
            IntegrationReviewState.ReviewRequired, IntegrationCheckState.Pending, ChecksText: "Checks running 9/14")
        {
            Status = status
        };

    [Fact]
    public void A_card_with_a_status_draws_both_marks_without_its_number_and_drops_the_checks_words()
    {
        using var context = new BunitContext();

        var card = context.Render<WorkPullRequestCard>(parameters => parameters.Add(p => p.PullRequest, Pull(Running)));

        var line = card.Find("[data-testid='work-pull-request-status']");
        Assert.Equal("9/14", line.QuerySelector("[data-mark='checks'] .pull-request-status__count")!.TextContent);
        Assert.Equal("running", line.QuerySelector("[data-mark='checks']")!.GetAttribute("data-variant"));
        Assert.Equal("1/2", line.QuerySelector("[data-mark='approvals'] .pull-request-status__count")!.TextContent);
        Assert.Equal("short", line.QuerySelector("[data-mark='approvals']")!.GetAttribute("data-variant"));
        Assert.Empty(card.FindAll("[data-testid='work-pull-request-status-number']"));

        Assert.Empty(card.FindAll("[data-testid='work-pull-request-checks']"));
        Assert.Equal("Review required", card.Find("[data-testid='work-pull-request-review']").TextContent);

        var title = card.Find("[data-testid='work-pull-request']").GetAttribute("title")!;
        Assert.Contains("9 of 14 checks running", title, StringComparison.Ordinal);
        Assert.Contains("1 of 2 approvals", title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_without_a_status_words_its_checks_as_before()
    {
        using var context = new BunitContext();

        var card = context.Render<WorkPullRequestCard>(parameters => parameters.Add(p => p.PullRequest, Pull(null)));

        Assert.Empty(card.FindAll("[data-testid='work-pull-request-status']"));
        Assert.Equal("Checks running 9/14", card.Find("[data-testid='work-pull-request-checks']").TextContent);
    }

    /// <summary>A merged pull request already says Merged; the line's "merged" would say it twice.</summary>
    [Fact]
    public void A_merged_card_draws_no_status_line()
    {
        using var context = new BunitContext();

        var card = context.Render<WorkPullRequestCard>(parameters => parameters
            .Add(p => p.PullRequest, Pull(Running with { State = "merged" }, IntegrationArtifactState.Merged)));

        Assert.Empty(card.FindAll("[data-testid='work-pull-request-status']"));
    }

    /// <summary>Approvals read against no rules say the count alone, with no verdict.</summary>
    [Fact]
    public void Approvals_whose_rules_were_not_read_stand_alone()
    {
        using var context = new BunitContext();

        var card = context.Render<WorkPullRequestCard>(parameters => parameters
            .Add(p => p.PullRequest, Pull(new PullRequestStatusModel(1214, "open", Approvals: new PullRequestApprovals(2, null, RequiredRead: false)))));

        var approvals = card.Find("[data-testid='work-pull-request-status-approvals']");
        Assert.Equal("received", approvals.GetAttribute("data-variant"));
        Assert.Equal("2", approvals.QuerySelector(".pull-request-status__count")!.TextContent);
        Assert.Empty(card.FindAll("[data-testid='work-pull-request-status-checks']"));
        Assert.Equal("Checks running 9/14", card.Find("[data-testid='work-pull-request-checks']").TextContent);
    }

    [Fact]
    public void Each_card_in_the_pull_requests_column_draws_its_own_line()
    {
        using var context = new BunitContext();

        var links = context.Render<WorkLinks>(parameters => parameters
            .Add(p => p.PullRequests,
            [
                Pull(Running),
                new WorkPullRequest("JSdotNet/Backlog#1215", 1215, "Passing", IntegrationArtifactState.Open)
                {
                    Status = new PullRequestStatusModel(1215, "open", new PullRequestChecks(14, 0, 0), new PullRequestApprovals(2, 2))
                }
            ])
            .Add(p => p.OnUnlinkPullRequest, _ => Task.CompletedTask));

        var lines = links.FindAll("[data-testid='work-links-pull-requests'] [data-testid='work-pull-request-status']");
        Assert.Equal(2, lines.Count);
        Assert.Equal("met", lines[1].QuerySelector("[data-mark='approvals']")!.GetAttribute("data-variant"));
        Assert.Equal("passed", lines[1].QuerySelector("[data-mark='checks']")!.GetAttribute("data-variant"));
    }

    [Fact]
    public void The_task_panel_section_draws_the_line_on_its_pull_request_cards()
    {
        using var context = new BunitContext();

        var panel = context.Render<WorkLinksPanel>(parameters => parameters.Add(p => p.PullRequests, [Pull(Running)]));

        var line = panel.Find("[data-testid='work-links-panel'] [data-testid='work-pull-request-status']");
        Assert.Equal("1 of 2 approvals", line.QuerySelector("[data-mark='approvals'] .sr-only")!.TextContent);
    }
}
