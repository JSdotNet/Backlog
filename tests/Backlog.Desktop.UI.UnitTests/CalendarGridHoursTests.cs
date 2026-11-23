using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.UI;
using Backlog.UI.Components.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.UnitTests;

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
public sealed class CalendarGridHoursTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 6);
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateOnly Saturday = new(2026, 10, 17);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CalendarGridHoursTests()
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
        new("docs", "Write the docs", new DateOnly(2026, 10, 20), CalendarTaskState.Draft),
        new("old", "Shipped already", Monday, CalendarTaskState.Done)
    ];

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Eight hours a weekday and none at the weekend, as a roadmap's default
    /// week reads.</summary>
    private static decimal? Weekdays(DateOnly day) =>
        day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0m : 8m;

    private static IRenderedComponent<CalendarGrid> Render(
        BunitContext context,
        IReadOnlyList<CalendarHoursBlock> blocks,
        List<CalendarHoursChange>? changes = null,
        Action<ComponentParameterCollectionBuilder<CalendarGrid>>? configure = null,
        bool hoursAvailable = true,
        bool withCapacity = true)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<CalendarGrid>(parameters =>
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

    /// <summary>A drop of a chip with Shift held opens the day's editor
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

    [Fact]
    public void The_hours_field_suggests_the_presets_and_taking_one_fills_the_figure_without_saving()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)], changes);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        var field = calendar.Find("[data-testid='cal-hours-editor-hours'] input");
        Assert.Equal("combobox", field.GetAttribute("role"));

        field.KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        Assert.Equal(
            ["0.5h", "1h", "2h", "3h", "4h", "6h", "8h"],
            calendar.FindAll("[data-testid='cal-hours-editor-hours'] [role='option']").Select(option => option.TextContent));

        calendar.Find("[data-testid='cal-hours-editor-hours-option-6']").Click();

        Assert.Equal("8", calendar.Find("[data-testid='cal-hours-editor-hours'] input").GetAttribute("value"));
        Assert.Empty(changes);

        calendar.Find("[data-testid='cal-hours-editor-save']").Click();
        Assert.Equal([new CalendarHoursChange("sync", Monday, 8m)], changes);
    }

    /// <summary>Enter on a highlighted suggestion takes it and only that: the editor
    /// stays open, and the next Enter saves.</summary>
    [Fact]
    public void Enter_on_a_suggestion_takes_it_and_does_not_also_save()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)], changes);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        // Down opens on the figure held (3h); one more is 4h.
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Empty(changes);
        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-hours-editor']"));
        Assert.Equal("4", calendar.Find("[data-testid='cal-hours-editor-hours'] input").GetAttribute("value"));

        calendar.Find("[data-testid='cal-hours-editor-hours'] input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal([new CalendarHoursChange("sync", Monday, 4m)], changes);
    }

    /// <summary>The Escape that closes the suggestions is the field's; the editor stays
    /// open until the next one.</summary>
    [Fact]
    public void Escape_closes_the_open_suggestions_first_and_the_editor_on_the_next()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarHoursChange>();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)], changes);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-hours-editor']"));
        Assert.Empty(calendar.FindAll("[data-testid='cal-hours-editor-hours'] [role='listbox']"));

        calendar.Find("[data-testid='cal-hours-editor-hours'] input").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(calendar.FindAll("[data-testid='cal-hours-editor']"));
        Assert.Empty(changes);
    }

    /// <summary>Review S2 round 1: the grid clips its content to round its corners, and
    /// cut the suggestions off below a day in the bottom row. The open list marks its
    /// wrapper, inside the grid, and the stylesheet lifts the clip on that mark.</summary>
    [Fact]
    public void An_open_list_of_suggestions_lifts_the_grids_clip()
    {
        using var context = new BunitContext();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)]);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.NotNull(calendar.Find("[data-testid='cal-grid'] .combobox--open [role='listbox']"));

        var css = File.ReadAllText(RepositoryRoot.File(
            "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n");
        var start = css.IndexOf("\n.task-calendar__grid:has(.combobox--open) {", StringComparison.Ordinal);
        Assert.True(start >= 0, "components.css does not lift the grid's clip while a combobox list is open.");
        Assert.Matches(@"overflow:\s*visible", css[start..css.IndexOf('}', start)]);
    }

    private static string? LastFocused(BunitContext context) =>
        context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "backlogFocus")
            .Select(invocation => invocation.Arguments[0] as string)
            .LastOrDefault();

    [Fact]
    public void Saving_a_blocks_editor_sends_the_focus_back_to_its_chip()
    {
        using var context = new BunitContext();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)]);
        var chipId = calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Id;
        Assert.False(string.IsNullOrEmpty(chipId));

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").Input("4");
        calendar.Find("[data-testid='cal-hours-editor-save']").Click();

        Assert.Equal(chipId, LastFocused(context));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("escape")]
    public void Closing_a_blocks_editor_without_saving_sends_the_focus_back_to_its_chip(string how)
    {
        using var context = new BunitContext();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)]);
        var chipId = calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Id;

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        if (how == "cancel") calendar.Find("[data-testid='cal-hours-editor-cancel']").Click();
        else calendar.Find("[data-testid='cal-hours-editor']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(chipId, LastFocused(context));
    }

    /// <summary>Remove takes the chip away, so the focus lands on the day's "Add hours…".</summary>
    [Fact]
    public void Removing_a_block_sends_the_focus_to_the_days_add_hours()
    {
        using var context = new BunitContext();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)]);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        calendar.Find("[data-testid='cal-hours-editor-remove']").Click();

        var addHours = calendar.Find($"[data-testid='cal-day-{Iso(Monday)}-add-hours']");
        Assert.False(string.IsNullOrEmpty(addHours.Id));
        Assert.Equal(addHours.Id, LastFocused(context));
    }

    [Fact]
    public void Adding_hours_sends_the_focus_back_to_the_days_add_hours()
    {
        using var context = new BunitContext();
        var calendar = Render(context, []);

        calendar.Find($"[data-testid='cal-day-{Iso(Monday)}-add-hours']").Click();
        calendar.Find("[data-testid='cal-hours-editor-pick'] select").Change("docs");
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").Input("2");
        calendar.Find("[data-testid='cal-hours-editor-save']").Click();

        Assert.Equal(calendar.Find($"[data-testid='cal-day-{Iso(Monday)}-add-hours']").Id, LastFocused(context));
    }

    /// <summary>A drag leaves no control to go back to, so a Shift drop's editor closes
    /// onto the day's "Add hours…".</summary>
    [Fact]
    public async Task Closing_a_shift_drops_editor_sends_the_focus_to_the_days_add_hours()
    {
        using var context = new BunitContext();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)]);

        await calendar.InvokeAsync(() => calendar.Instance.HoldOnDay("sync", Iso(Monday)));
        calendar.Find("[data-testid='cal-hours-editor-cancel']").Click();

        Assert.Equal(calendar.Find($"[data-testid='cal-day-{Iso(Monday)}-add-hours']").Id, LastFocused(context));
    }

    /// <summary>A refused figure keeps the editor open, so the focus stays where it is.</summary>
    [Fact]
    public void A_refused_figure_moves_no_focus()
    {
        using var context = new BunitContext();
        var calendar = Render(context, [new("sync", "Refactor sync", Monday, 3m)]);

        calendar.Find($"[data-testid='cal-block-sync-{Iso(Monday)}']").Click();
        var fieldId = calendar.Find("[data-testid='cal-hours-editor-hours'] input").Id;
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").Input("0");
        calendar.Find("[data-testid='cal-hours-editor-save']").Click();

        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-hours-editor-error']"));
        Assert.Equal(fieldId, LastFocused(context));
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

    /// <summary>A block may belong to a task the grid holds no chip for — one with no
    /// due date, or due outside the window — and its open editor survives the host's
    /// redraw: only a draft for a new block needs its task among <c>Tasks</c>.</summary>
    [Fact]
    public void An_open_editor_for_a_block_of_a_task_not_shown_survives_a_redraw()
    {
        using var context = new BunitContext();
        var calendar = Render(context, [new("undated", "Plan the offsite", Monday, 3m)]);

        calendar.Find($"[data-testid='cal-block-undated-{Iso(Monday)}']").Click();
        calendar.Find("[data-testid='cal-hours-editor-hours'] input").Input("4");

        calendar.Render(p => p.Add(c => c.Tasks, Tasks.ToList()));

        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-hours-editor']"));
        Assert.Equal("4", calendar.Find("[data-testid='cal-hours-editor-hours'] input").GetAttribute("value"));
    }
}
