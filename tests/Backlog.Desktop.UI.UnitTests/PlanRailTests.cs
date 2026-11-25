using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Goals;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks list's Plans / Flat switch and the Plans view's rail (local ADR 0062 §1, §2;
/// <c>.devbook/domain/tasks/requirements.md#plans-view</c>): the switch on the bar, the
/// rail's items in order with their counts, each plan's progress and run state, a rail
/// item selecting its plan or list, and the plan chips leaving the tag strip in Plans.
/// The switch's memory across a restart is <see cref="HomeWorkspaceSurfaceTests"/>'.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class PlanRailTests
{
    private const string Backlog = "JSdotNet/Backlog";

    private static async Task<(TasksPaneHost Host, TasksPlanSessionRequestTests.FakePlanTagSessionRequests Queue)> HostAsync()
    {
        var host = await TasksPaneHost.CreateAsync(Backlog);

        // +alpha: 1 of 4 done, one in a session, one ready.
        await host.WriteEntryAsync("# Wire it +alpha\n`task` `!done` `completed:2026-09-22`\n");
        await host.WriteEntryAsync("# Build it +alpha\n`task` `!in-progress`\n");
        await host.WriteEntryAsync("# Test it @bob +alpha\n`task` `!ready`\n");
        await host.WriteEntryAsync("# Ship it +alpha\n`task` `!draft`\n");
        // +beta: queued, nothing done.
        await host.WriteEntryAsync("# Plan it +beta\n`task` `!ready`\n");
        // +gamma: all drafts.
        await host.WriteEntryAsync("# Sketch it +gamma\n`task` `!draft`\n");
        // +delta: one blocked, nothing else ready.
        await host.WriteEntryAsync("# Fix the roof +delta\n`task` `!ready` `blocked:2026-10-05`\n");
        // +epsilon: running.
        await host.WriteEntryAsync("# Run it +epsilon\n`task` `!in-progress`\n");
        // Unplanned: two open, one done.
        await host.WriteEntryAsync("# Write the runbook\n`task` `!ready` `#docs`\n");
        await host.WriteEntryAsync("# Renew the certificate\n`task` `!draft`\n");
        await host.WriteEntryAsync("# Filed it\n`task` `!done` `completed:2026-09-20`\n");
        await host.State.SelectAsync(null);

        var queue = new TasksPlanSessionRequestTests.FakePlanTagSessionRequests();
        queue.Open["beta"] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog, DateTimeOffset.UnixEpoch, QueuePosition: 0);
        queue.Open["epsilon"] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Started, Backlog, DateTimeOffset.UnixEpoch);
        host.Context.Services.AddSingleton<IPlanTagSessionRequests>(queue);
        host.Context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));

        return (host, queue);
    }

    private static IRenderedComponent<TasksPane> RenderPlans(TasksPaneHost host, Action<TaskListGrouping>? picked = null) =>
        host.Context.Render<TasksPane>(parameters => parameters
            .Add(pane => pane.ListGrouping, TaskListGrouping.Plans)
            .Add(pane => pane.ListGroupingChanged, grouping => picked?.Invoke(grouping)));

    private static string Text(AngleSharp.Dom.IElement element) => element.TextContent.Trim();

    [Fact]
    public async Task The_switch_reads_Plans_and_picking_Flat_takes_the_rail_away_and_tells_the_host()
    {
        var (host, _) = await HostAsync();
        using var _host = host;
        var picked = new List<TaskListGrouping>();

        var pane = RenderPlans(host, picked.Add);

        var options = pane.FindAll("[data-testid='list-grouping'] [data-testid='list-grouping-option']");
        Assert.Equal(["Plans", "Flat"], options.Select(Text));
        Assert.Equal("true", options[0].GetAttribute("aria-checked"));
        Assert.Equal("false", options[1].GetAttribute("aria-checked"));
        Assert.NotNull(pane.Find("[data-testid='plan-rail']"));
        Assert.Contains("backlog-list--plans", pane.Find(".backlog-list").ClassList);

        pane.Find("[data-testid='list-grouping-option'][data-grouping='Flat']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([TaskListGrouping.Flat], picked);
            Assert.Empty(pane.FindAll("[data-testid='plan-rail']"));
            Assert.DoesNotContain("backlog-list--plans", pane.Find(".backlog-list").ClassList);
            Assert.Equal("true", pane.Find("[data-testid='list-grouping-option'][data-grouping='Flat']").GetAttribute("aria-checked"));
        });
    }

    [Fact]
    public async Task A_pane_with_no_host_choice_is_flat_and_the_Board_has_no_switch()
    {
        var (host, _) = await HostAsync();
        using var _host = host;

        var list = host.Render();
        Assert.Equal("true", list.Find("[data-testid='list-grouping-option'][data-grouping='Flat']").GetAttribute("aria-checked"));
        Assert.Empty(list.FindAll("[data-testid='plan-rail']"));

        list.Render(parameters => parameters
            .Add(pane => pane.Layout, TasksLayout.Board)
            .Add(pane => pane.ListGrouping, TaskListGrouping.Plans));

        Assert.Empty(list.FindAll("[data-testid='list-grouping']"));
        Assert.Empty(list.FindAll("[data-testid='plan-rail']"));
    }

    [Fact]
    public async Task The_rail_lists_My_Day_Now_each_plan_and_Unplanned_in_order_with_their_counts()
    {
        var (host, _) = await HostAsync();
        using var _host = host;

        var pane = RenderPlans(host);

        pane.WaitForAssertion(() =>
        {
            var items = pane.FindAll("[data-testid='plan-rail'] .plan-rail__item");
            Assert.Equal(
                ["plan-rail-myday", "plan-rail-now", "plan-rail-plan", "plan-rail-plan", "plan-rail-plan", "plan-rail-plan", "plan-rail-plan", "plan-rail-unplanned"],
                items.Select(item => item.GetAttribute("data-testid")));
            Assert.Equal("Plans · 5", Text(pane.Find("[data-testid='plan-rail-heading']")));
            Assert.Equal("0", Text(pane.Find("[data-testid='plan-rail-myday'] [data-testid='plan-rail-count']")));
            Assert.Equal("2", Text(pane.Find("[data-testid='plan-rail-now'] [data-testid='plan-rail-count']")));
            Assert.Equal("2", Text(pane.Find("[data-testid='plan-rail-unplanned'] [data-testid='plan-rail-count']")));
            Assert.Equal(
                ["alpha", "beta", "delta", "epsilon", "gamma"],
                pane.FindAll("[data-testid='plan-rail-plan']").Select(plan => plan.GetAttribute("data-plan")));
        });
    }

    [Fact]
    public async Task Each_plan_carries_its_done_and_total_a_progress_line_and_its_run_state_as_coloured_text()
    {
        var (host, _) = await HostAsync();
        using var _host = host;

        var pane = RenderPlans(host);

        pane.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement Plan(string name) => pane.Find($"[data-testid='plan-rail-plan'][data-plan='{name}']");
            string? State(string name) => Plan(name).QuerySelector("[data-testid='plan-rail-state']")?.TextContent;

            Assert.Equal("1/4", Text(Plan("alpha").QuerySelector("[data-testid='plan-rail-progress']")!));
            Assert.Equal("width: 25%", Plan("alpha").QuerySelector(".plan-rail__bar-fill")!.GetAttribute("style"));
            Assert.Equal("1 ready", State("alpha"));
            Assert.Equal("Queued 1st", State("beta"));
            Assert.Contains("plan-rail__state--queued", Plan("beta").QuerySelector("[data-testid='plan-rail-state']")!.ClassList);
            Assert.Equal("Blocked", State("delta"));
            Assert.Contains("plan-rail__state--blocked", Plan("delta").QuerySelector("[data-testid='plan-rail-state']")!.ClassList);
            Assert.Contains("plan-rail__bar--blocked", Plan("delta").QuerySelector(".plan-rail__bar")!.ClassList);
            Assert.Equal("Running", State("epsilon"));
            Assert.Contains("plan-rail__state--running", Plan("epsilon").QuerySelector("[data-testid='plan-rail-state']")!.ClassList);
            Assert.Equal("Draft", State("gamma"));
            Assert.Equal("alpha, 1 of 4 done, 1 ready", Plan("alpha").GetAttribute("aria-label"));
        });
    }

    [Fact]
    public async Task A_plan_with_nothing_to_say_shows_no_state()
    {
        var host = await TasksPaneHost.CreateAsync(Backlog);
        using var _host = host;
        await host.WriteEntryAsync("# Build it +quiet\n`task` `!in-progress`\n");
        await host.State.SelectAsync(null);

        var pane = RenderPlans(host);

        var plan = pane.Find("[data-testid='plan-rail-plan'][data-plan='quiet']");
        Assert.Null(plan.QuerySelector("[data-testid='plan-rail-state']"));
        Assert.Equal("0/1", Text(plan.QuerySelector("[data-testid='plan-rail-progress']")!));
        Assert.Equal("quiet, 0 of 1 done", plan.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Selecting_a_plan_lists_that_plan_and_selecting_it_again_lets_go()
    {
        var (host, _) = await HostAsync();
        using var _host = host;
        var pane = RenderPlans(host);

        pane.Find("[data-testid='plan-rail-plan'][data-plan='alpha']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("true", pane.Find("[data-testid='plan-rail-plan'][data-plan='alpha']").GetAttribute("aria-pressed"));
            Assert.Equal("+alpha", host.State.SelectedPlan);
            Assert.All(host.State.FilteredRows, row => Assert.Contains("+alpha", row.PreviewTags));
            Assert.Equal(4, host.State.FilteredRows.Count);
        });

        pane.Find("[data-testid='plan-rail-plan'][data-plan='alpha']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Null(host.State.SelectedPlan);
            Assert.Equal("false", pane.Find("[data-testid='plan-rail-plan'][data-plan='alpha']").GetAttribute("aria-pressed"));
            Assert.Equal(host.State.ScopedRows.Count, host.State.FilteredRows.Count);
        });
    }

    [Fact]
    public async Task Now_Unplanned_and_My_Day_each_select_their_list_one_at_a_time()
    {
        var (host, _) = await HostAsync();
        using var _host = host;
        var pane = RenderPlans(host);

        pane.Find("[data-testid='plan-rail-plan'][data-plan='alpha']").Click();
        pane.Find("[data-testid='plan-rail-now']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.True(host.State.NowAcrossPlansOnly);
            Assert.Null(host.State.SelectedPlan);
            Assert.Equal(["Build it +alpha", "Run it +epsilon"], host.State.FilteredRows.Select(row => row.PreviewTitle).Order());
            Assert.Equal("true", pane.Find("[data-testid='plan-rail-now']").GetAttribute("aria-pressed"));
        });

        pane.Find("[data-testid='plan-rail-unplanned']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.False(host.State.NowAcrossPlansOnly);
            Assert.True(host.State.IsTagSelected(TasksDesktopState.NoPlanTag));
            Assert.Equal(["Filed it", "Renew the certificate", "Write the runbook"], host.State.FilteredRows.Select(row => row.PreviewTitle).Order());
        });

        pane.Find("[data-testid='plan-rail-myday']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.NotNull(host.State.MyDayOn);
            Assert.False(host.State.IsTagSelected(TasksDesktopState.NoPlanTag));
            Assert.Equal("true", pane.Find("[data-testid='plan-rail-myday']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public async Task In_Plans_the_strip_keeps_people_and_tags_and_drops_the_plan_chips()
    {
        var (host, _) = await HostAsync();
        using var _host = host;

        var plans = RenderPlans(host);
        var stripped = plans.FindAll("[data-testid='tag-filter-option']").Select(Text).ToList();

        Assert.True(stripped.Any(label => label.StartsWith("@bob", StringComparison.Ordinal)), string.Join(" | ", stripped));
        Assert.Contains(stripped, label => label.StartsWith("#docs", StringComparison.Ordinal));
        Assert.DoesNotContain(stripped, label => label.StartsWith('+'));
        Assert.DoesNotContain(stripped, label => label.StartsWith("No plan", StringComparison.Ordinal));

        var flat = host.Render();
        var all = flat.FindAll("[data-testid='tag-filter-option']").Select(Text).ToList();
        Assert.True(all.Any(label => label.StartsWith("+alpha", StringComparison.Ordinal)), string.Join(" | ", all));
    }

    [Fact]
    public async Task Leaving_Plans_lets_go_of_Now_across_plans()
    {
        var (host, _) = await HostAsync();
        using var _host = host;
        var pane = RenderPlans(host);

        pane.Find("[data-testid='plan-rail-now']").Click();
        pane.WaitForAssertion(() => Assert.True(host.State.NowAcrossPlansOnly));

        pane.Find("[data-testid='list-grouping-option'][data-grouping='Flat']").Click();

        pane.WaitForAssertion(() => Assert.False(host.State.NowAcrossPlansOnly));
    }

    [Fact]
    public async Task On_a_narrow_column_the_rail_folds_into_the_Filter_menu()
    {
        var (host, _) = await HostAsync();
        using var _host = host;
        var pane = RenderPlans(host);

        await pane.InvokeAsync(() => pane.Instance.ListNarrowChangedAsync(true));

        Assert.DoesNotContain("backlog-list--plans", pane.Find(".backlog-list").ClassList);
        Assert.Empty(pane.FindAll("[data-testid='plan-rail']"));
        var trigger = pane.Find("[data-testid='filter-menu-button']");
        Assert.Equal("Pick a plan", trigger.GetAttribute("title"));

        await trigger.ClickAsync(new());

        var rail = Assert.Single(pane.FindAll("[data-testid='plan-rail']"));
        Assert.NotNull(rail.Closest("[data-testid='filter-menu-panel']"));

        await rail.QuerySelector("[data-testid='plan-rail-plan'][data-plan='alpha']")!.ClickAsync(new());

        Assert.Equal("+alpha", host.State.SelectedPlan);
        Assert.Equal("Pick a plan — 1 filter on", pane.Find("[data-testid='filter-menu-button']").GetAttribute("title"));
    }

    [Fact]
    public async Task A_blocked_plan_names_the_open_impediment_holding_it_back_in_its_tooltip()
    {
        var (host, _) = await HostAsync();
        using var _host = host;
        var asked = new List<(string Plan, IReadOnlyCollection<Guid> Entries)>();
        var source = new PlanImpedimentSource((plan, entries) =>
        {
            asked.Add((plan, entries));
            return plan == "delta" ? "Roofer not booked" : null;
        });

        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.ListGrouping, TaskListGrouping.Plans)
            .AddCascadingValue(source));

        pane.WaitForAssertion(() =>
        {
            var delta = pane.Find("[data-testid='plan-rail-plan'][data-plan='delta']");
            var state = delta.QuerySelector("[data-testid='plan-rail-state']")!;
            Assert.Equal("Blocked", Text(state));
            Assert.Equal("Blocked · Roofer not booked", state.GetAttribute("title"));
            Assert.Equal("delta, 0 of 1 done, Blocked · Roofer not booked", delta.GetAttribute("aria-label"));
        });

        // Asked only for the Blocked plan, with its open entry's id and the bare tag.
        var (plan, entries) = Assert.Single(asked.DistinctBy(call => call.Plan));
        Assert.Equal("delta", plan);
        var roof = host.State.Rows.Single(row => row.PreviewTitle.StartsWith("Fix the roof", StringComparison.Ordinal));
        Assert.Equal([roof.Id!.Value], entries);
    }

    [Fact]
    public async Task A_blocked_plan_with_no_impediment_linked_reads_plain_Blocked()
    {
        var (host, _) = await HostAsync();
        using var _host = host;
        var source = new PlanImpedimentSource((_, _) => null);

        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.ListGrouping, TaskListGrouping.Plans)
            .AddCascadingValue(source));

        pane.WaitForAssertion(() =>
        {
            var delta = pane.Find("[data-testid='plan-rail-plan'][data-plan='delta']");
            Assert.Equal("Blocked", delta.QuerySelector("[data-testid='plan-rail-state']")!.GetAttribute("title"));
            Assert.Equal("delta, 0 of 1 done, Blocked", delta.GetAttribute("aria-label"));
            // A state that is not Blocked carries no tooltip.
            Assert.Null(pane.Find("[data-testid='plan-rail-plan'][data-plan='alpha'] [data-testid='plan-rail-state']").GetAttribute("title"));
        });
    }

    [Fact]
    public async Task The_rail_names_an_impediment_once_the_source_says_it_changed()
    {
        var (host, _) = await HostAsync();
        using var _host = host;
        string? title = null;
        Action? changed = null;
        var source = new PlanImpedimentSource(
            (_, _) => title,
            watch: callback =>
            {
                changed = callback;
                return new Released(() => changed = null);
            });

        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.ListGrouping, TaskListGrouping.Plans)
            .AddCascadingValue(source));
        string? Tooltip() => pane.Find("[data-testid='plan-rail-plan'][data-plan='delta'] [data-testid='plan-rail-state']").GetAttribute("title");

        pane.WaitForAssertion(() => Assert.Equal("Blocked", Tooltip()));

        title = "Roofer not booked";
        Assert.NotNull(changed);
        changed!();

        pane.WaitForAssertion(() => Assert.Equal("Blocked · Roofer not booked", Tooltip()));
    }

    private sealed class Released(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    public void A_queue_place_reads_as_an_ordinal(int place, string expected) =>
        Assert.Equal(expected, PlanRail.Ordinal(place));
}
