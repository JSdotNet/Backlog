using System.Globalization;
using Backlog.UI.Components.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks pane in its Calendar layout: the same filtered rows, placed by due
/// date under the same filter bar and beside the same detail panel, with a drop
/// written as the entry's due date.
/// <para>
/// Dates are relative to the real today, because the pane reads its own clock the
/// way the list does for My Day.
/// </para>
/// </summary>
public sealed class TasksPaneCalendarTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IRenderedComponent<TasksPane> RenderCalendar(TasksPaneHost host) =>
        host.Context.Render<TasksPane>(parameters => parameters.Add(pane => pane.Layout, TasksLayout.Calendar));

    private static string ChipTestId(EntryRow row) => $"task-calendar-chip-{row.TaskId}";

    private static string TrayTestId(EntryRow row) => $"task-calendar-tray-{row.TaskId}";

    [Fact]
    public async Task The_calendar_replaces_the_list_under_the_same_filter_bar()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var dated = await host.WriteEntryAsync($"# Ship the calendar\n`task` `!ready` `due:{Iso(Today)}`\n");

        var pane = RenderCalendar(host);

        Assert.NotEmpty(pane.FindAll(".filter-bar"));
        Assert.Empty(pane.FindAll("[data-testid='entry-list']"));
        var calendar = pane.Find("[data-testid='task-calendar']");
        Assert.NotNull(calendar.QuerySelector($"[data-calendar-day='{Iso(Today)}'] [data-testid='{ChipTestId(dated)}']"));

        // The filter bar comes first: it is above the calendar in the column.
        var column = pane.Find(".backlog-list");
        Assert.True(column.InnerHtml.IndexOf("filter-bar", StringComparison.Ordinal)
                    < column.InnerHtml.IndexOf("task-calendar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_calendar_reads_the_filtered_rows()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var ready = await host.WriteEntryAsync($"# Ready one\n`task` `!ready` `due:{Iso(Today)}`\n");
        var draft = await host.WriteEntryAsync($"# Draft one\n`task` `due:{Iso(Today)}`\n");
        var undated = await host.WriteEntryAsync("# Undated draft\n`task`\n");

        var pane = RenderCalendar(host);
        Assert.NotEmpty(pane.FindAll($"[data-testid='{ChipTestId(draft)}']"));
        Assert.NotEmpty(pane.FindAll($"[data-testid='{TrayTestId(undated)}']"));

        pane.Find("#status-filter-ready").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.NotEmpty(pane.FindAll($"[data-testid='{ChipTestId(ready)}']"));
            Assert.Empty(pane.FindAll($"[data-testid='{ChipTestId(draft)}']"));
            Assert.Empty(pane.FindAll($"[data-testid='{TrayTestId(undated)}']"));
        });
    }

    [Fact]
    public async Task Overdue_and_done_entries_are_drawn_apart()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var yesterday = Today.AddDays(-1);
        var late = await host.WriteEntryAsync($"# Late one\n`task` `!ready` `due:{Iso(yesterday)}`\n");
        var finished = await host.WriteEntryAsync($"# Finished one\n`task` `!done` `completed:{Iso(yesterday)}` `due:{Iso(yesterday)}`\n");

        var pane = RenderCalendar(host);

        // On the 1st, yesterday is in the month before, which a grid that opens on
        // a Monday the 1st does not reach; step back to it.
        if (Today.Day == 1) pane.Find("[data-testid='task-calendar-previous']").Click();

        Assert.Contains("task-calendar__chip--overdue", pane.Find($"[data-testid='{ChipTestId(late)}']").ClassName);
        var done = pane.Find($"[data-testid='{ChipTestId(finished)}']");
        Assert.Contains("task-calendar__chip--done", done.ClassName);
        Assert.DoesNotContain("task-calendar__chip--overdue", done.ClassName);
    }

    [Fact]
    public async Task Pressing_a_chip_opens_the_existing_detail_panel()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var dated = await host.WriteEntryAsync($"# Ship the calendar\n`task` `due:{Iso(Today)}`\n");
        await host.State.SelectAsync(null);

        var pane = RenderCalendar(host);
        Assert.Empty(pane.FindAll("[data-testid='entry-detail']"));

        pane.Find($"[data-testid='{ChipTestId(dated)}']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Single(pane.FindAll("[data-testid='entry-detail']"));
            Assert.Same(dated, host.State.SelectedRow);
        });
    }

    /// <summary>Escape from the detail panel puts the focus back on the chip that
    /// opened it, as it puts it back on the row in the list.</summary>
    [Fact]
    public async Task Escape_closes_the_panel_and_returns_the_focus_to_the_chip()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var dated = await host.WriteEntryAsync($"# Ship the calendar\n`task` `due:{Iso(Today)}`\n");

        var pane = RenderCalendar(host);
        Assert.Same(dated, host.State.SelectedRow);
        var chipId = pane.Find($"[data-testid='{ChipTestId(dated)}']").Id;

        await pane.Find("[data-testid='entry-detail']").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.Null(host.State.SelectedRow);
        Assert.Contains(
            host.Context.JSInterop.Invocations["backlogFocus"],
            call => string.Equals(call.Arguments[0] as string, chipId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dropping_a_tray_entry_on_a_day_writes_its_due_date()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var undated = await host.WriteEntryAsync("# Persist the last view\n`task` `!ready`\n");
        // Another day of this month, so it is on the grid whatever the date.
        var day = Today.Day <= 15 ? Today.AddDays(3) : Today.AddDays(-3);

        var pane = RenderCalendar(host);
        var calendar = pane.FindComponent<TaskCalendar>();

        await calendar.InvokeAsync(() => calendar.Instance.DropOnDay(undated.TaskId, Iso(day)));

        Assert.Equal(day, undated.PreviewDueOn);
        Assert.Contains($"`due:{Iso(day)}`", undated.RawText, StringComparison.Ordinal);
        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll($"[data-testid='{TrayTestId(undated)}']"));
            Assert.NotNull(pane.Find($"[data-calendar-day='{Iso(day)}']").QuerySelector($"[data-testid='{ChipTestId(undated)}']"));
        });

        // What was written is what the store holds, not only what the row previews.
        await host.State.ReloadFromStoreAsync();
        var reloaded = Assert.Single(host.State.Rows, row => row.Id == undated.Id);
        Assert.Equal(day, reloaded.PreviewDueOn);
    }

    [Fact]
    public async Task Dragging_a_chip_to_another_day_moves_its_due_date()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var dated = await host.WriteEntryAsync($"# Ship the calendar\n`task` `due:{Iso(Today)}`\n");
        var day = Today.AddDays(1);

        var pane = RenderCalendar(host);
        var calendar = pane.FindComponent<TaskCalendar>();

        await calendar.InvokeAsync(() => calendar.Instance.DropOnDay(dated.TaskId, Iso(day)));

        Assert.Equal(day, dated.PreviewDueOn);
        Assert.Contains($"`due:{Iso(day)}`", dated.RawText, StringComparison.Ordinal);
        Assert.DoesNotContain($"`due:{Iso(Today)}`", dated.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_repository_colour_follows_the_visualization_switch()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var dated = await host.WriteEntryAsync($"# Ship the calendar\n`task` `repo:backlog` `due:{Iso(Today)}`\n");
        var undated = await host.WriteEntryAsync("# Undated\n`task` `repo:backlog`\n");

        var pane = RenderCalendar(host);
        Assert.DoesNotContain("repo-mark", pane.Find($"[data-testid='{ChipTestId(dated)}']").ClassName);
        Assert.DoesNotContain("repo-mark", pane.Find($"[data-testid='{TrayTestId(undated)}']").ClassName);

        Assert.Null(host.GitHub.Settings.SetShowRepositoryColours(true));
        pane.Render();

        Assert.Contains("repo-mark--1", pane.Find($"[data-testid='{ChipTestId(dated)}']").ClassName);
        Assert.Contains("repo-mark--1", pane.Find($"[data-testid='{TrayTestId(undated)}']").ClassName);
    }

    // --- The roadmap's plans --------------------------------------------------

    private static readonly Guid PlanId = Guid.Parse("6fc104a3-c9cb-47c7-96a8-64cf629121f7");

    private static readonly Guid StartedId = Guid.Parse("5122f5d7-743a-4deb-a9c3-77cbf0cac5b8");

    private static string PlanTestId => $"task-calendar-plan-{PlanId:N}";

    [Fact]
    public async Task Without_a_roadmap_the_calendar_offers_no_plans()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var pane = RenderCalendar(host);

        Assert.Empty(pane.FindAll("[data-testid='task-calendar-show-plans']"));
        Assert.DoesNotContain("Plans without a window", pane.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_plans_windows_milestones_and_shelf_are_drawn_from_the_port()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var plans = new FakeCalendarPlans(Today);
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);

        var pane = RenderCalendar(host);

        pane.WaitForAssertion(() =>
        {
            var bar = pane.FindAll($"[data-testid='{PlanTestId}']")[0];
            Assert.Equal("Calendar plans · +task-views · 3 of 8 pts", bar.GetAttribute("title"));
            Assert.Contains("task-calendar__plan--neutral", bar.ClassName);
            Assert.NotNull(pane.Find($"[data-calendar-day='{Iso(Today)}']").QuerySelector(".task-calendar__milestone"));
            Assert.Equal("release-q4", pane.Find("[data-testid='task-calendar-shelf-release-q4']").GetAttribute("data-calendar-plan"));
        });
        Assert.Equal(1, plans.Reads);
    }

    /// <summary>The plans are read on arriving at the Calendar and whenever the port says
    /// they changed — the plan, its work or the pace — not on every render.</summary>
    [Fact]
    public async Task The_plans_are_read_again_on_returning_to_the_calendar_and_on_a_change()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var plans = new FakeCalendarPlans(Today);
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);

        var pane = RenderCalendar(host);
        pane.WaitForAssertion(() => Assert.Equal(1, plans.Reads));
        pane.Render();
        Assert.Equal(1, plans.Reads);

        pane.Render(parameters => parameters.Add(p => p.Layout, TasksLayout.List));
        pane.Render(parameters => parameters.Add(p => p.Layout, TasksLayout.Calendar));
        pane.WaitForAssertion(() => Assert.Equal(2, plans.Reads));

        plans.RaiseChanged();
        pane.WaitForAssertion(() => Assert.Equal(3, plans.Reads));
    }

    [Fact]
    public async Task A_bar_wears_its_repository_colour_only_while_the_colours_are_showing()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));

        var pane = RenderCalendar(host);
        pane.WaitForAssertion(() => Assert.Contains("task-calendar__plan--neutral", pane.FindAll($"[data-testid='{PlanTestId}']")[0].ClassName));

        Assert.Null(host.GitHub.Settings.SetShowRepositoryColours(true));
        pane.Render();

        Assert.Contains("task-calendar__plan--band-1", pane.FindAll($"[data-testid='{PlanTestId}']")[0].ClassName);
    }

    [Fact]
    public async Task Unticking_show_plans_is_remembered_through_the_port_and_hides_the_plans()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var plans = new FakeCalendarPlans(Today);
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);

        var pane = RenderCalendar(host);
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll($"[data-testid='{PlanTestId}']")));
        Assert.True(pane.Find("[data-testid='task-calendar-show-plans'] input").HasAttribute("checked"));

        pane.Find("[data-testid='task-calendar-show-plans'] input").Change(false);

        Assert.False(plans.Shown);
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll($"[data-testid='{PlanTestId}']")));
    }

    /// <summary>The shelf drop: the pane hands the tag and the day to the port — whose
    /// adapter has the roadmap's own import place the window — and draws what it reads
    /// back.</summary>
    [Fact]
    public async Task Dropping_a_shelf_plan_on_a_day_starts_its_window_there_through_the_port()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var plans = new FakeCalendarPlans(Today);
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);
        var day = Today.Day <= 15 ? Today.AddDays(3) : Today.AddDays(-3);

        var pane = RenderCalendar(host);
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='task-calendar-shelf-release-q4']")));
        var calendar = pane.FindComponent<TaskCalendar>();

        await calendar.InvokeAsync(() => calendar.Instance.DropPlanOnDay("release-q4", Iso(day)));

        Assert.Equal([("release-q4", day)], plans.Started);
        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='task-calendar-shelf-release-q4']"));
            Assert.NotEmpty(pane.FindAll($"[data-testid='task-calendar-plan-{StartedId:N}']"));
        });
        Assert.Empty(host.Toasts.Visible);
    }

    [Fact]
    public async Task A_refused_shelf_drop_is_said_in_a_toast_and_the_plan_stays_on_the_shelf()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var plans = new FakeCalendarPlans(Today) { Refusal = "a cycle" };
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);

        var pane = RenderCalendar(host);
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='task-calendar-shelf-release-q4']")));
        var calendar = pane.FindComponent<TaskCalendar>();

        await calendar.InvokeAsync(() => calendar.Instance.DropPlanOnDay("release-q4", Iso(Today)));

        Assert.Contains(host.Toasts.Visible, toast => toast.Message.Contains("+release-q4 could not be planned: a cycle", StringComparison.Ordinal));
        Assert.NotEmpty(pane.FindAll("[data-testid='task-calendar-shelf-release-q4']"));
    }

    /// <summary>A roadmap with one plan through today, a milestone today, and one plan on
    /// the shelf that a start moves onto the plan.</summary>
    private sealed class FakeCalendarPlans(DateOnly today) : ICalendarPlans
    {
        private bool _started;

        public event Action? Changed;

        public bool Shown { get; private set; } = true;

        public int Reads { get; private set; }

        public string? Refusal { get; init; }

        public List<(string Tag, DateOnly Start)> Started { get; } = [];

        public void SetShown(bool shown) => Shown = shown;

        public void RaiseChanged() => Changed?.Invoke();

        public Task<CalendarPlansDto> ReadAsync(DateOnly day, CancellationToken cancellationToken = default)
        {
            Reads++;
            List<CalendarPlanWindowDto> windows =
                [new(PlanId, "Calendar plans", "task-views", today.AddDays(-1), today.AddDays(1), ["backlog"], 3, 8)];
            List<CalendarShelfPlanDto> shelf = [];

            if (_started) windows.Add(new(StartedId, "Release q4", "release-q4", Started[0].Start, Started[0].Start, [], 0, 5));
            else shelf.Add(new("release-q4", "Release q4", [], 3, 5, 0));

            return Task.FromResult(new CalendarPlansDto(windows, [new(Guid.NewGuid(), "Release", today)], shelf));
        }

        public Task<string?> StartAsync(string tag, DateOnly start, CancellationToken cancellationToken = default)
        {
            Started.Add((tag, start));
            if (Refusal is not null) return Task.FromResult<string?>(Refusal);

            _started = true;
            Changed?.Invoke();
            return Task.FromResult<string?>(null);
        }
    }
}
