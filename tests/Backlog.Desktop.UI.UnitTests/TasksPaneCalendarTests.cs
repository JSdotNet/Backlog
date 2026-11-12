using System.Globalization;
using System.Reflection;
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

        var pane = RenderCalendar(host);
        pane.Find("[data-testid='type-filter-option'][data-type='prompt']").Click();

        pane.Render(parameters => parameters.Add(p => p.Layout, TasksLayout.List));
        pane.WaitForAssertion(() =>
        {
            Assert.Contains(prompt, host.State.FilteredRows);
            Assert.DoesNotContain(task, host.State.FilteredRows);
            Assert.Equal("true", pane.Find("[data-testid='type-filter-option'][data-type='prompt']").GetAttribute("aria-pressed"));
        });

        pane.Render(parameters => parameters.Add(p => p.Layout, TasksLayout.Calendar));
        pane.WaitForAssertion(() =>
        {
            Assert.NotEmpty(pane.FindAll($"[data-testid='{ChipTestId(prompt)}']"));
            Assert.Empty(pane.FindAll($"[data-testid='{ChipTestId(task)}']"));
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

    // --- Knowledge events ----------------------------------------------------

    private const string EventTestId = "task-calendar-event-e0";

    private const string KeyDateTestId = "task-calendar-key-date-k0";

    [Fact]
    public async Task Without_an_events_port_the_calendar_offers_no_events()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var pane = RenderCalendar(host);

        Assert.Empty(pane.FindAll("[data-testid='task-calendar-show-events']"));
        Assert.Empty(pane.FindAll(".task-calendar__event"));
    }

    /// <summary>The events area switched off answers Off: no box, and nothing drawn,
    /// even on a device where the box was ticked before.</summary>
    [Fact]
    public async Task With_the_events_area_off_there_is_no_box_and_nothing_drawn_whatever_was_ticked()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var events = new FakeCalendarEvents(Today) { Available = false };
        events.SetShown(true);
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var pane = RenderCalendar(host);

        pane.WaitForAssertion(() => Assert.Equal(1, events.Reads));
        Assert.Empty(pane.FindAll("[data-testid='task-calendar-show-events']"));
        Assert.Empty(pane.FindAll($"[data-testid='{EventTestId}']"));
        Assert.Empty(pane.FindAll($"[data-testid='{KeyDateTestId}']"));
    }

    [Fact]
    public async Task The_events_and_their_key_dates_are_drawn_from_the_port_once_the_box_is_ticked()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var events = new FakeCalendarEvents(Today);
        events.SetShown(true);
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var pane = RenderCalendar(host);

        pane.WaitForAssertion(() =>
        {
            var bar = pane.FindAll($"[data-testid='{EventTestId}']")[0];
            Assert.Equal("Techorama · Going · Antwerp", bar.GetAttribute("title"));
            Assert.Contains("task-calendar__event--filled", bar.ClassName);
            Assert.False(bar.HasAttribute("data-calendar-task"));
            Assert.Contains("task-calendar__event--tentative", pane.FindAll("[data-testid='task-calendar-event-e1']")[0].ClassName);
            var keyDate = pane.Find($"[data-calendar-day='{Iso(Today)}'] [data-testid='{KeyDateTestId}']");
            Assert.Equal("Early bird ends", keyDate.QuerySelector(".task-calendar__milestone-text")!.TextContent);
        });
        Assert.Equal(1, events.Reads);
    }

    /// <summary>The events are read on arriving at the Calendar and on a feature change —
    /// which is how the box comes and goes as the events area is switched — not on every
    /// render.</summary>
    [Fact]
    public async Task The_events_are_read_again_on_returning_to_the_calendar_and_on_a_feature_change()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var events = new FakeCalendarEvents(Today) { Available = false };
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var pane = RenderCalendar(host);
        pane.WaitForAssertion(() => Assert.Equal(1, events.Reads));
        pane.Render();
        Assert.Equal(1, events.Reads);
        Assert.Empty(pane.FindAll("[data-testid='task-calendar-show-events']"));

        pane.Render(parameters => parameters.Add(p => p.Layout, TasksLayout.List));
        pane.Render(parameters => parameters.Add(p => p.Layout, TasksLayout.Calendar));
        pane.WaitForAssertion(() => Assert.Equal(2, events.Reads));

        events.Available = true;
        host.Features.SetEnabled("events", true);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(3, events.Reads);
            Assert.NotEmpty(pane.FindAll("[data-testid='task-calendar-show-events']"));
        });
    }

    /// <summary>On a device that never chose, the box is unticked and nothing is drawn; a
    /// tick goes through the port, is kept by the store, and survives a restart.</summary>
    [Fact]
    public async Task On_a_device_that_never_chose_the_events_are_off_until_ticked_and_the_tick_is_kept()
    {
        using var settings = new TempShellNavigationFile();
        using var host = await TasksPaneHost.CreateAsync();
        var events = new FakeCalendarEvents(Today, new ShellNavigationStore(settings.Path));
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var pane = RenderCalendar(host);

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='task-calendar-show-events']")));
        Assert.False(pane.Find("[data-testid='task-calendar-show-events'] input").HasAttribute("checked"));
        Assert.Empty(pane.FindAll($"[data-testid='{EventTestId}']"));
        Assert.Empty(pane.FindAll($"[data-testid='{KeyDateTestId}']"));

        pane.Find("[data-testid='task-calendar-show-events'] input").Change(true);

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll($"[data-testid='{EventTestId}']")));
        Assert.True(events.Shown);
        Assert.True(new ShellNavigationStore(settings.Path).CalendarEventsShown);

        pane.Find("[data-testid='task-calendar-show-events'] input").Change(false);

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll($"[data-testid='{EventTestId}']")));
        Assert.False(new ShellNavigationStore(settings.Path).CalendarEventsShown);
    }

    /// <summary>Until the first read lands the pane does not know whether the events area
    /// is on, so it offers no box: a box drawn in that gap could be ticked while the area
    /// is off, and the tick would be kept.</summary>
    [Fact]
    public async Task No_show_events_box_is_offered_before_the_events_read_lands()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var gate = new TaskCompletionSource();
        var events = new FakeCalendarEvents(Today) { Gate = gate };
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var pane = RenderCalendar(host);

        pane.WaitForAssertion(() => Assert.Equal(1, events.Reads));
        Assert.NotEmpty(pane.FindAll("[data-testid='task-calendar'] [data-calendar-day]"));
        Assert.Empty(pane.FindAll("[data-testid='task-calendar-show-events']"));

        gate.SetResult();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='task-calendar-show-events']")));
    }

    /// <summary>A read made on an earlier day is read again on the next render, so the
    /// Calendar left open overnight draws the events as they stand today. The pane reads
    /// its own wall clock, so the test dates the last read back a day rather than moving
    /// the clock.</summary>
    [Fact]
    public async Task The_events_are_read_again_once_the_day_has_moved_on()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var events = new FakeCalendarEvents(Today);
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var pane = RenderCalendar(host);
        pane.WaitForAssertion(() => Assert.Equal(1, events.Reads));
        pane.Render();
        Assert.Equal(1, events.Reads);

        var readOn = typeof(TasksPane).GetField("_calendarEventsReadOn", BindingFlags.Instance | BindingFlags.NonPublic)!;
        readOn.SetValue(pane.Instance, (DateOnly?)Today.AddDays(-1));
        pane.Render();

        pane.WaitForAssertion(() => Assert.Equal(2, events.Reads));
        Assert.Equal((DateOnly?)Today, (DateOnly?)readOn.GetValue(pane.Instance));
    }

    [Fact]
    public async Task An_events_read_that_fails_leaves_the_tasks_on_the_calendar()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var events = new FakeCalendarEvents(Today) { Fails = true };
        events.SetShown(true);
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var pane = RenderCalendar(host);

        pane.WaitForAssertion(() => Assert.Equal(1, events.Reads));
        Assert.NotEmpty(pane.FindAll("[data-testid='task-calendar'] [data-calendar-day]"));
        Assert.Empty(pane.FindAll(".task-calendar__event"));
    }
}
