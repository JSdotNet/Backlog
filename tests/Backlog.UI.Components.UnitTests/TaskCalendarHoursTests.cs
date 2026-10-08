using System.Globalization;
using Microsoft.AspNetCore.Components.Web;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Planned hours on the month calendar: the blocks drawn as chips of their own, each
/// day's total against its capacity and the mark for a day holding more, and the
/// editor that makes, changes and removes a block — from "Add hours…", from a block's
/// chip, and from a drop with Shift held. The calendar writes nothing; every test reads
/// what it reported through <c>OnHoursChanged</c>.
/// <para>
/// The culture is pinned to en-US, so the figures read "3h" and the decimal point is a
/// point.
/// </para>
/// </summary>
public sealed class TaskCalendarHoursTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 6);
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateOnly Saturday = new(2026, 10, 17);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public TaskCalendarHoursTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    private static readonly IReadOnlyList<CalendarTask> Tasks =
    [
        new("sync", "Refactor sync", Monday, CalendarTaskState.Ready),
        new("docs", "Write the docs", null, CalendarTaskState.Draft),
        new("old", "Shipped already", Monday, CalendarTaskState.Done)
    ];

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Eight hours a weekday and none at the weekend, as a roadmap's default
    /// week reads.</summary>
    private static decimal? Weekdays(DateOnly day) =>
        day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0m : 8m;

    private static IRenderedComponent<TaskCalendar> Render(
        BunitContext context,
        IReadOnlyList<CalendarHoursBlock> blocks,
        List<CalendarHoursChange>? changes = null,
        Action<ComponentParameterCollectionBuilder<TaskCalendar>>? configure = null,
        bool hoursAvailable = true,
        bool withCapacity = true)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<TaskCalendar>(parameters =>
        {
            parameters
                .Add(c => c.Tasks, Tasks)
                .Add(c => c.Today, Today)
                .Add(c => c.TestId, "cal")
                .Add(c => c.HoursAvailable, hoursAvailable)
                .Add(c => c.HoursBlocks, blocks)
                .Add(c => c.CapacityOn, withCapacity ? Weekdays : null)
                .Add(c => c.OnHoursChanged, (CalendarHoursChange change) => changes?.Add(change));
            configure?.Invoke(parameters);
        });
    }

    [Fact]
    public void A_block_is_a_chip_of_its_own_with_its_hours_apart_from_the_due_date_chip()
    {
        using var context = new BunitContext();

        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m, "repo-mark repo-mark--2")]);

        var block = calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']");
        Assert.Equal("Refactor sync · 3h", string.Join(' ', block.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Contains("task-calendar__block", block.ClassName);
        Assert.DoesNotContain("task-calendar__chip", block.ClassName);
        Assert.Contains("repo-mark--2", block.ClassName);
        Assert.Equal("Refactor sync, 3h planned", block.GetAttribute("title"));
        // A block is no due date: it is not a drag handle for one.
        Assert.False(block.HasAttribute("data-calendar-task"));

        // The task's due-date chip is still there, the solid one.
        Assert.Contains("task-calendar__chip", calendar.Find("[data-testid='cal-chip-sync']").ClassName);
    }

    [Fact]
    public void A_day_totals_its_blocks_against_its_capacity()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
        [
            new("sync", "Refactor sync", Monday, 3m),
            new("docs", "Write the docs", Monday, 2.5m)
        ]);

        var total = calendar.Find($"[data-testid='cal-day-{Iso(Monday)}-hours']");
        Assert.Equal("5.5h / 8h", total.QuerySelector("[aria-hidden='true']")!.TextContent.Trim());
        Assert.Equal("5.5h planned of 8h working hours", total.GetAttribute("title"));
        Assert.DoesNotContain("task-calendar__hours--over", total.ClassName);
        Assert.DoesNotContain("task-calendar__day--over", calendar.Find($"[data-calendar-day='{Iso(Monday)}']").ClassName);

        // A day with no blocks says nothing.
        Assert.Empty(calendar.FindAll($"[data-testid='cal-day-{Iso(Monday.AddDays(1))}-hours']"));
    }

    [Fact]
    public void A_day_over_its_capacity_is_marked_with_the_numbers_in_its_tooltip()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
        [
            new("sync", "Refactor sync", Monday, 6m),
            new("docs", "Write the docs", Monday, 4m),
            new("sync", "Refactor sync", Saturday, 2m)
        ]);

        var over = calendar.Find($"[data-testid='cal-day-{Iso(Monday)}-hours']");
        Assert.Equal("10h / 8h", over.QuerySelector("[aria-hidden='true']")!.TextContent.Trim());
        // The numbers are read out, not only hovered.
        Assert.Equal("10h planned, 8h of working hours — 2h over", over.QuerySelector(".sr-only")!.TextContent.Trim());
        Assert.Contains("task-calendar__hours--over", over.ClassName);
        Assert.Equal("10h planned, 8h of working hours — 2h over", over.GetAttribute("title"));
        Assert.Contains("task-calendar__day--over", calendar.Find($"[data-calendar-day='{Iso(Monday)}']").ClassName);

        // A day with no working hours at all is over by every hour planned on it.
        var weekend = calendar.Find($"[data-testid='cal-day-{Iso(Saturday)}-hours']");
        Assert.Contains("task-calendar__hours--over", weekend.ClassName);
        Assert.Equal("2h planned on a day with no working hours — 2h over", weekend.GetAttribute("title"));
    }

    [Fact]
    public void Without_a_capacity_the_total_stands_alone_and_is_never_over()
    {
        using var context = new BunitContext();

        var calendar = Render(context, [new("sync", "Refactor sync", Saturday, 30m)], withCapacity: false);

        var total = calendar.Find($"[data-testid='cal-day-{Iso(Saturday)}-hours']");
        Assert.Equal("30h", total.QuerySelector("[aria-hidden='true']")!.TextContent.Trim());
        Assert.DoesNotContain("task-calendar__hours--over", total.ClassName);
    }

    [Fact]
    public void A_host_that_keeps_no_hours_shows_no_blocks_no_total_and_no_add()
    {
        using var context = new BunitContext();

        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)], hoursAvailable: false);

        Assert.Empty(calendar.FindAll(".task-calendar__block"));
        Assert.Empty(calendar.FindAll(".task-calendar__hours"));
        Assert.Empty(calendar.FindAll(".task-calendar__add-hours"));
    }

    [Fact]
    public void Add_hours_on_a_day_reports_the_picked_task_and_its_hours()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var calendar = Render(context, [], changes);

        calendar.Find($"[data-testid='cal-day-{Iso(Monday)}-add-hours']").Click();

        var editor = calendar.Find("[data-testid='cal-hours-editor']");
        Assert.NotNull(editor);
        // The open tasks in view are offered, dated or not; a finished one is not.
        var options = calendar.FindAll("[data-testid='cal-hours-editor-pick'] option").Select(option => option.GetAttribute("value")).ToList();
        Assert.Contains("sync", options);
        Assert.Contains("docs", options);
        Assert.DoesNotContain("old", options);

        calendar.Find("[data-testid='cal-hours-editor-pick'] select").Change("docs");
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").Input("2.5");
        calendar.Find("[data-testid='cal-hours-editor-save']").Click();

        Assert.Equal([new CalendarHoursChange("docs", Monday, 2.5m)], changes);
        Assert.Empty(calendar.FindAll("[data-testid='cal-hours-editor']"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("25")]
    [InlineData("lots")]
    public void Hours_that_are_no_number_to_plan_are_said_and_not_reported(string typed)
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var calendar = Render(context, [], changes);

        calendar.Find($"[data-testid='cal-day-{Iso(Monday)}-add-hours']").Click();
        calendar.Find("[data-testid='cal-hours-editor-pick'] select").Change("sync");
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").Input(typed);
        calendar.Find("[data-testid='cal-hours-editor-save']").Click();

        Assert.Empty(changes);
        Assert.Contains("more than 0 and at most 24", calendar.Find("[data-testid='cal-hours-editor-error']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_without_a_task_picked_asks_for_one()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var calendar = Render(context, [], changes);

        calendar.Find($"[data-testid='cal-day-{Iso(Monday)}-add-hours']").Click();
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").Input("2");
        calendar.Find("[data-testid='cal-hours-editor-save']").Click();

        Assert.Empty(changes);
        Assert.Contains("Pick the task", calendar.Find("[data-testid='cal-hours-editor-error']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blocks_chip_opens_its_editor_to_change_the_hours()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)], changes);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();

        Assert.Equal("Refactor sync", calendar.Find("[data-testid='cal-hours-editor-task']").TextContent.Trim());
        Assert.Equal("3", calendar.Find("[data-testid='cal-hours-editor-hours'] input").GetAttribute("value"));

        calendar.Find("[data-testid='cal-hours-editor-hours'] input").Input("4.5");
        calendar.Find("[data-testid='cal-hours-editor-save']").Click();

        Assert.Equal([new CalendarHoursChange("sync", Monday, 4.5m)], changes);
    }

    [Fact]
    public void A_blocks_chip_opens_its_editor_to_remove_it()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)], changes);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        calendar.Find("[data-testid='cal-hours-editor-remove']").Click();

        Assert.Equal([new CalendarHoursChange("sync", Monday, null)], changes);
    }

    [Fact]
    public void Escape_closes_the_editor_and_reports_nothing()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)], changes);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        calendar.Find("[data-testid='cal-hours-editor']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(calendar.FindAll("[data-testid='cal-hours-editor']"));
        Assert.Empty(changes);
    }

    /// <summary>Picking a task that already has hours on the day turns the editor into
    /// that block's, so one day never holds two blocks for one task.</summary>
    [Fact]
    public void Picking_a_task_already_planned_on_the_day_edits_its_block()
    {
        using var context = new BunitContext();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)]);

        calendar.Find($"[data-testid='cal-day-{Iso(Monday)}-add-hours']").Click();
        calendar.Find("[data-testid='cal-hours-editor-pick'] select").Change("sync");

        Assert.Equal("Refactor sync", calendar.Find("[data-testid='cal-hours-editor-task']").TextContent.Trim());
        Assert.Equal("3", calendar.Find("[data-testid='cal-hours-editor-hours'] input").GetAttribute("value"));
        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-hours-editor-remove']"));
    }

    /// <summary>A drop with Shift held — a chip or a tray item — opens the day's editor
    /// for that task instead of moving its due date.</summary>
    [Fact]
    public async Task A_shift_drop_opens_the_days_editor_for_that_task_and_moves_no_due_date()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var dueChanges = new List<CalendarDueChange>();
        var calendar = Render(context, [], changes, p => p.Add(c => c.OnDueChanged, (CalendarDueChange change) => dueChanges.Add(change)));
        var day = Monday.AddDays(2);

        await calendar.InvokeAsync(() => calendar.Instance.HoldOnDay("docs", Iso(day)));

        var editor = calendar.Find($"[data-calendar-day='{Iso(day)}'] [data-testid='cal-hours-editor']");
        Assert.Equal("docs", editor.QuerySelector("select")!.GetAttribute("value"));

        calendar.Find("[data-testid='cal-hours-editor-hours'] input").Input("2");
        calendar.Find("[data-testid='cal-hours-editor-save']").Click();

        Assert.Equal([new CalendarHoursChange("docs", day, 2m)], changes);
        Assert.Empty(dueChanges);
    }

    [Fact]
    public async Task A_shift_drop_on_a_day_the_task_already_has_hours_on_edits_that_block()
    {
        using var context = new BunitContext();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)]);

        await calendar.InvokeAsync(() => calendar.Instance.HoldOnDay("sync", Iso(Monday)));

        Assert.Equal("3", calendar.Find("[data-testid='cal-hours-editor-hours'] input").GetAttribute("value"));
        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-hours-editor-remove']"));
    }

    [Fact]
    public async Task A_shift_drop_is_an_ordinary_drop_where_the_host_keeps_no_hours()
    {
        using var context = new BunitContext();
        var dueChanges = new List<CalendarDueChange>();
        var calendar = Render(context, [], configure: p => p.Add(c => c.OnDueChanged, (CalendarDueChange change) => dueChanges.Add(change)), hoursAvailable: false);

        await calendar.InvokeAsync(() => calendar.Instance.HoldOnDay("sync", Iso(Monday.AddDays(1))));

        Assert.Empty(calendar.FindAll("[data-testid='cal-hours-editor']"));
        Assert.Equal([new CalendarDueChange("sync", Monday.AddDays(1))], dueChanges);
    }

    /// <summary>Enter saves from the hours field only: on Remove or Cancel it is that
    /// button's press, never a save first.</summary>
    [Fact]
    public void Enter_saves_from_the_hours_field_and_not_from_the_other_buttons()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)], changes);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").Input("4");
        calendar.Find("[data-testid='cal-hours-editor-remove']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Empty(changes);

        calendar.Find("[data-testid='cal-hours-editor-hours'] input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal([new CalendarHoursChange("sync", Monday, 4m)], changes);
    }

    /// <summary>A block taken away elsewhere closes its open editor, so Save cannot put it
    /// back.</summary>
    [Fact]
    public void An_editor_whose_block_was_removed_elsewhere_closes()
    {
        using var context = new BunitContext();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)]);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-hours-editor']"));

        calendar.Render(p => p.Add(c => c.HoursBlocks, Array.Empty<CalendarHoursBlock>()));

        Assert.Empty(calendar.FindAll("[data-testid='cal-hours-editor']"));
    }
}

