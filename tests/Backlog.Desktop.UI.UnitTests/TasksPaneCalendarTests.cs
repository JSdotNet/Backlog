using System.Globalization;
using Backlog.Modules.Calendar.UI;
using Backlog.UI.Components.Tasks;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks layer on the Calendar surface (local ADR 0055 §3): the part Tasks offers,
/// drawing the same filtered rows placed by due date under the same filter bar and beside
/// the same detail panel, with a drop written as the entry's due date.
/// <para>
/// Dates are relative to the real today, because the part reads its own clock the
/// way the list does for My Day.
/// </para>
/// </summary>
public sealed class TasksPaneCalendarTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IRenderedComponent<CalendarSurface> RenderCalendar(TasksPaneHost host) =>
        host.WithCalendarSurface().RenderCalendarSurface();

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

    /// <summary>The type filter narrows the rows the Calendar reads: its month's chips
    /// and its tray of tasks with no due date alike, and pressing it again widens both.</summary>
    [Fact]
    public async Task The_type_filter_narrows_the_calendars_chips_and_its_undated_tray()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var datedPrompt = await host.WriteEntryAsync($"# Dated prompt\n`prompt` `due:{Iso(Today)}`\n");
        var datedTask = await host.WriteEntryAsync($"# Dated task\n`task` `due:{Iso(Today)}`\n");
        var undatedPrompt = await host.WriteEntryAsync("# Undated prompt\n`prompt`\n");
        var undatedIdea = await host.WriteEntryAsync("# Undated idea\n`idea`\n");

        var pane = RenderCalendar(host);
        Assert.NotEmpty(pane.FindAll($"[data-testid='{ChipTestId(datedTask)}']"));
        Assert.NotEmpty(pane.FindAll($"[data-testid='{TrayTestId(undatedIdea)}']"));

        pane.Find("[data-testid='type-filter-option'][data-type='prompt']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(EntryType.Prompt, host.State.SelectedType);
            Assert.NotEmpty(pane.FindAll($"[data-testid='{ChipTestId(datedPrompt)}']"));
            Assert.Empty(pane.FindAll($"[data-testid='{ChipTestId(datedTask)}']"));
            Assert.NotEmpty(pane.FindAll($"[data-testid='{TrayTestId(undatedPrompt)}']"));
            Assert.Empty(pane.FindAll($"[data-testid='{TrayTestId(undatedIdea)}']"));
        });

        pane.Find("[data-testid='type-filter-option'][data-type='prompt']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Null(host.State.SelectedType);
            Assert.NotEmpty(pane.FindAll($"[data-testid='{ChipTestId(datedTask)}']"));
            Assert.NotEmpty(pane.FindAll($"[data-testid='{TrayTestId(undatedIdea)}']"));
        });
    }

    /// <summary>The choice is the shared filter bar's, so it is still there after
    /// switching to the list and back, as the other filters are.</summary>
    [Fact]
    public async Task The_type_filter_is_kept_across_a_switch_of_view()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var prompt = await host.WriteEntryAsync($"# A prompt\n`prompt` `due:{Iso(Today)}`\n");
        var task = await host.WriteEntryAsync($"# A task\n`task` `due:{Iso(Today)}`\n");

        var calendar = RenderCalendar(host);
        calendar.Find("[data-testid='type-filter-option'][data-type='prompt']").Click();

        // Over to the Tasks view.
        await host.Context.DisposeComponentsAsync();
        var tasks = host.Context.Render<TasksSurface>();
        tasks.WaitForAssertion(() =>
        {
            Assert.Contains(prompt, host.State.FilteredRows);
            Assert.DoesNotContain(task, host.State.FilteredRows);
            Assert.Equal("true", tasks.Find("[data-testid='type-filter-option'][data-type='prompt']").GetAttribute("aria-pressed"));
        });

        // And back to the Calendar.
        await host.Context.DisposeComponentsAsync();
        calendar = host.RenderCalendarSurface();
        calendar.WaitForAssertion(() =>
        {
            Assert.NotEmpty(calendar.FindAll($"[data-testid='{ChipTestId(prompt)}']"));
            Assert.Empty(calendar.FindAll($"[data-testid='{ChipTestId(task)}']"));
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
}
