using System.Globalization;
using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Calendar.UI;
using Backlog.UI.Components.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Calendar as its own module's view (local ADR 0055, local ADR 0056), drawing its own
/// grid from the layers it reads through its own ports: the tasks and their hours, narrowed
/// and coloured by the window's scope, a chip opening the task in Tasks, a drop and an hours
/// edit written through the Tasks port; the plans — the bars, the milestones and the shelf —
/// the events and their key dates; Show tasks, Show plans, Show events and the range it
/// keeps; and when it reads them again.
/// <para>
/// Dates are relative to the real today, because the Calendar reads its own clock.
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
        var calendar = surface.FindComponent<CalendarGrid>();

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
        var calendar = surface.FindComponent<CalendarGrid>();

        await calendar.InvokeAsync(() => calendar.Instance.DropPlanOnDay("release-q4", Iso(Today)));

        Assert.Contains(host.Toasts.Visible, toast =>
            toast.Message.Contains("+release-q4 could not be planned: a cycle", StringComparison.Ordinal)
            && toast.TestId == CalendarSurface.PlanRefusedTestId);
        Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar-shelf-release-q4']"));
    }

    // --- The Events layer ----------------------------------------------------

    private const string KeyDateTestId = "task-calendar-key-date-k0";

    /// <summary>Show plans and Show events are two of the Calendar's own view choices,
    /// ticked apart.</summary>
    [Fact]
    public async Task Show_plans_and_show_events_are_ticked_apart()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));
        host.Context.Services.AddSingleton<ICalendarEvents>(new FakeCalendarEvents(Today));
        var preferences = new FakeCalendarViewPreferences { EventsShown = true };

        var surface = host.WithCalendarSurface(preferences).RenderCalendarSurface();
        surface.WaitForAssertion(() =>
        {
            Assert.NotEmpty(surface.FindAll($"[data-testid='{PlanTestId}']"));
            Assert.NotEmpty(surface.FindAll($"[data-testid='{EventTestId}']"));
        });

        surface.Find("[data-testid='task-calendar-show-plans'] input").Change(false);

        surface.WaitForAssertion(() => Assert.Empty(surface.FindAll($"[data-testid='{PlanTestId}']")));
        Assert.NotEmpty(surface.FindAll($"[data-testid='{EventTestId}']"));
        Assert.False(preferences.PlansShown);
        Assert.True(preferences.EventsShown);
    }

    [Fact]
    public async Task Without_an_events_port_the_calendar_offers_no_events()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var surface = host.WithCalendarSurface(new FakeCalendarViewPreferences { EventsShown = true }).RenderCalendarSurface();

        Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar']"));
        Assert.Empty(surface.FindAll("[data-testid='task-calendar-show-events']"));
        Assert.Empty(surface.FindAll(".task-calendar__event"));
    }

    /// <summary>The events area switched off answers Off: no box, and nothing drawn,
    /// even on a device where the box was ticked before.</summary>
    [Fact]
    public async Task With_the_events_area_off_there_is_no_box_and_nothing_drawn_whatever_was_ticked()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var events = new FakeCalendarEvents(Today) { Available = false };
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var surface = host.WithCalendarSurface(new FakeCalendarViewPreferences { EventsShown = true }).RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.Equal(1, events.Reads));
        Assert.Empty(surface.FindAll("[data-testid='task-calendar-show-events']"));
        Assert.Empty(surface.FindAll($"[data-testid='{EventTestId}']"));
        Assert.Empty(surface.FindAll($"[data-testid='{KeyDateTestId}']"));
    }

    [Fact]
    public async Task The_events_and_their_key_dates_are_drawn_from_the_port_once_the_box_is_ticked()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var events = new FakeCalendarEvents(Today);
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var surface = host.WithCalendarSurface(new FakeCalendarViewPreferences { EventsShown = true }).RenderCalendarSurface();

        surface.WaitForAssertion(() =>
        {
            var bar = surface.FindAll($"[data-testid='{EventTestId}']")[0];
            Assert.Equal("Techorama · Going · Antwerp", bar.GetAttribute("title"));
            Assert.Contains("task-calendar__event--filled", bar.ClassName);
            Assert.False(bar.HasAttribute("data-calendar-task"));
            Assert.Contains("task-calendar__event--tentative", surface.FindAll("[data-testid='task-calendar-event-e1']")[0].ClassName);
            var keyDate = surface.Find($"[data-calendar-day='{Iso(Today)}'] [data-testid='{KeyDateTestId}']");
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
        host.WithCalendarSurface();

        var surface = host.RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.Equal(1, events.Reads));
        surface.Render();
        Assert.Equal(1, events.Reads);
        Assert.Empty(surface.FindAll("[data-testid='task-calendar-show-events']"));

        // Leaving the Calendar and coming back to it.
        await host.Context.DisposeComponentsAsync();
        surface = host.RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.Equal(2, events.Reads));

        events.Available = true;
        host.Features.SetEnabled("events", true);

        surface.WaitForAssertion(() =>
        {
            Assert.Equal(3, events.Reads);
            Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar-show-events']"));
        });
    }

    /// <summary>On a device that never chose, the box is unticked and nothing is drawn; a
    /// tick is kept in the Calendar's view choices, under the stored name the file always
    /// used, and survives a restart.</summary>
    [Fact]
    public async Task On_a_device_that_never_chose_the_events_are_off_until_ticked_and_the_tick_is_kept()
    {
        using var settings = new TempShellNavigationFile();
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.Services.AddSingleton<ICalendarEvents>(new FakeCalendarEvents(Today));

        var surface = host.WithCalendarSurface(new ShellCalendarViewPreferences(new ShellNavigationStore(settings.Path)))
            .RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar-show-events']")));
        Assert.False(surface.Find("[data-testid='task-calendar-show-events'] input").HasAttribute("checked"));
        Assert.Empty(surface.FindAll($"[data-testid='{EventTestId}']"));
        Assert.Empty(surface.FindAll($"[data-testid='{KeyDateTestId}']"));

        surface.Find("[data-testid='task-calendar-show-events'] input").Change(true);

        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll($"[data-testid='{EventTestId}']")));
        using (var written = System.Text.Json.JsonDocument.Parse(ShellNavigationStoreTests.Written(settings.Path)))
        {
            Assert.True(written.RootElement.GetProperty("calendarEventsShown").GetBoolean());
        }

        Assert.True(new ShellNavigationStore(settings.Path).CalendarEventsShown);

        surface.Find("[data-testid='task-calendar-show-events'] input").Change(false);

        surface.WaitForAssertion(() => Assert.Empty(surface.FindAll($"[data-testid='{EventTestId}']")));
        Assert.DoesNotContain("calendarEventsShown", ShellNavigationStoreTests.Written(settings.Path), StringComparison.Ordinal);
        Assert.False(new ShellNavigationStore(settings.Path).CalendarEventsShown);
    }

    /// <summary>Until the first read lands the Calendar does not know whether the events
    /// area is on, so it offers no box: a box drawn in that gap could be ticked while the
    /// area is off, and the tick would be kept.</summary>
    [Fact]
    public async Task No_show_events_box_is_offered_before_the_events_read_lands()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var gate = new TaskCompletionSource();
        var events = new FakeCalendarEvents(Today) { Gate = gate };
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.Equal(1, events.Reads));
        Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar'] [data-calendar-day]"));
        Assert.Empty(surface.FindAll("[data-testid='task-calendar-show-events']"));

        gate.SetResult();

        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar-show-events']")));
    }

    /// <summary>The window's Events layer outlives the surface, so returning to the Calendar
    /// after the events area was switched off must not offer the box from the read it left
    /// with: a tick in that gap would be kept while the area is off. Each arrival offers no
    /// box until its own read lands.</summary>
    [Fact]
    public async Task A_returning_calendar_offers_no_show_events_box_before_its_own_read_lands()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var events = new FakeCalendarEvents(Today);
        host.Context.Services.AddSingleton<ICalendarEvents>(events);
        host.WithCalendarSurface();

        var surface = host.RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar-show-events']")));

        // Leaving the Calendar; the events area is switched off meanwhile, and the next
        // read is slow to answer.
        await host.Context.DisposeComponentsAsync();
        events.Available = false;
        var gate = new TaskCompletionSource();
        events.Gate = gate;

        surface = host.RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.Equal(2, events.Reads));
        Assert.NotEmpty(surface.FindAll("[data-testid='task-calendar'] [data-calendar-day]"));
        Assert.Empty(surface.FindAll("[data-testid='task-calendar-show-events']"));

        gate.SetResult();

        await surface.InvokeAsync(() => { });
        surface.Render();
        Assert.Empty(surface.FindAll("[data-testid='task-calendar-show-events']"));
    }

    /// <summary>A read made on an earlier day is read again on the next render, so the
    /// Calendar left open overnight draws the events as they stand today.</summary>
    [Fact]
    public async Task The_events_are_read_again_once_the_day_has_moved_on()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.Now);
        var events = new FakeCalendarEvents(Today);
        host.Context.Services.AddSingleton<TimeProvider>(clock);
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.Equal(1, events.Reads));
        surface.Render();
        Assert.Equal(1, events.Reads);

        clock.Advance(TimeSpan.FromDays(1));
        surface.Render();

        surface.WaitForAssertion(() => Assert.Equal(2, events.Reads));
    }

    [Fact]
    public async Task A_plans_read_that_fails_leaves_the_tasks_on_the_calendar()
    {
        using var host = await TasksPaneHost.CreateAsync();
        OneTask(host);
        var plans = new FakeCalendarPlans(Today) { Failure = new IOException("The roadmap could not be read.") };
        host.Context.Services.AddSingleton<ICalendarPlans>(plans);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.Equal(1, plans.Reads));
        surface.WaitForAssertion(() =>
            Assert.NotEmpty(surface.FindAll($"[data-calendar-day='{Iso(Today)}'] [data-testid='{ChipTestId(ShipId)}']")));
        Assert.Empty(surface.FindAll($"[data-testid='{PlanTestId}']"));
    }

    [Fact]
    public async Task An_events_read_that_fails_leaves_the_tasks_on_the_calendar()
    {
        using var host = await TasksPaneHost.CreateAsync();
        OneTask(host);
        var events = new FakeCalendarEvents(Today) { Fails = true };
        host.Context.Services.AddSingleton<ICalendarEvents>(events);

        var surface = host.WithCalendarSurface(new FakeCalendarViewPreferences { EventsShown = true }).RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.Equal(1, events.Reads));
        surface.WaitForAssertion(() =>
            Assert.NotEmpty(surface.FindAll($"[data-calendar-day='{Iso(Today)}'] [data-testid='{ChipTestId(ShipId)}']")));
        Assert.Empty(surface.FindAll(".task-calendar__event"));
    }

    // --- The Tasks layer (local ADR 0056) ------------------------------------------

    private static readonly Guid ShipId = Guid.Parse("a5d6b0c1-37e4-4b0a-9a51-2f1e7c0d4e11");

    private static readonly Guid DocsId = Guid.Parse("0b9f6f0e-6a40-4d55-8b0e-5a2b7f3c9d22");

    private static string ChipTestId(Guid id) => $"task-calendar-chip-{id}";

    private static string BlockTestId(Guid id, DateOnly day) => $"task-calendar-block-{id}-{Iso(day)}";

    /// <summary>Another day of today's month, so it is on the grid whatever the date.</summary>
    private static DateOnly Day => Today.Day <= 15 ? Today.AddDays(3) : Today.AddDays(-3);

    /// <summary>A Tasks port holding one ready task due today, filed under
    /// <c>backlog</c>, with three hours on <see cref="Day"/>.</summary>
    private static FakeCalendarTasks OneTask(TasksPaneHost host)
    {
        var tasks = new FakeCalendarTasks();
        tasks.Tasks.Add(new CalendarTaskDto(ShipId, "Ship the calendar", Today, CalendarTaskStatus.Ready, false, ["backlog"]));
        tasks.Hours.Add(new CalendarHoursBlockDto(ShipId, "Ship the calendar", Day, 3m, ["backlog"]));
        host.Context.Services.AddSingleton<ICalendarTasks>(tasks);
        return tasks;
    }

    [Fact]
    public async Task The_tasks_and_their_hours_are_drawn_from_the_port_for_the_days_on_screen()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var tasks = OneTask(host);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();

        surface.WaitForAssertion(() =>
        {
            Assert.NotNull(surface.Find($"[data-calendar-day='{Iso(Today)}']").QuerySelector($"[data-testid='{ChipTestId(ShipId)}']"));
            Assert.NotNull(surface.Find($"[data-calendar-day='{Iso(Day)}']").QuerySelector($"[data-testid='{BlockTestId(ShipId, Day)}']"));
        });
        var grid = surface.FindComponent<CalendarGrid>().Instance;
        Assert.Equal(grid.ShownWindow, tasks.Reads[^1]);
        Assert.Equal(CalendarMonth.Window(Today), tasks.Reads[0]);
        Assert.Single(tasks.Reads);
    }

    [Fact]
    public async Task Stepping_to_another_month_reads_the_tasks_for_its_days()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var tasks = OneTask(host);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.Single(tasks.Reads));

        surface.Find("[data-testid='task-calendar-next']").Click();

        var next = CalendarMonth.Window(CalendarMonth.FirstOf(Today).AddMonths(1));
        surface.WaitForAssertion(() => Assert.Equal(next, tasks.Reads[^1]));
        Assert.Equal(2, tasks.Reads.Count);
    }

    /// <summary>Show tasks is on by default, offered only while there are tasks to show;
    /// unticked, the chips and their hours go and the choice is kept (local ADR 0056 §4).</summary>
    [Fact]
    public async Task Show_tasks_is_on_by_default_and_unticking_it_hides_the_chips_and_hours_and_is_remembered()
    {
        using var host = await TasksPaneHost.CreateAsync();
        OneTask(host);
        var preferences = new FakeCalendarViewPreferences();

        var surface = host.WithCalendarSurface(preferences).RenderCalendarSurface();

        surface.WaitForAssertion(() =>
            Assert.True(surface.Find("[data-testid='task-calendar-show-tasks'] input").HasAttribute("checked")));
        Assert.NotEmpty(surface.FindAll($"[data-testid='{ChipTestId(ShipId)}']"));

        surface.Find("[data-testid='task-calendar-show-tasks'] input").Change(false);

        surface.WaitForAssertion(() =>
        {
            Assert.False(preferences.TasksShown);
            Assert.Empty(surface.FindAll($"[data-testid='{ChipTestId(ShipId)}']"));
            Assert.Empty(surface.FindAll($"[data-testid='{BlockTestId(ShipId, Day)}']"));
            Assert.Empty(surface.FindAll($"[data-testid='task-calendar-day-{Iso(Day)}-hours']"));
        });
    }

    /// <summary>A host without Tasks still has a Calendar: the plans and the events draw,
    /// and there is no Show tasks to offer (ADR 0056 Verification 4).</summary>
    [Fact]
    public async Task Without_tasks_the_calendar_draws_the_plans_and_events_and_offers_no_show_tasks()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.Services.AddSingleton<ICalendarTasks>(new FakeCalendarTasks { Off = true });
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));
        host.Context.Services.AddSingleton<ICalendarEvents>(new FakeCalendarEvents(Today));

        var surface = host.WithCalendarSurface(new FakeCalendarViewPreferences { EventsShown = true }).RenderCalendarSurface();

        surface.WaitForAssertion(() =>
        {
            Assert.NotEmpty(surface.FindAll($"[data-testid='{PlanTestId}']"));
            Assert.NotEmpty(surface.FindAll($"[data-testid='{EventTestId}']"));
        });
        Assert.Empty(surface.FindAll("[data-testid='task-calendar-show-tasks']"));
        Assert.Empty(surface.FindAll("[data-testid^='task-calendar-day-'][data-testid$='-add-hours']"));
    }

    [Fact]
    public async Task A_tasks_read_that_fails_leaves_the_plans_on_the_calendar()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var tasks = OneTask(host);
        tasks.Failure = new IOException("The task list could not be read.");
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));

        var surface = host.WithCalendarSurface().RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll($"[data-testid='{PlanTestId}']")));
        Assert.NotEmpty(tasks.Reads);
        Assert.Empty(surface.FindAll($"[data-testid='{ChipTestId(ShipId)}']"));
    }

    /// <summary>The window's scope narrows the tasks and their hours — the Brain taking the
    /// ones filed under no repository — and nothing of the Tasks view's filter bar
    /// reaches the Calendar.</summary>
    [Fact]
    public async Task The_windows_scope_narrows_the_tasks_and_their_hours_the_brain_included()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var tasks = OneTask(host);
        tasks.Tasks.Add(new CalendarTaskDto(DocsId, "Write the docs", Today, CalendarTaskStatus.Draft, false, []));
        tasks.Hours.Add(new CalendarHoursBlockDto(DocsId, "Write the docs", Day, 2m, []));
        var scope = new FakeCalendarWindowScope();
        host.Context.Services.AddSingleton<ICalendarWindowScope>(scope);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() =>
        {
            Assert.NotEmpty(surface.FindAll($"[data-testid='{ChipTestId(ShipId)}']"));
            Assert.NotEmpty(surface.FindAll($"[data-testid='{ChipTestId(DocsId)}']"));
        });

        scope.Scope.Add("other");
        scope.Brain = true;
        scope.RaiseChanged();

        surface.WaitForAssertion(() =>
        {
            Assert.Empty(surface.FindAll($"[data-testid='{ChipTestId(ShipId)}']"));
            Assert.Empty(surface.FindAll($"[data-testid='{BlockTestId(ShipId, Day)}']"));
            Assert.NotEmpty(surface.FindAll($"[data-testid='{ChipTestId(DocsId)}']"));
            Assert.NotEmpty(surface.FindAll($"[data-testid='{BlockTestId(DocsId, Day)}']"));
            Assert.Equal("2h", surface.Find($"[data-testid='task-calendar-day-{Iso(Day)}-hours']").QuerySelector("[aria-hidden='true']")!.TextContent.Trim());
        });
    }

    /// <summary>A type filter set in Tasks does not narrow the Calendar, and none of the
    /// Tasks view comes along: no filter bar, no detail panel, no tray, no My Day (ADR 0056
    /// Verification 7).</summary>
    [Fact]
    public async Task A_tasks_filter_does_not_reach_the_calendar_and_nothing_of_the_tasks_view_draws_there()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var prompt = await host.WriteEntryAsync($"# A prompt\n`prompt` `due:{Iso(Today)}`\n");
        await host.WriteEntryAsync($"# A task\n`task` `due:{Iso(Today)}`\n");
        var tasks = OneTask(host);
        host.WithCalendarSurface();

        var list = host.Context.Render<TasksSurface>();
        list.Find("[data-testid='type-filter-option'][data-type='prompt']").Click();
        await host.OpenAsync(prompt);
        list.WaitForAssertion(() => Assert.Single(list.FindAll("[data-testid='entry-detail']")));

        await host.Context.DisposeComponentsAsync();
        var surface = host.RenderCalendarSurface();

        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll($"[data-testid='{ChipTestId(ShipId)}']")));
        Assert.Empty(surface.FindAll(".filter-bar"));
        Assert.Empty(surface.FindAll("[data-testid='entry-detail']"));
        Assert.Empty(surface.FindAll("[data-testid='task-calendar-tray']"));
        Assert.Empty(surface.FindAll("[data-testid='task-calendar-my-day']"));
        Assert.Empty(surface.FindAll("[data-testid='backlog-pane']"));
        Assert.Single(tasks.Reads);
    }

    [Fact]
    public async Task The_calendar_draws_in_its_own_region_the_tab_controls()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var surface = host.WithCalendarSurface().RenderCalendarSurface();

        var region = surface.Find($"#{CalendarSurface.RegionId}");
        Assert.Equal("calendar-view", region.Id);
        Assert.Equal("Calendar", region.GetAttribute("aria-label"));
        Assert.NotNull(region.QuerySelector("[data-testid='task-calendar']"));
    }

    /// <summary>The chips, the hours blocks and the plan bars wear the band the window's
    /// colours give their first repository, and none while the colours are hidden.</summary>
    [Fact]
    public async Task Chips_blocks_and_plan_bars_wear_the_windows_band_and_are_neutral_without_one()
    {
        using var host = await TasksPaneHost.CreateAsync();
        OneTask(host);
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));
        var scope = new FakeCalendarWindowScope();
        host.Context.Services.AddSingleton<ICalendarWindowScope>(scope);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("repo-mark", surface.Find($"[data-testid='{ChipTestId(ShipId)}']").ClassName);
            Assert.DoesNotContain("repo-mark", surface.Find($"[data-testid='{BlockTestId(ShipId, Day)}']").ClassName);
            Assert.Contains("task-calendar__plan--neutral", surface.FindAll($"[data-testid='{PlanTestId}']")[0].ClassName);
        });

        scope.Bands["backlog"] = 2;
        scope.RaiseChanged();

        surface.WaitForAssertion(() =>
        {
            Assert.Contains("repo-mark--2", surface.Find($"[data-testid='{ChipTestId(ShipId)}']").ClassName);
            Assert.Contains("repo-mark--2", surface.Find($"[data-testid='{BlockTestId(ShipId, Day)}']").ClassName);
            Assert.Contains("task-calendar__plan--band-2", surface.FindAll($"[data-testid='{PlanTestId}']")[0].ClassName);
        });
    }

    /// <summary>The shell's scope answers from the window's own state: the bar takes the
    /// band of its repository only while the colours show.</summary>
    [Fact]
    public async Task A_bar_wears_its_repository_colour_only_while_the_windows_colours_are_showing()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        host.Context.Services.AddSingleton<ICalendarPlans>(new FakeCalendarPlans(Today));
        host.Context.Services.AddSingleton<ICalendarWindowScope>(new ShellCalendarWindowScope(host.State));

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.Contains("task-calendar__plan--neutral", surface.FindAll($"[data-testid='{PlanTestId}']")[0].ClassName));

        Assert.Null(host.State.SetRepositoryColoursVisible(true));

        surface.WaitForAssertion(() => Assert.Contains("task-calendar__plan--band-1", surface.FindAll($"[data-testid='{PlanTestId}']")[0].ClassName));
    }

    /// <summary>A chip dropped on another day writes its due date through the port; a
    /// refusal is said in a toast (ADR 0056 Verification 6).</summary>
    [Fact]
    public async Task Dragging_a_chip_to_another_day_writes_its_due_date_through_the_port_and_a_refusal_is_toasted()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var tasks = OneTask(host);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll($"[data-testid='{ChipTestId(ShipId)}']")));
        var grid = surface.FindComponent<CalendarGrid>();

        await grid.InvokeAsync(() => grid.Instance.DropOnDay(ShipId.ToString(), Iso(Day)));

        Assert.Equal([(ShipId, Day)], tasks.DueWrites);
        surface.WaitForAssertion(() =>
            Assert.NotNull(surface.Find($"[data-calendar-day='{Iso(Day)}']").QuerySelector($"[data-testid='{ChipTestId(ShipId)}']")));
        Assert.Empty(host.Toasts.Visible);

        tasks.Refusal = "the entry is read-only";
        await grid.InvokeAsync(() => grid.Instance.DropOnDay(ShipId.ToString(), Iso(Today)));

        Assert.Contains(host.Toasts.Visible, toast =>
            toast.Message.Contains("the entry is read-only", StringComparison.Ordinal)
            && toast.TestId == CalendarSurface.DueRefusedTestId);
        Assert.NotNull(surface.Find($"[data-calendar-day='{Iso(Day)}']").QuerySelector($"[data-testid='{ChipTestId(ShipId)}']"));
    }

    /// <summary>Hours added, changed and removed on a day go through the port; a refusal is
    /// said in a toast.</summary>
    [Fact]
    public async Task Hours_added_changed_and_removed_on_a_day_go_through_the_port()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var tasks = OneTask(host);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll($"[data-testid='{BlockTestId(ShipId, Day)}']")));

        // Change the block that is there.
        surface.Find($"[data-testid='{BlockTestId(ShipId, Day)}']").Click();
        surface.Find("[data-testid='task-calendar-hours-editor-hours'] input").Input("5");
        surface.Find("[data-testid='task-calendar-hours-editor-save']").Click();
        surface.WaitForAssertion(() => Assert.Equal([(ShipId, Day, (decimal?)5m)], tasks.HoursWrites));
        surface.WaitForAssertion(() => Assert.Contains("5h", surface.Find($"[data-testid='{BlockTestId(ShipId, Day)}']").TextContent, StringComparison.Ordinal));

        // Remove it.
        surface.Find($"[data-testid='{BlockTestId(ShipId, Day)}']").Click();
        surface.Find("[data-testid='task-calendar-hours-editor-remove']").Click();
        surface.WaitForAssertion(() => Assert.Empty(surface.FindAll($"[data-testid='{BlockTestId(ShipId, Day)}']")));
        Assert.Equal((ShipId, Day, (decimal?)null), tasks.HoursWrites[^1]);

        // Add one through the day's "Add hours…".
        surface.Find($"[data-testid='task-calendar-day-{Iso(Day)}-add-hours']").Click();
        surface.Find("[data-testid='task-calendar-hours-editor-pick'] select").Change(ShipId.ToString());
        surface.Find("[data-testid='task-calendar-hours-editor-hours'] input").Input("2");
        surface.Find("[data-testid='task-calendar-hours-editor-save']").Click();
        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll($"[data-testid='{BlockTestId(ShipId, Day)}']")));
        Assert.Equal((ShipId, Day, (decimal?)2m), tasks.HoursWrites[^1]);

        // A refusal is said, and the block stays as it was.
        tasks.Refusal = "at most 24";
        surface.Find($"[data-testid='{BlockTestId(ShipId, Day)}']").Click();
        surface.Find("[data-testid='task-calendar-hours-editor-hours'] input").Input("4");
        surface.Find("[data-testid='task-calendar-hours-editor-save']").Click();
        surface.WaitForAssertion(() => Assert.Contains(host.Toasts.Visible, toast =>
            toast.Message.Contains("at most 24", StringComparison.Ordinal)
            && toast.TestId == CalendarSurface.HoursRefusedTestId));
        Assert.Contains("2h", surface.Find($"[data-testid='{BlockTestId(ShipId, Day)}']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_day_totals_its_blocks_against_the_capacity_the_port_answers_and_marks_a_day_over_it()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var tasks = OneTask(host);
        tasks.Tasks.Add(new CalendarTaskDto(DocsId, "Write the docs", Today, CalendarTaskStatus.Draft, false, []));
        tasks.Hours.Add(new CalendarHoursBlockDto(DocsId, "Write the docs", Day, 4m, []));
        var capacity = new FakeCalendarCapacity(8m);
        host.Context.Services.AddSingleton<ICalendarCapacity>(capacity);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();

        surface.WaitForAssertion(() =>
            Assert.Equal("7h / 8h", surface.Find($"[data-testid='task-calendar-day-{Iso(Day)}-hours']").QuerySelector("[aria-hidden='true']")!.TextContent.Trim()));

        capacity.Hours = 6m;
        capacity.RaiseChanged();

        surface.WaitForAssertion(() =>
        {
            var day = surface.Find($"[data-testid='task-calendar-day-{Iso(Day)}-hours']");
            Assert.Equal("7h / 6h", day.QuerySelector("[aria-hidden='true']")!.TextContent.Trim());
            Assert.Contains("task-calendar__hours--over", day.ClassName);
        });
    }

    [Fact]
    public async Task Overdue_and_done_tasks_are_drawn_apart()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var tasks = new FakeCalendarTasks();
        tasks.Tasks.Add(new CalendarTaskDto(ShipId, "Late one", Today.AddDays(-1), CalendarTaskStatus.Ready, false, []));
        tasks.Tasks.Add(new CalendarTaskDto(DocsId, "Finished one", Today.AddDays(-1), CalendarTaskStatus.Done, false, []));
        host.Context.Services.AddSingleton<ICalendarTasks>(tasks);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();

        // On the 1st, yesterday is in the month before, which a grid that opens on a
        // Monday the 1st does not reach; step back to it.
        if (Today.Day == 1) surface.Find("[data-testid='task-calendar-previous']").Click();

        surface.WaitForAssertion(() =>
        {
            Assert.Contains("task-calendar__chip--overdue", surface.Find($"[data-testid='{ChipTestId(ShipId)}']").ClassName);
            var done = surface.Find($"[data-testid='{ChipTestId(DocsId)}']");
            Assert.Contains("task-calendar__chip--done", done.ClassName);
            Assert.DoesNotContain("task-calendar__chip--overdue", done.ClassName);
        });
    }

    /// <summary>Pressing a chip asks this window's navigator for the task in Tasks (ADR 0056
    /// Verification 8).</summary>
    [Fact]
    public async Task Pressing_a_chip_asks_the_windows_navigator_for_the_task_in_tasks()
    {
        using var host = await TasksPaneHost.CreateAsync();
        OneTask(host);
        var navigator = new SurfaceNavigator();
        var requests = new List<SurfaceRequest>();
        navigator.ShowRequested += requests.Add;
        host.Context.Services.AddSingleton<ISurfaceNavigator>(navigator);

        var surface = host.WithCalendarSurface().RenderCalendarSurface();
        surface.WaitForAssertion(() => Assert.NotEmpty(surface.FindAll($"[data-testid='{ChipTestId(ShipId)}']")));

        surface.Find($"[data-testid='{ChipTestId(ShipId)}']").Click();

        var request = Assert.Single(requests);
        Assert.Equal("tasks", request.SurfaceId);
        Assert.Equal(ShipId.ToString(), request.Value("entry"));
    }

    /// <summary>The Calendar may not see Tasks' query constants, so it keeps its own; they
    /// must name the same surface and key.</summary>
    [Fact]
    public void The_calendars_tasks_surface_and_entry_key_are_the_tasks_views_own()
    {
        Assert.Equal(TasksSurfaceQuery.SurfaceId, CalendarSurface.TasksSurfaceId);
        Assert.Equal(TasksSurfaceQuery.Entry, CalendarSurface.EntryQueryKey);
    }
}
