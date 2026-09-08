using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The dashboard surface: its fixed composition, its filter, and the independence
/// its seven parts promise.
/// </summary>
public class DashboardPaneTests
{
    /// <summary>
    /// Every part, on a machine where no provider is reachable. This is the state a
    /// fresh install is in, and a pane that only rendered with data behind it would
    /// fail here.
    /// </summary>
    [Fact]
    public void Every_part_renders_even_when_no_provider_can_answer()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();

        foreach (var part in new[]
                 {
                     "dashboard-headline",
                     "dashboard-score",
                     "dashboard-rework",
                     "dashboard-trend",
                     "dashboard-sessions",
                     "dashboard-spend-month",
                     "dashboard-spend-trend",
                     "dashboard-spend-model"
                 })
        {
            Assert.NotNull(pane.Find($"[data-testid='{part}']"));
        }
    }

    /// <summary>
    /// The independence claim, asserted rather than described: each part carries its
    /// source's own words, so one unconfigured provider explains itself instead of
    /// blanking the surface.
    /// </summary>
    [Fact]
    public void A_part_whose_source_refuses_carries_that_sources_reason()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();

        Assert.Contains(DashboardTestHost.UnavailableReason, pane.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// One part failing must not take another down. Productivity is refused here and
    /// cost answers, so the cost figures have to be on screen beside the explanation.
    /// </summary>
    [Fact]
    public void One_source_refusing_leaves_the_other_parts_figures_on_screen()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ICostInsights>(new ReadyCostInsights()));

        var pane = context.Render<DashboardPane>();

        // Productivity explains itself...
        Assert.Contains(DashboardTestHost.UnavailableReason, pane.Markup, StringComparison.Ordinal);

        // ...and the cost tile still shows its money.
        var tile = pane.Find("[data-testid='dashboard-spend-month-claude']");
        Assert.Contains("12.34", tile.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A source that genuinely yields before answering, which every real provider
    /// does and no synchronous test double does.
    /// </summary>
    /// <remarks>
    /// This is a regression test with a specific bug behind it. The part base used to
    /// await its fetch with <c>ConfigureAwait(false)</c>, which took the continuation
    /// off the renderer's dispatcher; <c>StateHasChanged</c> asserts it is on that
    /// dispatcher, so it threw and took the whole circuit down. Against doubles that
    /// returned an already-completed task the awaits resumed synchronously, the flag
    /// did nothing, and every test passed — the failure only appeared against a real
    /// provider. So the double here yields on purpose.
    /// </remarks>
    [Fact]
    public async Task A_source_that_answers_asynchronously_still_leaves_the_loading_state()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(new YieldingProductivityInsights()));

        var pane = context.Render<DashboardPane>();

        await pane.InvokeAsync(() => { });

        pane.WaitForAssertion(() =>
        {
            var headline = pane.Find("[data-testid='dashboard-headline']");
            Assert.DoesNotContain("Loading", headline.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Answered after yielding.", headline.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_filter_offers_every_configured_repository_and_an_all_repositories_option()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var options = pane.FindAll("[data-testid='dashboard-repository-filter'] option");

        Assert.Equal(3, options.Count);
        Assert.Equal("All repositories", options[0].TextContent);
        Assert.Contains(options, option => option.TextContent == "JSdotNet/backlog");
        Assert.Contains(options, option => option.TextContent == "JSdotNet/backlog-ide");
    }

    /// <summary>
    /// Choosing a repository has to reach the parts, or the filter is a control that
    /// silently drives half a page — which is the usual way a dashboard goes stale.
    /// </summary>
    [Fact]
    public void Focusing_a_repository_reaches_the_productivity_parts()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-repository-filter'] select").Change("backlog-ide");

        Assert.Contains("backlog-ide", productivity.Scopes.Select(scope => scope.RepositoryAlias));
    }

    /// <summary>
    /// The cost parts cannot narrow by repository, because neither provider reports
    /// spend that way. They must not re-fetch when the filter moves, or the dashboard
    /// would spend a call budget to produce the identical answer.
    /// </summary>
    [Fact]
    public void Focusing_a_repository_does_not_re_ask_the_cost_parts()
    {
        var costs = new RecordingCostInsights();

        using var context = Context(configure: services => services.AddSingleton<ICostInsights>(costs));

        var pane = context.Render<DashboardPane>();
        var afterFirstRender = costs.Calls;

        pane.Find("[data-testid='dashboard-repository-filter'] select").Change("backlog-ide");

        Assert.Equal(afterFirstRender, costs.Calls);
    }

    /// <summary>
    /// The constraint has to be on screen, not only in the code. A reader who cannot
    /// see why a figure did not move when they filtered will conclude the filter is
    /// broken.
    /// </summary>
    [Fact]
    public void The_cost_section_says_neither_filter_reaches_it()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var cost = pane.Find("[data-testid='dashboard-cost']");

        Assert.Contains(
            "neither the repository filter nor the machine filter above changes anything in this section",
            Squashed(cost.TextContent),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same rule for the other direction: GitHub cannot say which machine a pull
    /// request was worked from, so the productivity section says the machine filter
    /// does not reach it rather than letting the reader conclude the control is broken.
    /// </summary>
    [Fact]
    public void The_productivity_section_says_the_machine_filter_does_not_reach_it()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var productivity = pane.Find("[data-testid='dashboard-productivity']");

        Assert.Contains(
            "the machine filter above does not change anything in this section",
            Squashed(productivity.TextContent),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// And the third: the sessions part is the one thing the machine filter does drive,
    /// and the one thing the repository filter cannot — Claude records no repository
    /// against a session.
    /// </summary>
    [Fact]
    public void The_sessions_section_says_the_repository_filter_does_not_reach_it()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var sessions = pane.Find("[data-testid='dashboard-sessions-section']");

        // Copilot does record one, and always has. The refusal is right; the reason the
        // section used to give for it named the wrong assistant.
        Assert.Contains(
            "Only Copilot records a repository against a session, so filtering by one would hide "
            + "Claude's half of the picture; the repository filter above does not change this section",
            Squashed(sessions.TextContent),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_machine_filter_offers_this_machine_and_an_all_machines_option()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var options = pane.FindAll("[data-testid='dashboard-machine-filter'] option");

        Assert.Equal(2, options.Count);
        Assert.Equal("All machines", options[0].TextContent);
        Assert.Equal(DashboardTestHost.MachineName, options[1].TextContent);

        // The value is the id, not the name. A filter keyed on the name would merge two
        // machines that happen to share one and split one that was renamed.
        Assert.Equal(DashboardTestHost.MachineId, options[1].GetAttribute("value"));
    }

    /// <summary>
    /// The machine filter has to reach the one part that can honour it, or it is a
    /// control that does nothing.
    /// </summary>
    [Fact]
    public void Focusing_a_machine_reaches_the_sessions_part()
    {
        var sessions = new RecordingSessionInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(sessions));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-machine-filter'] select").Change(DashboardTestHost.MachineId);

        Assert.Contains(DashboardTestHost.MachineId, sessions.Scopes.Select(scope => scope.MachineId));
    }

    /// <summary>
    /// GitHub does not report which machine a pull request was worked from, so moving
    /// the machine filter must not send the productivity parts back to it. Four parts
    /// re-fetching a quarter's churn for an answer that cannot have changed is a few
    /// hundred wasted calls and a Loading flash over figures that were already right.
    /// </summary>
    [Fact]
    public void Focusing_a_machine_does_not_re_ask_the_productivity_parts()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        var afterFirstRender = productivity.Scopes.Count;

        pane.Find("[data-testid='dashboard-machine-filter'] select").Change(DashboardTestHost.MachineId);

        Assert.Equal(afterFirstRender, productivity.Scopes.Count);
    }

    [Fact]
    public void Focusing_a_machine_does_not_re_ask_the_cost_parts()
    {
        var costs = new RecordingCostInsights();

        using var context = Context(configure: services => services.AddSingleton<ICostInsights>(costs));

        var pane = context.Render<DashboardPane>();
        var afterFirstRender = costs.Calls;

        pane.Find("[data-testid='dashboard-machine-filter'] select").Change(DashboardTestHost.MachineId);

        Assert.Equal(afterFirstRender, costs.Calls);
    }

    /// <summary>
    /// The other half of the same rule, and the one the three-flag scope comparison
    /// exists for: the cost parts take no window either, so narrowing it must not
    /// re-ask them.
    /// </summary>
    [Fact]
    public void Narrowing_the_window_does_not_re_ask_the_cost_parts()
    {
        var costs = new RecordingCostInsights();

        using var context = Context(configure: services => services.AddSingleton<ICostInsights>(costs));

        var pane = context.Render<DashboardPane>();
        var afterFirstRender = costs.Calls;

        pane.Find("[data-testid='dashboard-window-4']").Click();

        Assert.Equal(afterFirstRender, costs.Calls);
    }

    /// <summary>
    /// Claude records no repository, so the repository filter must not reach the
    /// sessions part — a re-fetch would be harmless in itself, but a part that
    /// re-fetches on a filter it says it ignores is a part whose sentence has stopped
    /// being true.
    /// </summary>
    [Fact]
    public void Focusing_a_repository_does_not_re_ask_the_sessions_part()
    {
        var sessions = new RecordingSessionInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(sessions));

        var pane = context.Render<DashboardPane>();
        var afterFirstRender = sessions.Scopes.Count;

        pane.Find("[data-testid='dashboard-repository-filter'] select").Change("backlog-ide");

        Assert.Equal(afterFirstRender, sessions.Scopes.Count);
    }

    [Fact]
    public void The_sessions_part_puts_its_three_figures_on_screen()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        Assert.Contains("12", pane.Find("[data-testid='dashboard-sessions-count']").TextContent, StringComparison.Ordinal);
        Assert.Contains("5h", pane.Find("[data-testid='dashboard-sessions-active']").TextContent, StringComparison.Ordinal);
        Assert.Contains("19 Aug 09:30", pane.Find("[data-testid='dashboard-sessions-last']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A session whose start was never recorded is counted and adds no time, and the
    /// tile says so. An active time quietly lower than the session count implies is the
    /// figure a reader would take at face value and be wrong about.
    /// </summary>
    [Fact]
    public void The_active_time_tile_names_the_sessions_it_could_not_measure()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        Assert.Contains(
            "Excludes 3 sessions that left no activity record",
            pane.Find("[data-testid='dashboard-sessions-active']").TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The figure under this tile changed by a factor of twenty-eight, so the tile has to
    /// say what it now measures rather than leave a reader to assume it still means what
    /// it did. The threshold goes with it: five minutes is a judgement the answer moves
    /// under, and an unnamed constant is one nobody can argue with.
    /// </summary>
    [Fact]
    public void The_active_time_tile_says_what_it_measures_and_names_the_threshold()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var tile = Squashed(pane.Find("[data-testid='dashboard-sessions-active']").TextContent);

        Assert.Contains("Agent-active time", tile, StringComparison.Ordinal);
        Assert.Contains("Time an agent was producing, not time a session was open.", tile, StringComparison.Ordinal);

        // The number comes off the report rather than out of a constant beside the part,
        // so a threshold the Sessions context changes reaches this sentence.
        Assert.Contains("A gap of more than 5m ends a run", tile, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only one of the two assistants can evidence a wait at all. Reporting Claude's
    /// figure under both names would be the same failure as a capped read presented as a
    /// total — and the uncapped gap is admitted for the same reason, because a session
    /// picked up on Monday puts a whole weekend in this figure.
    /// </summary>
    [Fact]
    public void The_waiting_tile_says_whose_figure_it_is_and_that_a_gap_is_uncapped()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var tile = Squashed(pane.Find("[data-testid='dashboard-sessions-waiting']").TextContent);

        Assert.Contains("Waiting for a prompt", tile, StringComparison.Ordinal);
        Assert.Contains("Copilot records no prompt boundary, so this is Claude's alone.", tile, StringComparison.Ordinal);
        Assert.Contains("puts the whole weekend here", tile, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rename this tile exists under. A gap ends in a <em>prompt</em>, and 1,623 of
    /// the 1,728 that end one on the measured machine came from an SDK or a skill rather
    /// than a keyboard. "Waiting on you" measured the orchestrator and named the reader,
    /// which is precisely the failure the rest of this part is built to avoid, so the
    /// footnote has to say where the prompts come from.
    /// </summary>
    [Fact]
    public void The_waiting_tile_does_not_claim_the_reader_was_the_one_being_waited_on()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var tile = Squashed(pane.Find("[data-testid='dashboard-sessions-waiting']").TextContent);

        Assert.DoesNotContain("Waiting on you", tile, StringComparison.Ordinal);
        Assert.Contains(
            "Most prompts here come from an SDK or a skill rather than from the keyboard, "
            + "so this is rarely time it spent waiting on you.",
            tile,
            StringComparison.Ordinal);
    }

    /// <summary>Seven dated days down, twenty-four hours across, and every hour present
    /// on every row — a row that omitted its quiet hours would render short and read as
    /// "not reported" where the honest answer is zero.</summary>
    [Fact]
    public void The_grid_draws_seven_days_and_twenty_four_hours()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var grid = pane.Find("[data-testid='dashboard-sessions-hours']");

        Assert.Equal(7, grid.QuerySelectorAll("tbody tr").Length);

        // The hour headings specifically, not every heading: the day column adds a
        // twenty-fifth that is not an hour.
        Assert.Equal(24, grid.QuerySelectorAll(".metric-heatmap__bucket").Length);
        Assert.Equal(7 * 24, grid.QuerySelectorAll(".metric-heatmap__cell").Length);
    }

    /// <summary>
    /// The grid is the one figure on this surface the period control does not move, and
    /// the one drawn on a local clock. Both refusals are stated where the thing that will
    /// not move actually is, because a reader who changes the period and watches
    /// everything but this shift should have been told by its own caption.
    /// </summary>
    [Fact]
    public void The_grid_says_it_is_the_last_seven_days_and_a_local_clock()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var grid = Squashed(pane.Find("[data-testid='dashboard-sessions-hours']").TextContent);

        Assert.Contains("by hour of your local clock", grid, StringComparison.Ordinal);
        Assert.Contains("the last 7 days, whichever period is selected above", grid, StringComparison.Ordinal);
    }

    /// <summary>The count is in the block, not only in the tooltip. A shade is one of four
    /// steps and the readings run from nothing to a dozen, so the digit is what makes a
    /// busy hour legible without a pointer.</summary>
    [Fact]
    public void Each_block_prints_the_number_of_sessions_that_ran_at_once()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var cells = pane.Find("[data-testid='dashboard-sessions-hours']").QuerySelectorAll(".metric-heatmap__cell");

        // The fixture puts four concurrent sessions in the 09 column and nothing else, so
        // the quiet hours keep their track shade and print nothing — a grid where most
        // hours are empty would otherwise be a wall of noughts.
        Assert.Equal("4", cells[9].QuerySelector(".metric-heatmap__value")!.TextContent);
        Assert.Null(cells[8].QuerySelector(".metric-heatmap__value"));
    }

    /// <summary>
    /// The day's own figure, in its own column, counting sessions rather than adding up
    /// the peaks beside it. The caption has to say which, because a column of numbers at
    /// the end of a row of numbers reads as that row's total and this one is not.
    /// </summary>
    [Fact]
    public void The_grid_carries_a_day_column_counting_the_sessions_that_ran()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var grid = pane.Find("[data-testid='dashboard-sessions-hours']");

        Assert.Equal(
            ["Sessions", "In hours", "Outside"],
            grid.QuerySelectorAll("thead th").TakeLast(3).Select(heading => heading.TextContent));

        // The first row's three, in order. They do not add up and are not meant to.
        Assert.Equal(
            ["1", "0", "1"],
            grid.QuerySelectorAll("tbody tr")[0]
                .QuerySelectorAll(".metric-heatmap__total")
                .Select(total => total.TextContent));

        Assert.Contains(
            "The columns count the distinct sessions that ran each day, so they are not "
            + "the row added up — and a session that ran across the edge of your day is "
            + "counted in hours and outside them both, so those two do not add up to the "
            + "first either.",
            Squashed(grid.TextContent),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The second grid, on the same axes and the same outline, counting sessions that were
    /// on the go rather than sessions that were producing. The fixture makes them differ,
    /// because two grids wired to the same measure would render identically and pass a
    /// test that only checked one of them.
    /// </summary>
    [Fact]
    public void A_second_grid_counts_the_sessions_that_were_open_rather_than_producing()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var open = pane.Find("[data-testid='dashboard-sessions-open']");
        var peak = pane.Find("[data-testid='dashboard-sessions-hours']");

        var openCells = open.QuerySelectorAll("tbody tr")[0].QuerySelectorAll(".metric-heatmap__cell");
        var peakCells = peak.QuerySelectorAll("tbody tr")[0].QuerySelectorAll(".metric-heatmap__cell");

        Assert.Equal("7", openCells[9].QuerySelector(".metric-heatmap__value")!.TextContent);
        Assert.Equal("4", peakCells[9].QuerySelector(".metric-heatmap__value")!.TextContent);

        // An hour where nothing produced but something was still on the go.
        Assert.Equal("2", openCells[10].QuerySelector(".metric-heatmap__value")!.TextContent);
        Assert.Null(peakCells[10].QuerySelector(".metric-heatmap__value"));

        // Seven days and twenty-four hours, same as the first, and the working week
        // outlined on it too.
        Assert.Equal(7, open.QuerySelectorAll("tbody tr").Length);
        Assert.Equal(24, open.QuerySelectorAll(".metric-heatmap__bucket").Length);
        Assert.NotEmpty(open.QuerySelectorAll(".metric-heatmap__cell--marked"));

        // The day columns belong to the day, not to a chart, so they are not repeated.
        Assert.Empty(open.QuerySelectorAll(".metric-heatmap__total"));
    }

    /// <summary>The pair is the point, so the second grid has to say what it counts and
    /// what it deliberately does not.</summary>
    [Fact]
    public void The_open_grid_says_it_excludes_a_session_that_never_resumed()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var open = Squashed(pane.Find("[data-testid='dashboard-sessions-open']").TextContent);

        Assert.Contains("Sessions on the go at once", open, StringComparison.Ordinal);
        Assert.Contains(
            "A session that went quiet and never resumed is not counted, because nothing "
            + "records when a session ended.",
            open,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The working week is outlined on the grid. The fixture keeps the default — Monday to
    /// Friday, nine to half five, weekend off — and 2026-08-13 is a Thursday, so the row
    /// runs Thursday through Wednesday.
    /// <para>
    /// Half past five is the case worth pinning: the 17:00 hour is only half inside the
    /// working day and there is no half-outlined cell, so it is outlined whole and the
    /// caption says the mark follows the overlap. 18:00 is the first hour outside.
    /// </para>
    /// </summary>
    [Fact]
    public void The_grid_outlines_the_hours_of_the_working_week()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var rows = pane.Find("[data-testid='dashboard-sessions-hours']").QuerySelectorAll("tbody tr");

        var thursday = rows[0].QuerySelectorAll(".metric-heatmap__cell");
        var saturday = rows[2].QuerySelectorAll(".metric-heatmap__cell");

        Assert.DoesNotContain("metric-heatmap__cell--marked", thursday[8].ClassList);
        Assert.Contains("metric-heatmap__cell--marked", thursday[9].ClassList);

        // Half past five leaves this hour half worked, and it is outlined whole.
        Assert.Contains("metric-heatmap__cell--marked", thursday[17].ClassList);
        Assert.DoesNotContain("metric-heatmap__cell--marked", thursday[18].ClassList);

        // A day off is outlined nowhere at all.
        Assert.DoesNotContain(
            saturday,
            cell => cell.ClassList.Contains("metric-heatmap__cell--marked"));
    }

    /// <summary>The outline is a preference and not a reading, so the caption has to say
    /// both where it comes from and that a part-worked hour is marked whole.</summary>
    [Fact]
    public void The_grid_says_the_outline_is_the_readers_own_working_hours()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var grid = Squashed(pane.Find("[data-testid='dashboard-sessions-hours']").TextContent);

        Assert.Contains(
            "Outlined cells are your working hours from Settings, marked wherever the hour "
            + "overlaps them.",
            grid,
            StringComparison.Ordinal);
    }

    /// <summary>A cell's shade is one of four steps and answers "how many at once". The
    /// two durations it cannot carry reach a screen reader, not only a pointer.</summary>
    [Fact]
    public void A_cell_carries_the_two_durations_its_shade_cannot()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var cells = pane.Find("[data-testid='dashboard-sessions-hours']").QuerySelectorAll(".metric-heatmap__cell");

        Assert.Contains(
            "2h agent-active, 45m waiting",
            cells[9].QuerySelector(".sr-only")!.TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty grid is not seven rows of nothing. A scope that left no parsable record
    /// has nothing to draw, and drawing it anyway would claim seven days went by with no
    /// agent on them — a different fact from having nothing to read.
    /// </summary>
    [Fact]
    public void A_part_with_no_activity_draws_tiles_and_no_grid()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(
                new ReadySessionInsights(Insight() with { ActivityByHour = [] })));

        var pane = context.Render<DashboardPane>();

        Assert.NotNull(pane.Find("[data-testid='dashboard-sessions-active']"));
        Assert.Empty(pane.FindAll("[data-testid='dashboard-sessions-hours']"));

        // Both grids, or neither. They are the same seven days read two ways, so one
        // drawn without the other would be an axis with half an answer on it.
        Assert.Empty(pane.FindAll("[data-testid='dashboard-sessions-open']"));
    }

    /// <summary>The note owns the mixture the grid creates: one figure on a local clock
    /// among figures that are all UTC, and one that ignores the period control.</summary>
    [Fact]
    public void The_note_admits_the_grid_is_the_only_thing_here_in_local_time()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var note = Squashed(pane.Find("[data-testid='dashboard-sessions-note']").TextContent);

        Assert.Contains(
            "The grid below is the last 7 days on this machine's local clock, whichever period is "
            + "selected, and every other figure here is UTC.",
            note,
            StringComparison.Ordinal);
    }

    /// <summary>One is a session. A footnote that says "1 sessions" is a footnote the
    /// reader stops trusting about the arithmetic as well as the grammar.</summary>
    [Fact]
    public void One_unmeasurable_session_is_named_in_the_singular()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with { WithoutActivity = 1 })));

        var pane = context.Render<DashboardPane>();

        Assert.Contains(
            "Excludes 1 session that left no activity record",
            pane.Find("[data-testid='dashboard-sessions-active']").TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Overlapping sessions are summed rather than merged, so an hour in which three
    /// agents ran reads as three hours. Nothing on the surface can tell whether that
    /// happened, which is why the sentence is permanent: it is there on a complete
    /// reading with nothing else to excuse, not only when some other flag is raised.
    /// </summary>
    [Fact]
    public void The_active_time_tile_always_says_that_concurrent_sessions_are_counted_twice()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with { WithoutActivity = 0 })));

        var pane = context.Render<DashboardPane>();
        var tile = pane.Find("[data-testid='dashboard-sessions-active']").TextContent;

        Assert.Contains(
            "Concurrent sessions are counted separately, so this can exceed elapsed time.",
            tile,
            StringComparison.Ordinal);

        // And nothing to excuse means nothing excused: the conditional half stays away.
        Assert.DoesNotContain("Excludes", tile, StringComparison.Ordinal);
    }

    /// <summary>
    /// One machine on the list is one row in the breakdown, which is a restatement of
    /// the tiles above it rather than a comparison — the Rework part's rule.
    /// </summary>
    [Fact]
    public void A_breakdown_of_one_row_is_not_drawn_at_all()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                Breakdown = [new AssistantSessionRow("tower", "DEV-TOWER", 12, TimeSpan.FromHours(5), null)]
            })));

        var pane = context.Render<DashboardPane>();

        Assert.Empty(pane.FindAll("[data-testid='dashboard-sessions-breakdown']"));
    }

    [Fact]
    public void A_breakdown_with_something_to_compare_is_drawn_as_a_table()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var breakdown = pane.Find("[data-testid='dashboard-sessions-breakdown']");

        // Machine, because no machine is focused. The column is named after whichever
        // question the filter has not already answered.
        Assert.Contains("Machine", breakdown.TextContent, StringComparison.Ordinal);
        Assert.Contains("DEV-LAPTOP", breakdown.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two machines can be called the same thing, and the breakdown has to draw both.
    /// The table keys its rows on the machine's id for exactly this: keyed on the name,
    /// two siblings share a key, Blazor's keyed diff throws, and the whole surface goes
    /// with it rather than one row being wrong.
    /// </summary>
    [Fact]
    public void Two_machines_sharing_a_name_are_drawn_as_two_rows()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                Breakdown =
                [
                    new AssistantSessionRow("first", "DEV-TOWER", 8, TimeSpan.FromHours(4), null),
                    new AssistantSessionRow("second", "DEV-TOWER", 4, TimeSpan.FromHours(1), null)
                ]
            })));

        var pane = context.Render<DashboardPane>();

        // Rendered again on purpose. A duplicate key is not a first-render problem — the
        // first pass has nothing to diff against — it is what the second pass does with
        // two siblings claiming to be the same row, and the second pass is what a reader
        // gets from any interaction at all.
        pane.Render();

        Assert.Equal(2, pane.FindAll("[data-testid='dashboard-sessions-breakdown'] tbody tr").Count);
    }

    [Fact]
    public void Focusing_a_machine_names_the_breakdown_after_the_assistant_instead()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                Breakdown =
                [
                    new AssistantSessionRow("Claude", "Claude", 8, TimeSpan.FromHours(4), null),
                    new AssistantSessionRow("Copilot", "Copilot", 4, TimeSpan.FromHours(1), null)
                ]
            })));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-machine-filter'] select").Change(DashboardTestHost.MachineId);

        var breakdown = pane.Find("[data-testid='dashboard-sessions-breakdown']");

        Assert.Contains("Assistant", breakdown.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Machine", breakdown.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A capped read and an unreadable folder both make every figure above them less
    /// than the whole truth, and both have to say so. A capped number presented as a
    /// total is how a dashboard quietly stops being trusted.
    /// </summary>
    [Fact]
    public void The_sessions_note_admits_a_capped_read_and_an_unreadable_folder()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                Capped = true,
                Unreadable = ["Copilot"]
            })));

        var pane = context.Render<DashboardPane>();
        var note = Squashed(pane.Find("[data-testid='dashboard-sessions-note']").TextContent);

        Assert.Contains("Only Copilot records a repository against a session", note, StringComparison.Ordinal);

        // The number comes off the report rather than out of a constant beside the part,
        // so a cap the Sessions context changes reaches this sentence.
        Assert.Contains("Only the newest 100 sessions per assistant were read", note, StringComparison.Ordinal);
        Assert.Contains("so these figures are a floor", note, StringComparison.Ordinal);
        Assert.Contains("Copilot's folder could not be read.", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one chart on this part, and the sentence that says what a column is. Every
    /// other part on this surface draws its series; this one used to be the exception.
    /// </summary>
    [Fact]
    public void The_sessions_part_draws_a_column_per_week()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                SessionsPerWeek =
                [
                    new InsightPoint("W32", 3),
                    new InsightPoint("W33", 0),
                    new InsightPoint("W34", 9)
                ]
            })));

        var pane = context.Render<DashboardPane>();

        // A column per bucket, the quiet week among them: a chart that drew only the
        // weeks with something in them would put W32 next to W34 and read as two
        // consecutive weeks.
        Assert.Equal(3, pane.FindAll("[data-testid='dashboard-sessions-bars'] .metric-bars__column").Count);

        var bars = Squashed(pane.Find("[data-testid='dashboard-sessions-bars']").TextContent);

        // The bucketing rule, beside the columns it governs rather than left for a
        // reader to deduce from a total that does not add up.
        Assert.Contains("counted in the week they last moved", bars, StringComparison.Ordinal);

        // And the figures themselves, in the table the columns are only a picture of.
        Assert.Contains("W33", bars, StringComparison.Ordinal);
    }

    /// <summary>
    /// A capped read makes the columns a floor exactly as it makes the tiles one, and
    /// the note that already admits the cap has to cover them. A column read as a whole
    /// week's work is the same untruth as a total read as a total.
    /// </summary>
    [Fact]
    public void A_capped_read_says_the_columns_are_floors()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                Capped = true,
                SessionsPerWeek = [new InsightPoint("W34", 9)]
            })));

        var pane = context.Render<DashboardPane>();
        var note = Squashed(pane.Find("[data-testid='dashboard-sessions-note']").TextContent);

        Assert.Contains(
            "so these figures are a floor, the weekly columns included",
            note,
            StringComparison.Ordinal);
    }

    private static AssistantSessionsInsight Insight() =>
        new(
            Sessions: 12,
            ActiveTime: TimeSpan.FromHours(5),
            LastActivityAt: new DateTimeOffset(2026, 8, 19, 9, 30, 0, TimeSpan.Zero),
            WithoutActivity: 3,
            Capped: false,
            CapPerAssistant: 100,
            Unreadable: [],
            Breakdown:
            [
                new AssistantSessionRow("tower", "DEV-TOWER", 8, TimeSpan.FromHours(4), null),
                new AssistantSessionRow("laptop", "DEV-LAPTOP", 4, TimeSpan.FromHours(1), null)
            ])
        {
            Waiting = TimeSpan.FromHours(9),
            IdleAfter = TimeSpan.FromMinutes(5),
            ActivityByHour = Grid(),
            ActivityByDay =
            [
                // The three counts differ on purpose, and deliberately do not add up: a
                // session that ran across the edge of the working day is in both halves,
                // so a fixture where they summed would let a part that mixed the columns
                // up pass.
                .. Enumerable.Range(0, 7).Select(day =>
                    new ActivityDay(new DateOnly(2026, 8, 13).AddDays(day), day + 1, day, day + 1))
            ]
        };

    /// <summary>The full grid the part is handed in practice: seven dated days, all
    /// twenty-four hours each, quiet hours included. A fixture that omitted the quiet
    /// ones would let a part that drops them pass.</summary>
    private static IReadOnlyList<ActivityHour> Grid() =>
    [
        .. Enumerable.Range(0, 7).SelectMany(day => Enumerable.Range(0, 24).Select(hour =>
            new ActivityHour(
                new DateOnly(2026, 8, 13).AddDays(day),
                hour,
                hour == 9 ? 4 : 0,
                hour == 9 ? TimeSpan.FromHours(2) : TimeSpan.Zero,
                hour == 9 ? TimeSpan.FromMinutes(45) : TimeSpan.Zero,

                // Higher than the peak, because a session waiting on a prompt is on the go
                // and is not producing. Equal figures would let the two grids be wired to
                // the same measure without a test noticing.
                hour == 9 ? 7 : hour == 10 ? 2 : 0,

                // Mon-Fri 09:00-17:30 against a week that starts on a Thursday.
                WorkingHours.Default.Covers(new DateOnly(2026, 8, 13).AddDays(day).DayOfWeek, hour))))
    ];

    /// <summary>Sessions that can answer, so the part's own rendering — tiles, note and
    /// breakdown — can be asserted rather than only its unavailable state.</summary>
    private sealed class ReadySessionInsights(AssistantSessionsInsight insight) : ISessionInsights
    {
        public Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<AssistantSessionsInsight>.Ready(insight));

        public void Invalidate()
        {
        }
    }

    /// <summary>
    /// The defect this part was rebuilt for was a target nobody could see: full marks
    /// came from a constant, so the card read "441 of 6" and there was nowhere on
    /// screen to find out why. A derived target is only better if the reader can
    /// trace it, so the note names the block it came from.
    /// </summary>
    [Fact]
    public void The_score_note_names_the_target_and_where_it_came_from()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(new ReadyProductivityInsights(Score())));

        var pane = context.Render<DashboardPane>();
        var note = Squashed(pane.Find("[data-testid='dashboard-score-note']").TextContent);

        Assert.Contains("a quarter above your own best four weeks", note, StringComparison.Ordinal);

        // The figure it works out to, the record behind it, and when that record was
        // set — all three, because any two of them leave the third unarguable.
        Assert.Contains("380 merged pull requests", note, StringComparison.Ordinal);
        Assert.Contains("304", note, StringComparison.Ordinal);
        Assert.Contains("12 May 2026", note, StringComparison.Ordinal);
        Assert.Contains("09 Jun 2026", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// No history, no target, and three of the seven inputs simply absent. A reader
    /// who cannot see that throughput dropped out reads what is left as a score of
    /// everything.
    /// </summary>
    [Fact]
    public void The_score_note_says_when_volume_is_not_being_scored()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(
                new ReadyProductivityInsights(Score() with { Target = null })));

        var pane = context.Render<DashboardPane>();
        var note = Squashed(pane.Find("[data-testid='dashboard-score-note']").TextContent);

        Assert.Contains(
            "Merged pull requests, issues closed and assistant sessions are not being scored",
            note,
            StringComparison.Ordinal);
        Assert.Contains("could not be read, or there is not enough of it yet", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sessions input refuses two of the surface's three dimensions and is worth
    /// the least of the seven, and all of that is said permanently rather than
    /// conditionally: the conditions are invisible from the card, so a reader could
    /// not tell a missing sentence from an absent caveat.
    /// </summary>
    [Fact]
    public void The_score_note_refuses_the_repository_dimension_for_sessions()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(new ReadyProductivityInsights(Score())));

        var pane = context.Render<DashboardPane>();
        var note = Squashed(pane.Find("[data-testid='dashboard-score-note']").TextContent);

        Assert.Contains("count effort rather than output", note, StringComparison.Ordinal);
        // Not a weight count. The card renormalises the shares over the inputs that
        // actually had something to read, so a note naming a fixed denominator
        // contradicts the percentage printed beside the row whenever one drops out.
        Assert.Contains("carry the least weight here", note, StringComparison.Ordinal);
        Assert.Contains("rises when another input", note, StringComparison.Ordinal);
        Assert.Contains("while one repository is in focus", note, StringComparison.Ordinal);
        Assert.Contains("machine filter does not move this figure", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// A count read from a listing that stopped early is a floor, and every part built
    /// on that listing has to say so. A capped number presented as a total is how a
    /// dashboard quietly stops being trusted.
    /// </summary>
    [Fact]
    public void A_truncated_window_says_its_figures_are_a_floor()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(
                new ReadyProductivityInsights(Score() with { Complete = false })));

        var pane = context.Render<DashboardPane>();

        foreach (var part in new[] { "dashboard-score", "dashboard-headline", "dashboard-rework", "dashboard-trend" })
        {
            var note = Squashed(pane.Find($"[data-testid='{part}-note']").TextContent);

            Assert.Contains("could not be read", note, StringComparison.Ordinal);
            Assert.Contains("floor", note, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A score with the whole composition behind it: the three volume inputs read
    /// against the reader's own record, and the four proportions.
    /// </summary>
    private static ProductivityScoreInsight Score() =>
        new(
            72m,
            [
                new ProductivityScoreInput("Pull requests merged", 304m, 380m, 3m),
                new ProductivityScoreInput("Issues closed", 72m, 90m, 2m),
                new ProductivityScoreInput("First review within a day", 40m, 50m, 2m),
                new ProductivityScoreInput("Merged without post-review churn", 30m, 50m, 1m),
                new ProductivityScoreInput("Merged under 400 changed lines", 20m, 44m, 1m),
                new ProductivityScoreInput("Merged touching 10 files or fewer", 24m, 44m, 1m),
                new ProductivityScoreInput("Assistant sessions", 40m, 75m, 1m)
            ])
        {
            Target = new ProductivityTarget(
                new DateTimeOffset(2026, 5, 12, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero),
                304,
                380m)
        };

    /// <summary>
    /// Productivity that can answer, so the notes the parts write about their own
    /// figures can be read rather than only their unavailable state. Every part gets
    /// the same completeness flag, because it is one report behind all four.
    /// </summary>
    private sealed class ReadyProductivityInsights(ProductivityScoreInsight score) : IProductivityInsights
    {
        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityHeadline>.Ready(
                new ProductivityHeadline(304, 72, 0.2m, TimeSpan.FromHours(5), [], [], [])
                {
                    Complete = score.Complete
                }));

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityScoreInsight>.Ready(score));

        public Task<InsightResult<ProductivityTrend>> GetTrendAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityTrend>.Ready(
                new ProductivityTrend(
                    [new InsightSeries("backlog", [new InsightPoint("W33", 40m), new InsightPoint("W34", 55m)])],
                    null)
                {
                    Complete = score.Complete
                }));

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ReworkInsight>.Ready(
                new ReworkInsight(6, 30, 12, 2, 9, true, [new InsightPoint("W34", 3m)], [])
                {
                    Complete = score.Complete
                }));

        public void Invalidate(DashboardScope scope)
        {
        }
    }

    /// <summary>Markup wraps a note across several source lines, so the text arrives
    /// with newlines and runs of spaces in it. Comparing a sentence needs those
    /// collapsed, or the assertion is about the indentation.</summary>
    private static string Squashed(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void The_window_can_be_narrowed_to_four_weeks()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-window-4']").Click();

        Assert.Contains(DashboardPeriod.FourWeeks, productivity.Scopes.Select(scope => scope.Period));
    }

    [Fact]
    public void Refreshing_a_part_asks_its_source_again()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-headline-refresh']").Click();

        Assert.True(productivity.Invalidations > 0);
    }

    /// <summary>
    /// The surface is deliberately not configurable — no layout editing, no adding or
    /// removing a part, nothing persisted. This is the guard against that quietly
    /// changing: the only controls on the panel are the filter, the per-part refresh,
    /// and the close button.
    /// </summary>
    [Fact]
    public void The_panel_offers_no_way_to_configure_itself()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();

        // Phrases rather than single words. A bare "configure" would match the
        // headline's own note about configured repositories, which is the pane saying
        // where its figures come from — the opposite of an affordance.
        var forbidden = new[]
        {
            "customise",
            "customize",
            "add widget",
            "remove widget",
            "edit layout",
            "reset layout",
            "rearrange"
        };

        foreach (var phrase in forbidden)
        {
            Assert.DoesNotContain(phrase, pane.Markup, StringComparison.OrdinalIgnoreCase);
        }

        // Every control on the panel is accounted for, by name and then by count. The
        // count is the part that bites: a control added later without a reason lands
        // here rather than on screen unnoticed.
        Assert.Single(pane.FindAll("[data-testid='dashboard-repository-filter'] select"));
        Assert.Single(pane.FindAll("[data-testid='dashboard-machine-filter'] select"));
        Assert.Equal(2, pane.FindAll("[data-testid='dashboard-window-filter'] button").Count);
        Assert.Equal(8, pane.FindAll("[data-testid$='-refresh']").Count);
        Assert.Single(pane.FindAll("[aria-label='Close dashboard']"));

        var controls = pane.FindAll("button, select, input, textarea");

        // One close, two filter selects, two window buttons, eight refreshes.
        Assert.Equal(1 + 2 + 2 + 8, controls.Count);
    }

    /// <summary>
    /// The pane holds nothing that outlives being closed. A saved arrangement is the
    /// thing that falls out of step with the code, so there is none to save.
    /// </summary>
    [Fact]
    public void Closing_and_reopening_the_pane_returns_to_the_default_scope()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var first = context.Render<DashboardPane>();
        first.Find("[data-testid='dashboard-repository-filter'] select").Change("backlog-ide");

        productivity.Scopes.Clear();

        var second = context.Render<DashboardPane>();
        _ = second;

        Assert.All(productivity.Scopes, scope => Assert.True(scope.IsAllRepositories));
    }

    [Fact]
    public void The_close_button_reports_that_it_was_pressed_and_nothing_else()
    {
        using var context = Context();

        var closed = 0;
        var pane = context.Render<DashboardPane>(parameters =>
            parameters.Add(pane => pane.OnClose, () => closed++));

        pane.Find("[aria-label='Close dashboard']").Click();

        Assert.Equal(1, closed);
    }

    /// <summary>The shell finds the surface by this id and test id; renaming either
    /// would break the takeover without breaking the pane.</summary>
    [Fact]
    public void The_panel_keeps_the_identifiers_the_shell_addresses_it_by()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var panel = pane.Find("[data-testid='dashboard-panel']");

        Assert.Equal("dashboard-pane", panel.Id);
        Assert.Equal("dashboard-title", panel.GetAttribute("aria-labelledby"));
    }

    /// <summary>
    /// The header is the library SectionHeader now, so what is worth holding is
    /// that it still renders this pane's own class names - app.css styles all five
    /// and did not move.
    /// </summary>
    [Fact]
    public void The_header_is_the_shared_component_wearing_this_panes_classes()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var header = pane.Find(".dashboard-panel__header");

        SectionHeaderAdoptionTests.AssertPaneHeader(header, "dashboard-panel", "dashboard-title");
        SectionHeaderAdoptionTests.AssertPaneHeaderActions(header, "dashboard-panel");
        Assert.NotNull(header.QuerySelector(".dashboard-panel__header-actions button"));
    }

    private static BunitContext Context(Action<IServiceCollection>? configure = null)
    {
        var context = new BunitContext();

        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        configure?.Invoke(context.Services);

        return context;
    }

    private sealed class RecordingProductivityInsights : IProductivityInsights
    {
        public List<DashboardScope> Scopes { get; } = [];

        public int Invalidations { get; private set; }

        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(InsightResult<ProductivityHeadline>.Unavailable("Not configured."));
        }

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(InsightResult<ProductivityScoreInsight>.Unavailable("Not configured."));
        }

        public Task<InsightResult<ProductivityTrend>> GetTrendAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(InsightResult<ProductivityTrend>.Unavailable("Not configured."));
        }

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(InsightResult<ReworkInsight>.Unavailable("Not configured."));
        }

        public void Invalidate(DashboardScope scope) => Invalidations++;
    }

    /// <summary>
    /// Productivity that yields before answering, so the continuation genuinely
    /// resumes rather than running through synchronously. Every real provider behaves
    /// this way; a double returning <c>Task.FromResult</c> does not, which is what let
    /// a dispatcher bug through.
    /// </summary>
    private sealed class YieldingProductivityInsights : IProductivityInsights
    {
        public async Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return InsightResult<ProductivityHeadline>.Unavailable("Answered after yielding.");
        }

        public async Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return InsightResult<ProductivityScoreInsight>.Unavailable("Answered after yielding.");
        }

        public async Task<InsightResult<ProductivityTrend>> GetTrendAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return InsightResult<ProductivityTrend>.Unavailable("Answered after yielding.");
        }

        public async Task<InsightResult<ReworkInsight>> GetReworkAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return InsightResult<ReworkInsight>.Unavailable("Answered after yielding.");
        }

        public void Invalidate(DashboardScope scope)
        {
        }
    }

    /// <summary>Sessions that remembers every scope it was asked for, so a test can say
    /// which filters reached it and which did not.</summary>
    private sealed class RecordingSessionInsights : ISessionInsights
    {
        public List<DashboardScope> Scopes { get; } = [];

        public Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(InsightResult<AssistantSessionsInsight>.Unavailable("Not configured."));
        }

        public void Invalidate()
        {
        }
    }

    private sealed class RecordingCostInsights : ICostInsights
    {
        public int Calls { get; private set; }

        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(InsightResult<SpendThisMonthInsight>.Unavailable("Not configured."));
        }

        public Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(InsightResult<SpendTrendInsight>.Unavailable("Not configured."));
        }

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(InsightResult<SpendByModelInsight>.Unavailable("Not configured."));
        }

        public void Invalidate()
        {
        }
    }

    /// <summary>Cost that can answer, so a test can prove one refused source does not
    /// silence a working one.</summary>
    private sealed class ReadyCostInsights : ICostInsights
    {
        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Ready(new SpendThisMonthInsight(
            [
                new MonthlySpend(
                    SpendProvider.Claude,
                    new DashboardMoney(12.34m, "USD"),
                    Allowance: null,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 19),
                    IsEstimate: true)
            ])));

        public Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendTrendInsight>.Ready(new SpendTrendInsight(
                [new InsightSeries("Claude", [new InsightPoint("Aug 26", 12.34m)])],
                "USD",
                SpendBucket.Month)));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByModelInsight>.Ready(new SpendByModelInsight(
                [new InsightRow("opus", 1_000, new DashboardMoney(12.34m, "USD"), "Claude")])));

        public void Invalidate()
        {
        }
    }
}
