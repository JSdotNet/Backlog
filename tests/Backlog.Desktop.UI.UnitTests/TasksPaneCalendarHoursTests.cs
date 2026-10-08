using System.Globalization;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Planned hours through the Tasks pane, over a real store in a temporary folder: a
/// block made, changed and removed from a Calendar day and from the detail panel's
/// Planned hours section, each written to the entry as its own field and read back
/// from the store; the day's total against the capacity Tasks' port answers, and the
/// mark for a day over it; and the filter bar and the repository colours, which the
/// blocks follow as the chips do.
/// <para>
/// Dates are relative to the real today, because the pane reads its own clock, and
/// chosen inside today's month so they are on the grid whatever the date.
/// </para>
/// </summary>
public sealed class TasksPaneCalendarHoursTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Another day of today's month.</summary>
    private static DateOnly Day => Today.Day <= 15 ? Today.AddDays(3) : Today.AddDays(-3);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IRenderedComponent<TasksPane> RenderCalendar(TasksPaneHost host) =>
        host.Context.Render<TasksPane>(parameters => parameters.Add(pane => pane.Layout, TasksLayout.Calendar));

    private static string BlockTestId(EntryRow row, DateOnly day) => $"task-calendar-block-{row.TaskId}-{Iso(day)}";

    private static async Task<IReadOnlyList<PlannedHoursDto>> StoredAsync(TasksPaneHost host, EntryRow row)
    {
        await host.State.ReloadFromStoreAsync();
        return Assert.Single(host.State.Rows, candidate => candidate.Id == row.Id).PlannedHours;
    }

    [Fact]
    public async Task Add_hours_on_a_calendar_day_writes_a_block_to_the_entry_and_draws_it()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Refactor sync\n`task` `!ready`\n");
        var text = row.RawText;
        var pane = RenderCalendar(host);

        pane.Find($"[data-testid='task-calendar-day-{Iso(Day)}-add-hours']").Click();
        pane.Find("[data-testid='task-calendar-hours-editor-pick'] select").Change(row.TaskId);
        pane.Find("[data-testid='task-calendar-hours-editor-hours'] input").Input("3");
        pane.Find("[data-testid='task-calendar-hours-editor-save']").Click();

        pane.WaitForAssertion(() =>
        {
            var block = pane.Find($"[data-testid='{BlockTestId(row, Day)}']");
            Assert.Contains("Refactor sync", block.TextContent, StringComparison.Ordinal);
            Assert.Contains("3h", block.TextContent, StringComparison.Ordinal);
        });

        // Its own field, not text: the entry's markdown is as it was.
        Assert.Equal(text, row.RawText);
        Assert.Equal([new PlannedHoursDto(Day, 3m)], await StoredAsync(host, row));
    }

    [Fact]
    public async Task A_blocks_chip_changes_and_removes_its_hours()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Refactor sync\n`task` `!ready`\n");
        await host.State.SetPlannedHoursAsync(row, Day, 3m);
        var pane = RenderCalendar(host);

        pane.Find($"[data-testid='{BlockTestId(row, Day)}']").Click();
        pane.Find("[data-testid='task-calendar-hours-editor-hours'] input").Input("5");
        pane.Find("[data-testid='task-calendar-hours-editor-save']").Click();

        pane.WaitForAssertion(() => Assert.Contains("5h", pane.Find($"[data-testid='{BlockTestId(row, Day)}']").TextContent, StringComparison.Ordinal));
        Assert.Equal([new PlannedHoursDto(Day, 5m)], await StoredAsync(host, row));

        pane.Render();
        pane.Find($"[data-testid='{BlockTestId(row, Day)}']").Click();
        pane.Find("[data-testid='task-calendar-hours-editor-remove']").Click();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll(".task-calendar__block")));
        Assert.Empty(await StoredAsync(host, row));
    }

    /// <summary>A Shift drop of a tray entry onto a day opens that day's editor for it and
    /// leaves its due date alone.</summary>
    [Fact]
    public async Task A_shift_drop_from_the_tray_plans_hours_and_moves_no_due_date()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var undated = await host.WriteEntryAsync("# Write the docs\n`task` `!ready`\n");
        var pane = RenderCalendar(host);
        var calendar = pane.FindComponent<TaskCalendar>();

        await calendar.InvokeAsync(() => calendar.Instance.HoldOnDay(undated.TaskId, Iso(Day)));
        pane.Find("[data-testid='task-calendar-hours-editor-hours'] input").Input("2");
        pane.Find("[data-testid='task-calendar-hours-editor-save']").Click();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll($"[data-testid='{BlockTestId(undated, Day)}']")));
        Assert.Null(undated.PreviewDueOn);
        Assert.NotEmpty(pane.FindAll($"[data-testid='task-calendar-tray-{undated.TaskId}']"));
        Assert.Equal([new PlannedHoursDto(Day, 2m)], await StoredAsync(host, undated));
    }

    [Fact]
    public async Task The_detail_panels_planned_hours_add_change_and_remove_a_block()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Refactor sync\n`task` `!ready`\n");
        await host.OpenAsync(row);
        var pane = host.Render();

        var section = pane.Find("[data-testid='entry-planned-hours']");
        Assert.Contains("Planned hours", section.TextContent, StringComparison.Ordinal);

        pane.Find("[data-testid='entry-planned-hours-editor-add-day'] input").Input(Iso(Day));
        pane.Find("[data-testid='entry-planned-hours-editor-add-hours'] input").Input("4");
        pane.Find("[data-testid='entry-planned-hours-editor-add']").Click();

        pane.WaitForAssertion(() => Assert.Equal(
            "4",
            pane.Find($"[data-testid='entry-planned-hours-editor-hours-{Iso(Day)}'] input").GetAttribute("value")));
        Assert.Equal([new PlannedHoursDto(Day, 4m)], row.PlannedHours);

        pane.Find($"[data-testid='entry-planned-hours-editor-hours-{Iso(Day)}'] input").Change("1.5");
        pane.WaitForAssertion(() => Assert.Equal([new PlannedHoursDto(Day, 1.5m)], row.PlannedHours));

        pane.Find($"[data-testid='entry-planned-hours-editor-remove-{Iso(Day)}']").Click();
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='entry-planned-hours-editor-row']")));
        Assert.Empty(await StoredAsync(host, row));
    }

    /// <summary>A figure the module refuses is said on the entry and the band reads Error;
    /// nothing is written.</summary>
    [Fact]
    public async Task A_refused_figure_is_said_and_nothing_is_written()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Refactor sync\n`task` `!ready`\n");

        await host.State.SetPlannedHoursAsync(row, Day, 30m);

        Assert.NotNull(row.PlannedHoursError);
        Assert.Contains("at most 24", row.PlannedHoursError, StringComparison.Ordinal);
        Assert.Empty(await StoredAsync(host, row));
    }

    [Fact]
    public async Task A_day_totals_its_blocks_against_the_capacity_the_port_answers_and_marks_a_day_over_it()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var capacity = new FakeCalendarCapacity(8m);
        host.Context.Services.AddSingleton<ICalendarCapacity>(capacity);
        var sync = await host.WriteEntryAsync("# Refactor sync\n`task` `!ready`\n");
        var docs = await host.WriteEntryAsync("# Write the docs\n`task` `!ready`\n");
        await host.State.SetPlannedHoursAsync(sync, Day, 3m);
        await host.State.SetPlannedHoursAsync(docs, Day, 4m);

        var pane = RenderCalendar(host);

        var total = pane.Find($"[data-testid='task-calendar-day-{Iso(Day)}-hours']");
        Assert.Equal("7h / 8h", total.QuerySelector("[aria-hidden='true']")!.TextContent.Trim());
        Assert.DoesNotContain("task-calendar__hours--over", total.ClassName);

        await host.State.SetPlannedHoursAsync(docs, Day, 6m);
        pane.Render();

        pane.WaitForAssertion(() =>
        {
            var over = pane.Find($"[data-testid='task-calendar-day-{Iso(Day)}-hours']");
            Assert.Equal("9h / 8h", over.QuerySelector("[aria-hidden='true']")!.TextContent.Trim());
            Assert.Contains("task-calendar__hours--over", over.ClassName);
            Assert.Contains("1h over", over.GetAttribute("title"), StringComparison.Ordinal);
        });

        // A change to the working week redraws the day against the new figure.
        capacity.Hours = 10m;
        capacity.RaiseChanged();
        pane.WaitForAssertion(() =>
        {
            var day = pane.Find($"[data-testid='task-calendar-day-{Iso(Day)}-hours']");
            Assert.Equal("9h / 10h", day.QuerySelector("[aria-hidden='true']")!.TextContent.Trim());
            Assert.DoesNotContain("task-calendar__hours--over", day.ClassName);
        });
    }

    [Fact]
    public async Task The_filter_bar_narrows_the_blocks_as_it_narrows_the_chips()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var prompt = await host.WriteEntryAsync("# A prompt\n`prompt`\n");
        var task = await host.WriteEntryAsync("# A task\n`task`\n");
        await host.State.SetPlannedHoursAsync(prompt, Day, 2m);
        await host.State.SetPlannedHoursAsync(task, Day, 3m);

        var pane = RenderCalendar(host);
        Assert.NotEmpty(pane.FindAll($"[data-testid='{BlockTestId(task, Day)}']"));

        pane.Find("[data-testid='type-filter-option'][data-type='prompt']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.NotEmpty(pane.FindAll($"[data-testid='{BlockTestId(prompt, Day)}']"));
            Assert.Empty(pane.FindAll($"[data-testid='{BlockTestId(task, Day)}']"));
            Assert.Equal("2h", pane.Find($"[data-testid='task-calendar-day-{Iso(Day)}-hours']").QuerySelector("[aria-hidden='true']")!.TextContent.Trim());
        });
    }

    [Fact]
    public async Task A_blocks_repository_colour_follows_the_visualization_switch()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var row = await host.WriteEntryAsync("# Refactor sync\n`task` `repo:backlog`\n");
        await host.State.SetPlannedHoursAsync(row, Day, 3m);

        var pane = RenderCalendar(host);
        Assert.DoesNotContain("repo-mark", pane.Find($"[data-testid='{BlockTestId(row, Day)}']").ClassName);

        Assert.Null(host.GitHub.Settings.SetShowRepositoryColours(true));
        pane.Render();

        Assert.Contains("repo-mark--1", pane.Find($"[data-testid='{BlockTestId(row, Day)}']").ClassName);
    }

    /// <summary>A working week of the same hours every day, changed by the test.</summary>
    private sealed class FakeCalendarCapacity(decimal hours) : ICalendarCapacity
    {
        public event Action? Changed;

        public decimal Hours { get; set; } = hours;

        public decimal HoursOn(DateOnly day) => Hours;

        public void RaiseChanged() => Changed?.Invoke();
    }
}
