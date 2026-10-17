using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A goal's own page on the phone (<c>.devbook/domain/progress/features.md#goals-on-the-phone</c>):
/// its forecast and due date, done/total by the desktop's counting rules, the last four weeks
/// of what was done, how the open items stand, and its items under Plan, Tasks and Pull
/// requests — pull requests with the checks and approvals a desktop last read.
/// </summary>
public sealed class GoalScreenTests
{
    private static FakeTimeProvider Clock() => new(StandupFixture.Now);

    private static string Route(Guid id) => $"standup/goals/{id}";

    [Fact]
    public void The_page_counts_the_goal_by_the_desktop_rules()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());

        var app = host.Open(Route(StandupFixture.PaidId));

        app.WaitForAssertion(() => Assert.Equal("Customers get paid faster", app.Find("[data-testid='goal-title']").TextContent));
        Assert.Equal("At risk", app.Find("[data-testid='goal-standing']").TextContent.Trim());
        Assert.Contains("due 30 Oct", app.Find("[data-testid='goal-line']").TextContent, StringComparison.Ordinal);
        Assert.Equal("Goal", app.Find(".standup-back__title").TextContent);
        Assert.Equal("standup", app.Find("[data-testid='goal-back']").GetAttribute("href"));
        Assert.Equal("Back to the stand-up", app.Find("[data-testid='goal-back']").GetAttribute("aria-label"));

        // The plan's four entries, the task attached directly and the pull request attached
        // on its own: six items, three of them done.
        Assert.Equal("3/6", app.Find("[data-testid='goal-done']").TextContent);
        Assert.Equal("3 of 6 done, 1 in progress, 1 blocked, 1 not started", app.Find("[data-testid='goal-bar']").GetAttribute("aria-label"));
        Assert.Equal(
            ["1 doing", "1 blocked", "1 to do"],
            app.FindAll("[data-testid='goal-legend'] .standup-detail__key").Select(key => key.TextContent.Trim()));

        // All three were done in the week of 12 October; this week has none yet.
        var spark = app.Find("[data-testid='goal-spark']");
        Assert.Equal("img", spark.GetAttribute("role"));
        Assert.Equal("Done per week: 0, 0, 3, 0", spark.GetAttribute("aria-label"));
        Assert.Equal(4, spark.QuerySelectorAll(".week-spark__bar").Length);
    }

    [Fact]
    public void The_items_sit_under_plan_tasks_and_pull_requests()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());
        var app = host.Open(Route(StandupFixture.PaidId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='goal-plans']")));

        var plan = app.Find("[data-testid='goal-plan']");
        Assert.Equal("Bank integration", plan.QuerySelector("[data-testid='goal-plan-title']")!.TextContent);
        Assert.Equal("1/4", plan.QuerySelector("[data-testid='goal-plan-done']")!.TextContent);
        var open = app.FindAll("[data-testid='goal-plan-item']");
        Assert.Equal(3, open.Count);
        Assert.Contains("blocked", open.Single(item => item.TextContent.Contains("Statement format", StringComparison.Ordinal)).TextContent, StringComparison.Ordinal);

        // The pull request in progress: counts on screen, the words for a screen reader only.
        var mapping = open.Single(item => item.TextContent.Contains("Map the bank's fields", StringComparison.Ordinal));
        var checks = mapping.QuerySelector("[data-testid='goal-item-pr-checks']")!;
        Assert.Equal("14 of 14 checks passed", checks.QuerySelector(".sr-only")!.TextContent);
        Assert.Equal("14/14", checks.QuerySelector("[aria-hidden='true']:not(svg)")!.TextContent);
        var approvals = mapping.QuerySelector("[data-testid='goal-item-pr-approvals']")!;
        Assert.Equal("1 of 2 approvals", approvals.QuerySelector(".sr-only")!.TextContent);
        Assert.Equal("1/2", approvals.QuerySelector("[aria-hidden='true']:not(svg)")!.TextContent);
        var visible = string.Concat(mapping.QuerySelectorAll("[aria-hidden='true']").Select(part => part.TextContent));
        Assert.DoesNotContain("checks", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("approvals", visible, StringComparison.Ordinal);
        Assert.Contains("badge--gh-open", mapping.QuerySelector("[data-testid='goal-item-pr-number']")!.ClassList);

        var task = Assert.Single(app.FindAll("[data-testid='goal-task']"));
        Assert.Contains("Invoice PDF export", task.TextContent, StringComparison.Ordinal);
        Assert.Contains("status-dot--done", task.QuerySelector(".status-dot")!.ClassList);
        Assert.Null(task.QuerySelector("[data-testid='goal-item-pr-checks']"));

        var pull = Assert.Single(app.FindAll("[data-testid='goal-pull']"));
        Assert.Equal("#1352", pull.QuerySelector("[data-testid='goal-pull-status-number']")!.TextContent.Trim());
        Assert.Contains("badge--gh-merged", pull.QuerySelector("[data-testid='goal-pull-status-number']")!.ClassList);
        // "merged" is said once, by the shared line; the row adds no word of its own.
        Assert.Equal("merged", pull.QuerySelector("[data-testid='goal-pull-status-merged']")!.TextContent);
        Assert.Null(pull.QuerySelector("[data-testid='goal-pull-word']"));
        // Merged: its checks and approvals no longer decide anything, so neither mark is drawn.
        Assert.Null(pull.QuerySelector("[data-testid='goal-pull-status-checks']"));
        Assert.Null(pull.QuerySelector("[data-testid='goal-pull-status-approvals']"));
    }

    /// <summary>The shared status line says "merged" and nothing else in words, so an item's
    /// open pull request has its state said once beside it, for a screen reader only.</summary>
    [Fact]
    public void An_item_pull_request_says_its_state_to_a_screen_reader_unless_the_line_does()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());
        var app = host.Open(Route(StandupFixture.PaidId));
        app.WaitForAssertion(() => Assert.NotEmpty(app.FindAll("[data-testid='goal-plan-item']")));

        var mapping = app.FindAll("[data-testid='goal-plan-item']").Single(item => item.TextContent.Contains("Map the bank's fields", StringComparison.Ordinal));
        var state = mapping.QuerySelector("[data-testid='goal-item-pr-state']")!;
        Assert.Equal("open", state.TextContent);
        Assert.Contains("sr-only", state.ClassList);

        // Merged is said by the line itself, so the task row adds nothing.
        var invoice = Assert.Single(app.FindAll("[data-testid='goal-task']"));
        Assert.Equal("merged", invoice.QuerySelector("[data-testid='goal-item-pr-merged']")!.TextContent);
        Assert.Null(invoice.QuerySelector("[data-testid='goal-item-pr-state']"));
    }

    [Fact]
    public void A_goal_this_phone_does_not_hold_says_so_and_leads_back()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());

        var app = host.Open(Route(Guid.NewGuid()));

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='goal-missing']")));
        Assert.Equal("standup", app.Find("[data-testid='goal-missing'] a").GetAttribute("href"));
        Assert.Empty(app.FindAll("[data-testid='goal-page']"));
    }

    [Fact]
    public void Before_a_desktop_sends_the_stand_up_the_page_shows_no_goal()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service(withContext: false));

        var app = host.Open(Route(StandupFixture.PaidId));

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='goal-none']")));
        Assert.Empty(app.FindAll("[data-testid='goal-title']"));
    }
}
