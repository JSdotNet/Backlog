using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's Stand-up tab (<c>.devbook/domain/progress/features.md#goals-on-the-phone</c>):
/// shown between Inbox and Notes only while goals are on — which the phone reads as holding
/// a stand-up context — and drawing the four blocks the desktop reads, by its rules, with the
/// iteration switch re-reading them for this visit.
/// </summary>
public sealed class StandupScreenTests
{
    private static FakeTimeProvider Clock() => new(StandupFixture.Now);

    [Fact]
    public void Without_a_stand_up_context_there_is_no_stand_up_tab()
    {
        var tasks = StandupFixture.Service(withContext: false);
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);

        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Equal(2, tasks.Pulls));
        Assert.Equal(["Today", "Inbox", "Notes"], app.FindAll("[data-testid='tab-bar'] a").Select(tab => tab.TextContent.Trim()));
    }

    [Fact]
    public void With_a_stand_up_context_the_tab_sits_between_inbox_and_notes()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());

        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Equal(
            ["Today", "Inbox", "Stand-up", "Notes"],
            app.FindAll("[data-testid='tab-bar'] a").Select(tab => tab.TextContent.Trim())));
        Assert.Equal(["", "inbox", "standup", "notes"], app.FindAll("[data-testid='tab-bar'] a").Select(tab => tab.GetAttribute("href")));
    }

    /// <summary>
    /// The Stand-up tab wears the target the desktop's Goals option does, and the
    /// other tabs wear glyphs of their own, so no tab reads as the odd one out.
    /// </summary>
    [Fact]
    public void Every_tab_leads_with_its_glyph_and_stand_up_wears_the_target()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());

        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Equal(4, app.FindAll("[data-testid='tab-bar'] a").Count));
        Assert.Equal(
            ["sun-icon", "inbox-icon", "target-icon", "sticky-note-icon"],
            app.FindAll("[data-testid='tab-bar'] a .tab-bar__icon svg").Select(glyph => glyph.GetAttribute("class")));
        Assert.All(
            app.FindAll("[data-testid='tab-bar'] a .tab-bar__icon svg"),
            glyph => Assert.Equal("true", glyph.GetAttribute("aria-hidden")));
    }

    [Fact]
    public async Task A_first_pull_that_failed_is_tried_again_when_the_app_comes_back()
    {
        var tasks = StandupFixture.Service();
        tasks.State = InboxServiceState.Unreachable;
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open("");
        app.WaitForAssertion(() => Assert.True(tasks.Pulls == 0 && app.FindAll("[data-testid='tab-bar'] a").Count == 3));

        tasks.State = InboxServiceState.Answering;
        await app.InvokeAsync(() => host.Service<Backlog.Mobile.UI.Services.AppLifecycle>().Resume());

        app.WaitForAssertion(() => Assert.Equal(
            ["Today", "Inbox", "Stand-up", "Notes"],
            app.FindAll("[data-testid='tab-bar'] a").Select(tab => tab.TextContent.Trim())));
    }

    [Fact]
    public void Before_a_desktop_sends_the_stand_up_the_page_says_so_and_shows_none()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service(withContext: false));

        var app = host.Open("standup");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='standup-none']")));
        Assert.Equal("No stand-up yet", app.Find("[data-testid='standup-none'] .empty-state__title").TextContent.Trim());
        Assert.Empty(app.FindAll("[data-testid='standup']"));
        Assert.Empty(app.FindAll("[data-testid='standup-goal']"));
    }

    [Fact]
    public void The_stand_up_draws_its_goals_blocks_and_impediments()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());

        var app = host.Open("standup");

        app.WaitForAssertion(() => Assert.Equal(2, app.FindAll("[data-testid='standup-goal']").Count));
        Assert.Equal("Stand-up", app.Find("h1").TextContent);

        Assert.Equal(
            ["Customers get paid faster", "Devbook search ships"],
            app.FindAll("[data-testid='standup-goal-title']").Select(title => title.TextContent));
        Assert.Equal(["At risk", "On track"], app.FindAll("[data-testid='standup-goal-standing']").Select(badge => badge.TextContent.Trim()));
        Assert.Equal("3/6 · +3", Squash(app.FindAll("[data-testid='standup-goal-done']")[0].TextContent));
        Assert.Equal("due 30 Oct", app.FindAll("[data-testid='standup-goal-due']")[0].TextContent);
        Assert.Equal(
            "3 of 6 done, 1 in progress, 1 blocked, 1 not started",
            app.FindAll("[data-testid='standup-goal-bar']")[0].GetAttribute("aria-label"));

        // Finished: grouped by goal, No goal last, a merged pull request's number beside its task.
        Assert.Equal("5", app.Find("[data-testid='standup-finished-total']").TextContent.Trim());
        Assert.Equal(
            ["Customers get paid faster", "Devbook search ships", "No goal"],
            app.FindAll("[data-testid='standup-finished-list-goal']").Select(goal => goal.TextContent));
        var finished = app.FindAll("[data-testid='standup-finished-list-title']").Select(title => title.TextContent).ToList();
        Assert.Contains("Invoice PDF export", finished);
        Assert.Contains("VAT rate per line item", finished);
        // A pull request attached straight to the goal is named once, by its chip.
        Assert.Contains("Pull request", finished);
        Assert.DoesNotContain(finished, title => title.Contains("JSdotNet/Backlog#", StringComparison.Ordinal));
        var invoice = app.FindAll("[data-testid='standup-finished-list-item']").Single(item => item.TextContent.Contains("Invoice PDF export", StringComparison.Ordinal));
        Assert.Contains("badge--gh-merged", invoice.QuerySelector(".badge")!.ClassList);
        Assert.Equal("#1338 merged", invoice.QuerySelector(".badge")!.TextContent.Trim());
        Assert.Equal(" merged", invoice.QuerySelector(".badge .sr-only")!.TextContent);

        // Planned: the work in progress and the plan's first ready entry, with the planned points
        // against the pace, and the checks of the pull request in progress.
        Assert.Equal("2", app.Find("[data-testid='standup-planned-total']").TextContent.Trim());
        Assert.Equal(
            ["Map the bank's fields", "Reconcile payments"],
            app.FindAll("[data-testid='standup-planned-list-title']").Select(title => title.TextContent));
        var points = app.Find("[data-testid='standup-points']");
        Assert.Equal("13 / 18 pts", points.TextContent);
        Assert.Equal("13 points planned, you usually finish 18 a week", points.GetAttribute("title"));
        Assert.Equal("14 of 14 checks passed", app.Find("[data-testid='standup-planned-pr-checks'] .sr-only").TextContent);
        Assert.Contains("8 pts", app.FindAll("[data-testid='standup-planned-list-item']")[1].TextContent, StringComparison.Ordinal);

        // Impediments: the open ones, oldest first, and the one resolved last week folded away.
        Assert.Equal("2", app.Find("[data-testid='standup-impediments-total']").TextContent.Trim());
        Assert.Equal(
            ["Bank sample files missing", "Credit note numbering unclear"],
            app.FindAll("[data-testid='standup-impediment-title']").Select(title => title.TextContent));
        Assert.Equal(["Escalated", "New"], app.FindAll("[data-testid='standup-impediment-state']").Select(badge => badge.TextContent.Trim()));
        Assert.Equal(
            "account manager · check Tue · Customers get paid faster",
            Squash(app.FindAll("[data-testid='standup-impediment-meta']")[0].TextContent));
        Assert.StartsWith("Not reported yet", Squash(app.FindAll("[data-testid='standup-impediment-meta']")[1].TextContent), StringComparison.Ordinal);
        Assert.Equal($"standup/impediments/{StandupFixture.EscalatedId}", app.FindAll("[data-testid='standup-impediment']")[0].GetAttribute("href"));

        var fold = app.Find("[data-testid='standup-resolved']");
        Assert.Equal("1 resolved", fold.TextContent.Replace("▸", string.Empty, StringComparison.Ordinal).Trim());
        Assert.Equal("false", fold.GetAttribute("aria-expanded"));

        fold.Click();

        app.WaitForAssertion(() => Assert.Equal("true", app.Find("[data-testid='standup-resolved']").GetAttribute("aria-expanded")));
        var resolved = app.Find("[data-testid='standup-resolved-impediment']");
        Assert.Equal("Staging VPN certificate expired", resolved.QuerySelector("[data-testid='standup-resolved-impediment-title']")!.TextContent);
        Assert.Equal("Resolved 14 Oct", Squash(resolved.QuerySelector("[data-testid='standup-resolved-impediment-meta']")!.TextContent));
    }

    /// <summary>A planned item's pull request says its state once, for a screen reader, beside
    /// the shared line that draws it only as the pill's tone.</summary>
    [Fact]
    public void A_planned_pull_request_says_its_state_to_a_screen_reader()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());

        var app = host.Open("standup");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='standup-planned-pr-state']")));
        var state = Assert.Single(app.FindAll("[data-testid='standup-planned-pr-state']"));
        Assert.Equal("open", state.TextContent);
        Assert.Contains("sr-only", state.ClassList);
        Assert.Contains("Map the bank's fields", state.Closest("[data-testid='standup-planned-list-item']")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_iteration_switch_opens_on_the_desktop_length_and_re_reads_the_windows()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());
        var app = host.Open("standup");

        app.WaitForAssertion(() => Assert.Equal("12–16 Oct → 19–23 Oct", app.Find("[data-testid='standup-windows']").TextContent));
        var group = app.Find("[data-testid='standup-iteration']");
        Assert.Equal("radiogroup", group.GetAttribute("role"));
        Assert.Equal(
            ["Day", "Week", "2 weeks", "Month"],
            app.FindAll("[data-testid='standup-iteration'] [role='radio']").Select(radio => radio.TextContent.Trim()));
        Assert.Equal("true", app.Find("[data-testid='standup-length-week']").GetAttribute("aria-checked"));

        app.Find("[data-testid='standup-length-day']").Click();

        app.WaitForAssertion(() => Assert.Equal("16 Oct → 19 Oct", app.Find("[data-testid='standup-windows']").TextContent));
        Assert.Equal("true", app.Find("[data-testid='standup-length-day']").GetAttribute("aria-checked"));

        // Last is Friday alone now, so Finished holds only what was done that day.
        Assert.Equal(["VAT rate per line item"], app.FindAll("[data-testid='standup-finished-list-title']").Select(title => title.TextContent));
    }

    [Fact]
    public void A_goal_card_opens_the_goals_own_page()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());
        var app = host.Open("standup");
        app.WaitForAssertion(() => Assert.Equal(2, app.FindAll("[data-testid='standup-goal']").Count));

        var card = app.FindAll("[data-testid='standup-goal']")[0];
        Assert.Equal($"standup/goals/{StandupFixture.PaidId}", card.GetAttribute("href"));

        host.Navigation.NavigateTo(card.GetAttribute("href")!);

        app.WaitForAssertion(() => Assert.Equal("Customers get paid faster", app.Find("[data-testid='goal-title']").TextContent));
        Assert.Contains("tab-bar__tab--active", app.Find("[data-testid='tab-bar-stand-up']").ClassList);
    }

    [Fact]
    public void A_pull_that_does_not_answer_keeps_the_stand_up_and_says_why()
    {
        var tasks = StandupFixture.Service();
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open("standup");
        app.WaitForAssertion(() => Assert.Equal(2, app.FindAll("[data-testid='standup-goal']").Count));

        tasks.State = InboxServiceState.Unreachable;
        app.Find("[data-testid='standup-refresh']").Click();

        app.WaitForAssertion(() => Assert.Contains(
            "Cloud sync can't be reached.",
            app.Find("[data-testid='standup-pull-notice']").TextContent,
            StringComparison.Ordinal));
        Assert.Equal(2, app.FindAll("[data-testid='standup-goal']").Count);
    }

    private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