/// <summary>
/// The detail panel's planned hours for one task: a line a day to change or remove,
/// and a line to add another day.
/// </summary>
public sealed class PlannedHoursEditorTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public PlannedHoursEditorTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private static IRenderedComponent<PlannedHoursEditor> Render(BunitContext context, IReadOnlyList<PlannedHoursDay> blocks, List<PlannedHoursDayChange> changes) =>
        context.Render<PlannedHoursEditor>(parameters => parameters
            .Add(c => c.Blocks, blocks)
            .Add(c => c.DefaultDay, Monday)
            .Add(c => c.TestId, "ph")
            .Add(c => c.OnChanged, (PlannedHoursDayChange change) => changes.Add(change)));

    [Fact]
    public void Each_day_is_a_line_with_its_hours()
    {
        using var context = new BunitContext();

        var editor = Render(context, [new(Monday, 3m), new(Monday.AddDays(1), 1.5m)], []);

        Assert.Equal(["2026-10-12", "2026-10-13"], editor.FindAll("[data-testid='ph-row']").Select(row => row.GetAttribute("data-day")));
        Assert.Equal("1.5", editor.Find("[data-testid='ph-hours-2026-10-13'] input").GetAttribute("value"));
    }

    [Fact]
    public void A_changed_figure_is_reported_on_change()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [new(Monday, 3m)], changes);

        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Change("4");

        Assert.Equal([new PlannedHoursDayChange(Monday, 4m)], changes);
    }

    [Fact]
    public void A_figure_that_is_no_number_of_hours_is_said_and_not_reported()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [new(Monday, 3m)], changes);

        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Change("0");

        Assert.Empty(changes);
        Assert.Contains("more than 0", editor.Find("[data-testid='ph-error']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_reports_the_day_with_no_hours()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [new(Monday, 3m)], changes);

        editor.Find("[data-testid='ph-remove-2026-10-12']").Click();

        Assert.Equal([new PlannedHoursDayChange(Monday, null)], changes);
    }

    [Fact]
    public void Add_reports_the_day_picked_and_its_hours()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [], changes);

        Assert.Equal("2026-10-12", editor.Find("[data-testid='ph-add-day'] input").GetAttribute("value"));
        editor.Find("[data-testid='ph-add-day'] input").Input("2026-10-14");
        editor.Find("[data-testid='ph-add-hours'] input").Input("2");
        editor.Find("[data-testid='ph-add']").Click();

        Assert.Equal([new PlannedHoursDayChange(new DateOnly(2026, 10, 14), 2m)], changes);
    }
}

/// <summary>The hours arithmetic and wording, apart from the markup.</summary>
public sealed class CalendarHoursTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    /// <summary>A number field hands its value over with a point whatever the culture, so
    /// "1.5" is one and a half in a culture that groups thousands with a point too.</summary>
    [Theory]
    [InlineData("nl-NL", "1.5", 1.5)]
    [InlineData("nl-NL", "2,5", 2.5)]
    [InlineData("en-US", "2.5h", 2.5)]
    [InlineData("en-US", "24", 24)]
    public void A_typed_figure_is_read_with_a_point_first_and_never_grouped(string culture, string typed, double hours)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        Assert.Equal((decimal)hours, CalendarHours.Parse(typed));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("24.25")]
    [InlineData("1,000")]
    [InlineData("0.1")]
    [InlineData("")]
    public void A_figure_that_is_no_number_of_hours_reads_as_none(string typed)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        Assert.Null(CalendarHours.Parse(typed));
    }

    [Fact]
    public void The_field_starts_from_an_invariant_figure()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");

        Assert.Equal("2.5", CalendarHours.Edit(2.5m));
    }
}
