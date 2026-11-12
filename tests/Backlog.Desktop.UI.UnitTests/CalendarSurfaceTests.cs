using System.Globalization;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Calendar.UI;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Calendar as its own module's view (local ADR 0055), rendered with the real Tasks
/// part it hosts: the Plans layer the Calendar reads through its own port and hands the
/// part — the bars, the milestones and the shelf, Show plans and the range it keeps — and
/// when it reads them again.
/// <para>
/// Dates are relative to the real today, because the Calendar and the pane read their own
/// clock the way the list does for My Day.
/// </para>
/// </summary>
public sealed class CalendarSurfaceTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string PlanTestId => $"task-calendar-plan-{FakeCalendarPlans.PlanId:N}";

    private const string EventTestId = "task-calendar-event-e0";

    [Fact]
    public async Task Without_a_roadmap_the_calendar_offers_no_plans()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var surface = host.WithCalendarSurface().RenderCalendarSurface();

        Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar']"));
        Assert.Empty(surface.FindAll("[data-testid='task-calendar-show-plans']"));
        Assert.DoesNotContain("Plans without a window", surface.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_plans_windows_milestones_and_shelf_are_drawn_from_the_port()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var plans = new FakeCalendarPlans(Today);
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();

        surface.WaitForAssertion(() =>
        {
            var bar = surface.FindAll($"[data-testid='{PlanTestId}']")[0];
            Assert.Equal("Calendar plans · +task-views · 3 of 8 pts", bar.GetAttribute("title"));
            Assert.Contains("task-calendar__plan--neutral", bar.ClassName);
            Assert.NotNull(surface.Find($"[data-calendar-day='{Iso(Today)}']").QuerySelector(".task-calendar__milestone"));
            Assert.Equal("release-q4", surface.Find("[data-testid='task-calendar-shelf-release-q4']").GetAttribute("data-calendar-plan"));
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
        host.WithCalendarSurface();

        var surface = host.RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.Equal(1, plans.Reads));
        surface.Render();
        Assert.Equal(1, plans.Reads);

        // Leaving the Calendar and coming back to it.
        await host.Context.DisposeComponentsAsync();
        surface = host.RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.Equal(2, plans.Reads));

        plans.RaiseChanged();
        surface.WaitForAssertion(() => Assert.Equal(3, plans.Reads));
    }

    /// <summary>The roadmap switched off — any feature switched — owes a read, so the
    /// plans and the box follow the switch without leaving the Calendar.</summary>
    [Fact]
    public async Task The_plans_are_read_again_on_a_feature_change()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var plans = new FakeCalendarPlans(Today);
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.Equal(1, plans.Reads));

        host.Features.SetEnabled("events", true);

        surface.WaitForAssertion(() => Assert.Equal(2, plans.Reads));
    }

    /// <summary>The Calendar left open past midnight: a press of its own — the range — draws
    /// the plans as they stand on the new day, not as they stood yesterday. The surface's own
    /// redraw does not pass through its parameters, so the press asks for the read itself.</summary>
    [Fact]
    public async Task A_range_switch_after_the_day_rolled_over_reads_the_plans_again()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.Now);
        var plans = new FakeCalendarPlans(Today);
        host.Context.Services.AddSingleton<TimeProvider>(clock);
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.Equal(1, plans.Reads));

        clock.Advance(TimeSpan.FromDays(1));
        surface.Find("[data-testid='task-calendar-range-week']").Click();

        surface.WaitForAssertion(() => Assert.Equal(2, plans.Reads));
    }

    [Fact]
    public async Task A_plans_read_that_fails_leaves_the_tasks_on_the_calendar()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var dated = await host.WriteEntryAsync($"# Ship the calendar\n`task` `!ready` `due:{Iso(Today)}`\n");
        var plans = new FakeCalendarPlans(Today) { Failure = new IOException("The roadmap could not be read.") };
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.Equal(1, plans.Reads));
        Assert.NotEmpty(surface.FindAll($"[data-calendar-day='{Iso(Today)}'] [data-testid='task-calendar-chip-{dated.TaskId}']"));
        Assert.Empty(surface.FindAll($"[data-testid='{PlanTestId}']"));
    }

    /// <summary>The colours are the Tasks window's, which the Calendar cannot see: the
    /// part gives each bar the band of its first repository while they show.</summary>
    [Fact]
    public async Task A_bar_wears_its_repository_colour_only_while_the_colours_are_showing()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.Contains("task-calendar__plan--neutral", surface.FindAll($"[data-testid='{PlanTestId}']")[0].ClassName));

        Assert.Null(host.GitHub.Settings.SetShowRepositoryColours(true));
        surface.Render();

        surface.WaitForAssertion(() => Assert.Contains("task-calendar__plan--band-1", surface.FindAll($"[data-testid='{PlanTestId}']")[0].ClassName));
    }

    [Fact]
    public async Task Unticking_show_plans_is_remembered_in_the_calendars_view_choices_and_hides_the_plans()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));
        var preferences = new FakeCalendarViewPreferences();

        var surface = host.WithCalendarSurface(preferences).RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll($"[data-testid='{PlanTestId}']")));
        Assert.True(surface.Find("[data-testid='task-calendar-show-plans'] input").HasAttribute("checked"));

        surface.Find("[data-testid='task-calendar-show-plans'] input").Change(false);

        Assert.False(preferences.PlansShown);
        surface.WaitForAssertion(() => Assert.Empty(surface.FindAll($"[data-testid='{PlanTestId}']")));
    }

    /// <summary>On a device that never chose, the box reads the store's default: off,
    /// with no plan drawn over the month until it is ticked.</summary>
    [Fact]
    public async Task On_a_device_that_never_chose_the_plans_are_off_until_ticked()
    {
        using var file = new TempShellNavigationFile();
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));

        var surface = host.WithCalendarSurface(new ShellCalendarViewPreferences(new ShellNavigationStore(file.Path)))
            .RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar-show-plans']")));
        Assert.False(surface.Find("[data-testid='task-calendar-show-plans'] input").HasAttribute("checked"));
        Assert.Empty(surface.FindAll($"[data-testid='{PlanTestId}']"));

        surface.Find("[data-testid='task-calendar-show-plans'] input").Change(true);

        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll($"[data-testid='{PlanTestId}']")));
        Assert.True(new ShellNavigationStore(file.Path).CalendarPlansShown);
    }

    /// <summary>A choice already stored survives a restart either way: ticked stays
    /// ticked, and an untick written while the box was on by default stays off.</summary>
    [Theory]
    [InlineData("""{"calendarPlansShown":true}""", true)]
    [InlineData("""{"calendarPlansShown":false}""", false)]
    public async Task A_stored_show_plans_choice_survives_a_restart(string stored, bool shown)
    {
        using var file = new TempShellNavigationFile();
        File.WriteAllText(file.Path, stored);
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));

        var surface = host.WithCalendarSurface(new ShellCalendarViewPreferences(new ShellNavigationStore(file.Path)))
            .RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar-show-plans']")));
        Assert.Equal(shown, surface.Find("[data-testid='task-calendar-show-plans'] input").HasAttribute("checked"));
        Assert.Equal(shown, surface.FindAll($"[data-testid='{PlanTestId}']").Count > 0);
    }

    /// <summary>The Month | Week range is the Calendar's own view choice: the surface hands
    /// the range it keeps to the calendar, keeps a press, and the calendar answers the
    /// press at once.</summary>
    [Fact]
    public async Task The_calendar_range_is_read_from_its_view_choices_and_a_pick_is_kept()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var preferences = new FakeCalendarViewPreferences { Range = CalendarRange.Week };

        var surface = host.WithCalendarSurface(preferences).RenderCalendarSurface();

        var monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        Assert.Equal("true", surface.Find("[data-testid='task-calendar-range-week']").GetAttribute("aria-pressed"));
        Assert.Equal(7, surface.FindAll("[data-testid='task-calendar'] [data-calendar-day]").Count);
        Assert.NotEmpty(surface.FindAll($"[data-calendar-day='{Iso(monday)}']"));

        surface.Find("[data-testid='task-calendar-range-month']").Click();

        Assert.Equal(CalendarRange.Month, preferences.Range);
        surface.WaitForAssertion(() =>
        {
            Assert.Equal("true", surface.Find("[data-testid='task-calendar-range-month']").GetAttribute("aria-pressed"));
            Assert.True(surface.FindAll("[data-testid='task-calendar'] [data-calendar-day]").Count >= 28);
        });
    }

    /// <summary>The shelf drop: the Calendar hands the tag and the day to the port — whose
    /// adapter has the roadmap's own import place the window — and draws what it reads
    /// back.</summary>
    [Fact]
    public async Task Dropping_a_shelf_plan_on_a_day_starts_its_window_there_through_the_port()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var plans = new FakeCalendarPlans(Today);
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);
        var day = Today.Day <= 15 ? Today.AddDays(3) : Today.AddDays(-3);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar-shelf-release-q4']")));
        var calendar = surface.FindComponent<TaskCalendar>();

        await calendar.InvokeAsync(() => calendar.Instance.DropPlanOnDay("release-q4", Iso(day)));

        Assert.Equal([("release-q4", day)], plans.Started);
        surface.WaitForAssertion(() =>
        {
            Assert.Empty(surface.FindAll("[data-testid='task-calendar-shelf-release-q4']"));
            Assert.NotEmpty(surface.FindAll($"[data-testid='task-calendar-plan-{FakeCalendarPlans.StartedId:N}']"));
        });
        Assert.Empty(host.Toasts.Visible);
    }

    [Fact]
    public async Task A_refused_shelf_drop_is_said_in_a_toast_and_the_plan_stays_on_the_shelf()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today) { Refusal = "a cycle" });

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar-shelf-release-q4']")));
        var calendar = surface.FindComponent<TaskCalendar>();

        await calendar.InvokeAsync(() => calendar.Instance.DropPlanOnDay("release-q4", Iso(Today)));

        Assert.Contains(host.Toasts.Visible, toast =>
            toast.Message.Contains("+release-q4 could not be planned: a cycle", StringComparison.Ordinal)
            && toast.TestId == CalendarSurface.PlanRefusedTestId);
        Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar-shelf-release-q4']"));
    }

    /// <summary>The filter bar, the open task and the selection are the window's (local ADR
    /// 0043 §5), so the Calendar's Tasks part draws what Tasks left — the filtered chips,
    /// the open entry beside them — and a filter set on the Calendar narrows Tasks.</summary>
    [Fact]
    public async Task A_filter_and_the_open_task_set_on_tasks_are_the_calendars_too()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var prompt = await host.WriteEntryAsync($"# A prompt\n`prompt` `due:{Iso(Today)}`\n");
        var task = await host.WriteEntryAsync($"# A task\n`task` `due:{Iso(Today)}`\n");
        host.WithCalendarSurface();

        var tasks = host.Context.Render<TasksSurface>();
        tasks.Find("[data-testid='type-filter-option'][data-type='prompt']").Click();
        await host.OpenAsync(prompt);
        tasks.WaitForAssertion(() => Assert.Single(tasks.FindAll("[data-testid='entry-detail']")));

        // Over to the Calendar.
        await host.Context.DisposeComponentsAsync();
        var calendar = host.RenderCalendarSurface();

        calendar.WaitForAssertion(() =>
        {
            Assert.NotEmpty(calendar.FindAll($"[data-testid='task-calendar-chip-{prompt.TaskId}']"));
            Assert.Empty(calendar.FindAll($"[data-testid='task-calendar-chip-{task.TaskId}']"));
            Assert.Equal("true", calendar.Find("[data-testid='type-filter-option'][data-type='prompt']").GetAttribute("aria-pressed"));
            Assert.Contains("A prompt", Assert.Single(calendar.FindAll("[data-testid='entry-detail']")).TextContent, StringComparison.Ordinal);
        });

        // And back: a filter set on the Calendar narrows Tasks.
        calendar.Find("[data-testid='type-filter-option'][data-type='task']").Click();
        await host.Context.DisposeComponentsAsync();
        tasks = host.Context.Render<TasksSurface>();

        tasks.WaitForAssertion(() =>
        {
            Assert.Equal("true", tasks.Find("[data-testid='type-filter-option'][data-type='task']").GetAttribute("aria-pressed"));
            Assert.Contains(task, host.State.FilteredRows);
            Assert.DoesNotContain(prompt, host.State.FilteredRows);
        });
    }

    /// <summary>The Events layer is still the Tasks part's own until it moves: its box is
    /// ticked apart from the Calendar's Show plans.</summary>
    [Fact]
    public async Task Show_plans_and_show_events_are_ticked_apart()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var events = new FakeCalendarEvents(Today);
        events.SetShown(true);
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() =>
        {
            Assert.NotEmpty(surface.FindAll($"[data-testid='{PlanTestId}']"));
            Assert.NotEmpty(surface.FindAll($"[data-testid='{EventTestId}']"));
        });

        surface.Find("[data-testid='task-calendar-show-plans'] input").Change(false);

        surface.WaitForAssertion(() => Assert.Empty(surface.FindAll($"[data-testid='{PlanTestId}']")));
        Assert.NotEmpty(surface.FindAll($"[data-testid='{EventTestId}']"));
        Assert.True(events.Shown);
    }
}
