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

    /// <summary>
    /// The repository is the shell's to choose: the header carries one scope for the
    /// whole screen, and a second select here was two repository filters on one
    /// screen answering differently. So the pane offers no repository control, and
    /// with nothing handed in it reads every repository.
    /// </summary>
    [Fact]
    public void The_pane_offers_no_repository_control_and_reads_all_repositories_by_default()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();

        Assert.Empty(pane.FindAll("[data-testid='dashboard-repository-filter']"));
        Assert.DoesNotContain("All repositories", pane.Markup, StringComparison.Ordinal);
        Assert.All(productivity.Scopes, scope => Assert.True(scope.IsAllRepositories));
    }

    /// <summary>
    /// The repository handed in has to reach the parts, or the header's scope is a
    /// control that silently drives half a page — which is the usual way a dashboard
    /// goes stale.
    /// </summary>
    [Fact]
    public void Focusing_a_repository_reaches_the_productivity_parts()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        FocusRepository(pane, "backlog-ide");

        Assert.Contains(productivity.Scopes, scope => scope.Repositories.Contains("backlog-ide") && !scope.IsAllRepositories);
    }

    /// <summary>
    /// The header's scope holds several repositories at once, and the parts get the
    /// whole set in the order it was taken — the anchor first, because the trend
    /// holds that one up against the rest.
    /// </summary>
    [Fact]
    public void Several_repositories_reach_the_parts_as_one_focus_in_the_order_taken()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        FocusRepository(pane, "backlog-ide", "backlog");

        var focus = productivity.Scopes[^1].Repositories;
        Assert.Equal(["backlog-ide", "backlog"], focus.Aliases);
        Assert.Equal("backlog-ide", focus.Anchor);
    }

    /// <summary>
    /// A cleared scope is an empty list, and the pane reads it as all repositories
    /// rather than as a focus on nothing.
    /// </summary>
    [Fact]
    public void An_empty_scope_reads_as_all_repositories()
    {
        var productivity = new RecordingProductivityInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(productivity));

        var pane = context.Render<DashboardPane>();
        FocusRepository(pane, "backlog-ide");
        FocusRepository(pane);

        Assert.True(productivity.Scopes[^1].IsAllRepositories);
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

        FocusRepository(pane, "backlog-ide");

        Assert.Equal(afterFirstRender, costs.Calls);
    }

    /// <summary>
    /// No explanatory prose on the face of the pane: not a subtitle under the title,
    /// not a paragraph under a section heading, not a line under a part's title. The
    /// figures are what the dashboard shows; what they cover is behind each part's
    /// info mark, and the test below reads it there.
    /// </summary>
    [Fact]
    public void The_pane_wears_no_explanatory_text_on_its_face()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();

        Assert.Empty(pane.FindAll(".dashboard-panel__subtitle"));
        Assert.Empty(pane.FindAll(".dashboard-section__note"));
        Assert.Empty(pane.FindAll(".dashboard-part__note"));
        Assert.Empty(pane.FindAll("p.dashboard-part__note, .dashboard-part__heading > p"));
        Assert.DoesNotContain("does not change anything in this section", pane.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The filter refusals — the spend part says no provider reports spend per
    /// repository, the sessions part says only Copilot records one — are the one thing a
    /// reader could get wrong about this dashboard, so they are not dropped with the
    /// prose: each part with a note carries it behind the library's info mark, a named
    /// button beside the title whose tooltip is what describes it.
    /// </summary>
    [Fact]
    public void Each_part_keeps_its_note_behind_an_info_mark_beside_its_title()
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
            var trigger = pane.Find($"[data-testid='{part}-info']");
            var note = pane.Find($"[data-testid='{part}-note']");
            var title = pane.Find($"[data-testid='{part}'] .dashboard-part__title");

            Assert.Equal("BUTTON", trigger.TagName);
            Assert.Equal($"About {title.TextContent}", trigger.GetAttribute("aria-label"));
            Assert.Equal("tooltip", note.GetAttribute("role"));
            Assert.Equal(note.Id, trigger.GetAttribute("aria-describedby"));
            // Beside the heading, not inside it: the heading's own name stays the title.
            Assert.Equal(title.ParentElement, trigger.ParentElement?.ParentElement);
            Assert.False(string.IsNullOrWhiteSpace(note.TextContent));
        }

        // Main widened this note from two providers to three while the marks were
        // being built; what is pinned is that the refusal still reaches a reader
        // through the mark, not the sentence's current arithmetic.
        Assert.Contains(
            "No provider reports spend per repository",
            Squashed(pane.Find("[data-testid='dashboard-spend-month-note']").TextContent),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Each section is a heading with the library's fold trigger inside it — the
    /// accordion shape, so a screen reader walking headings still finds the three — and
    /// every one opens expanded, because a dashboard that opens folded shows nothing.
    /// </summary>
    [Theory]
    [InlineData("dashboard-productivity", "dashboard-productivity-toggle", "Productivity")]
    [InlineData("dashboard-sessions-section", "dashboard-sessions-toggle", "Sessions")]
    [InlineData("dashboard-cost", "dashboard-cost-toggle", "Cost")]
    public void Each_section_heading_is_a_fold_trigger_that_starts_open(string section, string toggle, string title)
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var heading = pane.Find($"[data-testid='{section}'] h3.dashboard-section__title");
        var trigger = pane.Find($"[data-testid='{toggle}']");

        Assert.Equal("H3", trigger.ParentElement?.TagName);
        Assert.Equal("dashboard-section__title", trigger.ParentElement?.ClassName);
        Assert.Contains(title, heading.TextContent, StringComparison.Ordinal);
        Assert.Equal("true", trigger.GetAttribute("aria-expanded"));
        Assert.False(pane.Find($"[data-testid='{section}'] .fold__region").HasAttribute("hidden"));
    }

    /// <summary>
    /// Folding hides the parts and unfolding brings them back — the same ones, not a
    /// fresh render. FoldControl keeps its region in the DOM while hidden, so a part's
    /// figures survive the fold and nothing re-fetches on the way back.
    /// </summary>
    [Fact]
    public void Folding_a_section_hides_its_parts_without_re_fetching_them()
    {
        var costs = new RecordingCostInsights();
        using var context = Context(configure: services => services.AddSingleton<ICostInsights>(costs));

        var pane = context.Render<DashboardPane>();
        var afterFirstRender = costs.Calls;
        var region = pane.Find("[data-testid='dashboard-cost'] .fold__region");

        pane.Find("[data-testid='dashboard-cost-toggle']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.True(pane.Find("[data-testid='dashboard-cost'] .fold__region").HasAttribute("hidden"));
            Assert.Equal("false", pane.Find("[data-testid='dashboard-cost-toggle']").GetAttribute("aria-expanded"));
        });
        Assert.NotNull(pane.Find("[data-testid='dashboard-spend-month']"));

        pane.Find("[data-testid='dashboard-cost-toggle']").Click();

        pane.WaitForAssertion(() =>
            Assert.False(pane.Find("[data-testid='dashboard-cost'] .fold__region").HasAttribute("hidden")));
        Assert.Equal(afterFirstRender, costs.Calls);
    }

    /// <summary>One section's fold is its own: closing Cost leaves Productivity and
    /// Sessions where they were.</summary>
    [Fact]
    public void Folding_one_section_leaves_the_others_open()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();

        pane.Find("[data-testid='dashboard-cost-toggle']").Click();

        pane.WaitForAssertion(() =>
            Assert.True(pane.Find("[data-testid='dashboard-cost'] .fold__region").HasAttribute("hidden")));
        Assert.False(pane.Find("[data-testid='dashboard-productivity'] .fold__region").HasAttribute("hidden"));
        Assert.False(pane.Find("[data-testid='dashboard-sessions-section'] .fold__region").HasAttribute("hidden"));
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
    /// Moving a filter starts the next fetch before it cancels the last one. The order
    /// matters because the sessions read is one shared entry behind the module's cache
    /// that stops when its last waiter leaves: withdrawn first and joined second, a
    /// twelve-week parse that was nearly done would be thrown away and started again
    /// from nothing, on the very gesture — a filter moved during the first load — that
    /// this ordering exists for.
    /// </summary>
    [Fact]
    public void Moving_a_filter_joins_the_next_fetch_before_it_withdraws_the_last()
    {
        var sessions = new TokenOrderSessionInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(sessions));

        var pane = context.Render<DashboardPane>();
        pane.Find("[data-testid='dashboard-machine-filter'] select").Change(DashboardTestHost.MachineId);

        Assert.True(sessions.Calls >= 2, "The filter change never reached the sessions part.");
        Assert.DoesNotContain(true, sessions.PredecessorWithdrawnOnArrival);
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

    /// <summary>
    /// The mean is over the counted sessions and the tile says how many that is. A
    /// figure of 7.5 over 8 of 12 sessions presented as "prompts per session" without the
    /// denominator would read as a fact about all twelve, four of which said nothing.
    /// </summary>
    [Fact]
    public void The_prompts_tile_shows_the_mean_and_says_how_many_sessions_it_covers()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var tile = Squashed(pane.Find("[data-testid='dashboard-sessions-prompts']").TextContent);

        Assert.Contains("Prompts per session", tile, StringComparison.Ordinal);
        Assert.Contains("7.5", tile, StringComparison.Ordinal);
        Assert.Contains("over 8 of 12 sessions", tile, StringComparison.Ordinal);
        Assert.Contains("Copilot records no prompt count", tile, StringComparison.Ordinal);

        Assert.NotNull(pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-prompts-per-session']"));
    }

    /// <summary>
    /// Nothing to average is a dash and no chart, not a zero and a flat one. Zero would
    /// say the person opened sessions and never spoke; the only thing a window of
    /// uncounted sessions supports is that there was nothing to count from.
    /// </summary>
    [Fact]
    public void A_part_with_no_counted_session_shows_no_prompt_figure_and_no_prompt_chart()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(
                new ReadySessionInsights(Insight() with
                {
                    PromptsPerSession = null,
                    SessionsWithPrompts = 0,
                    PromptsPerSessionPerWeek = [new("W33", 0m), new("W34", 0m)]
                })));

        var pane = context.Render<DashboardPane>();
        var tile = Squashed(pane.Find("[data-testid='dashboard-sessions-prompts']").TextContent);

        Assert.Contains("—", tile, StringComparison.Ordinal);
        Assert.DoesNotContain("over 0 of", tile, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='dashboard-sessions-weekly-bars-toggle-prompts-per-session']"));
    }

    /// <summary>
    /// The two figures read off the session records: output tokens with the other
    /// three counts and the denominator in the hint, and pull requests counted once.
    /// Both say they are partial when fewer sessions could say than the tile beside
    /// them counts.
    /// </summary>
    [Fact]
    public void Tokens_and_pull_requests_are_tiles_that_say_how_many_sessions_they_cover()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(WithRecords(Insight()))));

        var pane = context.Render<DashboardPane>();

        var tokens = pane.Find("[data-testid='dashboard-sessions-tokens']");
        Assert.Contains("45.0K", tokens.TextContent, StringComparison.Ordinal);
        Assert.Contains("From 8 of 12 sessions — the figure is partial.", tokens.OuterHtml, StringComparison.Ordinal);
        Assert.Contains("1,200 input", tokens.OuterHtml, StringComparison.Ordinal);

        var prs = pane.Find("[data-testid='dashboard-sessions-pull-requests']");
        Assert.Contains("3", Squashed(prs.TextContent), StringComparison.Ordinal);
        Assert.Contains("each counted once", prs.OuterHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tokens are their own chart and never a line on the hours chart, cut by model
    /// until the reader asks for repositories; pull requests are a chart of their own
    /// by repository.
    /// </summary>
    [Fact]
    public void Tokens_are_a_chart_of_their_own_cut_by_model_or_by_repository()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(WithRecords(Insight()))));

        var pane = context.Render<DashboardPane>();

        var bars = pane.Find("[data-testid='dashboard-sessions-tokens-bars']");
        Assert.Contains("Output tokens per week, by model", bars.TextContent, StringComparison.Ordinal);
        Assert.Contains("claude-opus-5-5", bars.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("tokens", pane.Find("[data-testid='dashboard-sessions-weekly-bars']").TextContent, StringComparison.OrdinalIgnoreCase);

        pane.Find("[data-testid='dashboard-sessions-tokens-by-repository']").Click();

        bars = pane.Find("[data-testid='dashboard-sessions-tokens-bars']");
        Assert.Contains("Output tokens per week, by repository", bars.TextContent, StringComparison.Ordinal);
        Assert.Contains("backlog", bars.TextContent, StringComparison.Ordinal);

        var prs = pane.Find("[data-testid='dashboard-sessions-pull-requests-bars']");
        Assert.Contains("Pull requests linked per week, by repository", prs.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_part_where_no_session_recorded_usage_shows_a_dash_and_no_token_chart()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        Assert.Contains("—", pane.Find("[data-testid='dashboard-sessions-tokens']").TextContent, StringComparison.Ordinal);
        Assert.Contains("—", pane.Find("[data-testid='dashboard-sessions-pull-requests']").TextContent, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='dashboard-sessions-tokens-bars']"));
        Assert.Empty(pane.FindAll("[data-testid='dashboard-sessions-pull-requests-bars']"));
    }

    /// <summary>
    /// A wall and a fall-back are different news: the limit tile says which refusals had
    /// overage to fall back to and, for the rest, the reason the assistant gave in words.
    /// </summary>
    [Fact]
    public void The_limit_tile_says_what_overage_did_about_the_refusals()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                LimitHits = new LimitHitCounts(3, 1)
                {
                    OnOverage = 1,
                    Walled = [new LimitWall("org_spend_cap_reached", 2)],
                    OverageUnrecorded = 1
                }
            })));

        var pane = context.Render<DashboardPane>();

        Assert.Contains(
            "Overage: 1 fell back to overage · 2 walled — org spend cap reached · 1 said nothing about overage.",
            Squashed(pane.Find("[data-testid='dashboard-sessions-limit-hits']").TextContent),
            StringComparison.Ordinal);
    }

    /// <summary>The sample insight with the record-side figures filled in: tokens and
    /// pull requests over fewer sessions than the count, so "partial" has to show.</summary>
    private static AssistantSessionsInsight WithRecords(AssistantSessionsInsight insight) =>
        insight with
        {
            Tokens = new TokenTotals(Output: 45_000, Input: 1_200, CacheRead: 900_000, CacheWrite: 30_000),
            SessionsWithUsage = 8,
            TokensByModel = [new WeeklyBand("claude-opus-5-5", null, [new("W33", 20_000m), new("W34", 25_000m)], 45_000m)],
            TokensByRepository =
            [
                new WeeklyBand("backlog", RepositoryBandKind.Configured, [new("W33", 5_000m), new("W34", 25_000m)], 30_000m),
                new WeeklyBand(RepositoryWeekly.UnrecordedName, RepositoryBandKind.Unrecorded, [new("W33", 15_000m), new("W34", 0m)], 15_000m)
            ],
            PullRequests = 3,
            SessionsWithPullRequestRecord = 8,
            PullRequestsByRepository = [new WeeklyBand("backlog", RepositoryBandKind.Configured, [new("W33", 1m), new("W34", 2m)], 3m)]
        };

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
    public void The_grid_says_which_week_it_is_and_a_local_clock()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var grid = Squashed(pane.Find("[data-testid='dashboard-sessions-hours']").TextContent);

        // The caption names the measure; the mark beside it carries the rest. Both are
        // inside the grid, which is what this reads — a reader meets the week it is
        // showing, and the refusals, at the thing that will not move either way.
        Assert.Contains("Sessions producing at once, by hour", grid, StringComparison.Ordinal);
        Assert.Contains("Your local clock, the week of W34, picked above, as calendar days", grid, StringComparison.Ordinal);
        Assert.Contains("A cell in the error colour is an hour Claude had walled you out for the 5-hour limit", grid, StringComparison.Ordinal);
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
                new ReadySessionInsights(Insight() with { ActivityByHour = [], Grids = [] })));

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

        Assert.Contains("Claude records no repository, so its sessions are placed only by a working folder inside a registered clone", note, StringComparison.Ordinal);

        // No per-assistant number: the read is everything inside the period, and the
        // sentence is for a source that could not reach that far back.
        Assert.Contains("Not every session in this period could be read", note, StringComparison.Ordinal);
        Assert.DoesNotContain("newest", note, StringComparison.Ordinal);
        Assert.Contains("so these figures are a floor", note, StringComparison.Ordinal);
        Assert.Contains("Copilot's folder could not be read.", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// A column per bucket, the quiet week among them, and the sentence that says what
    /// a week is. A chart that drew only the weeks with something in them would put
    /// W32 next to W34 and read as two consecutive weeks.
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
                ],
                ActiveTimePerWeek = [new("W32", 1m), new("W33", 0m), new("W34", 2m)],
                WaitingPerWeek = [new("W32", 1m), new("W33", 0m), new("W34", 2m)]
            })));

        var pane = context.Render<DashboardPane>();

        Assert.Equal(3, pane.FindAll("[data-testid='dashboard-sessions-weekly-bars'] .metric-stacked-bars__column").Count);

        var chart = Squashed(pane.Find("[data-testid='dashboard-sessions-weekly-bars']").TextContent);

        // Both bucketing rules, beside the columns they govern rather than left for a
        // reader to deduce from a total that does not add up.
        Assert.Contains("counted in the week the session last moved", chart, StringComparison.Ordinal);
        Assert.Contains("placed in the week they were worked", chart, StringComparison.Ordinal);

        // And the figures themselves, in the table the columns are only a picture of.
        Assert.Contains("W33", chart, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every tile cut by week in one chart: the two hour tiles as the stack, the four
    /// counts as lines on a scale of their own, the legend on top because it is the
    /// filter for both charts. Waiting starts off; the other five are on, each carrying
    /// its own tile's figure; the total row is the columns' alone.
    /// </summary>
    [Fact]
    public void The_sessions_part_stacks_the_hours_and_draws_the_counts_as_lines_over_them()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        var chart = pane.Find("[data-testid='dashboard-sessions-weekly-bars']");

        Assert.Contains("metric-stacked-bars--stacked", chart.ClassList);
        Assert.Equal(2, chart.QuerySelectorAll(".metric-stacked-bars__column").Length);

        // Waiting is off from the start, so one band per column and four lines.
        Assert.All(
            chart.QuerySelectorAll(".metric-stacked-bars__column"),
            column => Assert.Single(column.QuerySelectorAll(".metric-stacked-bars__segment")));
        Assert.Equal(4, chart.QuerySelectorAll("polyline.metric-stacked-bars__line").Length);
        Assert.NotNull(chart.QuerySelector(".metric-stacked-bars__scale--right"));
        Assert.Equal("false", chart.QuerySelector("[data-testid='dashboard-sessions-weekly-bars-toggle-waiting-for-a-prompt']")!.GetAttribute("aria-pressed"));

        // The legend sits above the plot: it is the filter for the chart below too.
        var legend = chart.QuerySelector(".metric-stacked-bars__legend")!;
        Assert.True(legend.CompareDocumentPosition(chart.QuerySelector(".metric-stacked-bars__plot")!).HasFlag(AngleSharp.Dom.DocumentPositions.Following));

        var text = Squashed(chart.TextContent);

        Assert.Contains("on the right-hand scale, which is theirs alone", text, StringComparison.Ordinal);
        Assert.Contains("The legend is the filter for this chart and the one below", text, StringComparison.Ordinal);
        Assert.Contains("Calendar weeks from Monday, because no usage reset is known", text, StringComparison.Ordinal);
        Assert.Contains("Agent-hours on the go", text, StringComparison.Ordinal);

        foreach (var toggle in new[] { "producing", "sessions", "prompts-per-session", "sessions-at-once", "agents-at-once" })
        {
            Assert.Equal("true", chart.QuerySelector($"[data-testid='dashboard-sessions-weekly-bars-toggle-{toggle}']")!.GetAttribute("aria-pressed"));
        }

        Assert.Contains("5h", chart.QuerySelector("[data-testid='dashboard-sessions-weekly-bars-toggle-producing']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("9h", chart.QuerySelector("[data-testid='dashboard-sessions-weekly-bars-toggle-waiting-for-a-prompt']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("12", chart.QuerySelector("[data-testid='dashboard-sessions-weekly-bars-toggle-sessions']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("peak 11", chart.QuerySelector("[data-testid='dashboard-sessions-weekly-bars-toggle-agents-at-once']")!.TextContent, StringComparison.Ordinal);

        // The total is the hours shown — producing alone, with waiting off.
        Assert.Equal(["3h", "2h"], [.. chart.QuerySelectorAll("tfoot td").Select(cell => cell.TextContent)]);
    }

    /// <summary>The week sentence names the boundary in force, in the words the reader
    /// would use: their setting, or what the assistant reported.</summary>
    [Fact]
    public void The_weekly_chart_says_which_reset_cuts_its_weeks()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                Week = new UsageWeekInfo(WeekSource.Configured, "Monday 14:00")
            })));

        var pane = context.Render<DashboardPane>();
        var text = Squashed(pane.Find("[data-testid='dashboard-sessions-weekly-bars']").TextContent);

        Assert.Contains("Weeks run from Monday 14:00 to the next, your configured usage reset", text, StringComparison.Ordinal);
    }

    /// <summary>Switching a measure off in the legend takes it out of the stack: the
    /// toggle is the component's, but the part is where a reader meets it.</summary>
    [Fact]
    public void A_measure_switched_off_in_the_legend_leaves_the_weekly_stack()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-agents-at-once']").Click();
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-waiting-for-a-prompt']").Click();

        var chart = pane.Find("[data-testid='dashboard-sessions-weekly-bars']");

        // One line fewer, one band more: waiting came on as agents went off.
        Assert.Equal(3, chart.QuerySelectorAll("polyline.metric-stacked-bars__line").Length);
        Assert.All(
            chart.QuerySelectorAll(".metric-stacked-bars__column"),
            column => Assert.Equal(2, column.QuerySelectorAll(".metric-stacked-bars__segment").Length));

        // The same choice reaches the repository stack. With the three remaining counts
        // switched off too, its bands are hours alone and read as durations: producing
        // plus waiting per week.
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-sessions']").Click();
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-prompts-per-session']").Click();
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-sessions-at-once']").Click();

        var repositories = pane.Find("[data-testid='dashboard-sessions-repository-bars']");
        Assert.Equal(["9h", "5h"], [.. repositories.QuerySelectorAll("tfoot td").Select(cell => cell.TextContent)]);
        Assert.Contains("Producing + Waiting for a prompt per week, by repository", Squashed(repositories.TextContent), StringComparison.Ordinal);
        Assert.Contains("The measures switched on above, added up per repository and stacked", Squashed(repositories.TextContent), StringComparison.Ordinal);
        Assert.Contains("All repositories, agent-hours", Squashed(repositories.TextContent), StringComparison.Ordinal);
    }

    /// <summary>
    /// The repository chart stacks what the shared legend has on — producing alone at
    /// first — one band per repository with the unrecorded row drawn and named for
    /// what it is. Its legend is a key, not a filter: the repositories are the header's
    /// scope chips, and a second control here would be a second answer.
    /// </summary>
    [Fact]
    public void The_repository_chart_stacks_the_selected_measures_by_repository_with_a_key_and_no_filter_of_its_own()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        Assert.Empty(pane.FindAll("[data-testid='dashboard-sessions-measure']"));

        var chart = pane.Find("[data-testid='dashboard-sessions-repository-bars']");

        Assert.All(
            chart.QuerySelectorAll(".metric-stacked-bars__column"),
            column => Assert.Equal(2, column.QuerySelectorAll(".metric-stacked-bars__segment").Length));

        // A key: the same entries, nothing to press.
        var legend = chart.QuerySelector(".metric-stacked-bars__legend")!;
        Assert.Equal("UL", legend.TagName);
        Assert.Empty(legend.QuerySelectorAll("button"));

        var text = Squashed(chart.TextContent);

        // Everything but waiting is on from the start, so a band is a sum across units
        // and the chart says so rather than printing it as a figure of something.
        Assert.Contains("Producing + Sessions + Prompts per session + Sessions at once + Agents at once per week, by repository", text, StringComparison.Ordinal);
        Assert.Contains("The measures switched on above, added up per repository and stacked", text, StringComparison.Ordinal);
        Assert.Contains("a sum across units that is not a figure of anything", text, StringComparison.Ordinal);
        Assert.Contains("whose folder lies in no registered clone", text, StringComparison.Ordinal);
        Assert.Contains("Press a week to open it in the grids below", text, StringComparison.Ordinal);

        Assert.Equal(
            ["No repository recorded", "backlog"],
            chart.QuerySelectorAll("tbody th").Select(cell => cell.TextContent));
        Assert.Empty(chart.QuerySelectorAll("[class*='segment--identity-']"));

        // The unrecorded band: 2+2+6+2+0 and 1+6+9+3+11, as plain numbers.
        Assert.Equal(["12", "30"], [.. chart.QuerySelectorAll("tbody tr")[0].QuerySelectorAll("td").Select(cell => cell.TextContent)]);
        Assert.Contains("42", chart.QuerySelector("[data-testid='dashboard-sessions-repository-bars-toggle-no-repository-recorded']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("All repositories — a sum across measures, not a figure of anything", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Adding a count to the selection makes the band a sum across units, and the
    /// chart says so rather than printing the number as if it were one: the figures
    /// stop reading as durations, the total heading and the caption say what it is not.
    /// </summary>
    [Fact]
    public void Adding_a_count_to_the_selection_makes_the_repository_band_a_mixed_sum_and_says_so()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        // Producing + Sessions at once: 2 + 2 and 1 + 3 for the unrecorded band.
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-sessions']").Click();
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-prompts-per-session']").Click();
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-agents-at-once']").Click();

        var chart = pane.Find("[data-testid='dashboard-sessions-repository-bars']");
        var text = Squashed(chart.TextContent);

        Assert.Contains("Producing + Sessions at once per week, by repository", text, StringComparison.Ordinal);
        Assert.Contains("The measures switched on above, added up per repository and stacked", text, StringComparison.Ordinal);
        Assert.Contains("a sum across units that is not a figure of anything", text, StringComparison.Ordinal);
        Assert.Contains("All repositories — a sum across measures, not a figure of anything", text, StringComparison.Ordinal);

        Assert.Equal(["4", "4"], [.. chart.QuerySelectorAll("tbody tr")[0].QuerySelectorAll("td").Select(cell => cell.TextContent)]);
        Assert.Contains("8", chart.QuerySelector("[data-testid='dashboard-sessions-repository-bars-toggle-no-repository-recorded']")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>With nothing selected the stack says so rather than drawing an empty
    /// axis under a heading about repositories.</summary>
    [Fact]
    public void With_nothing_selected_the_repository_chart_says_so()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-producing']").Click();
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-sessions']").Click();
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-prompts-per-session']").Click();
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-sessions-at-once']").Click();
        pane.Find("[data-testid='dashboard-sessions-weekly-bars-toggle-agents-at-once']").Click();

        var text = Squashed(pane.Find("[data-testid='dashboard-sessions-repository-bars']").TextContent);

        Assert.Contains("Nothing selected, by repository", text, StringComparison.Ordinal);
        Assert.Contains("Nothing selected in the legend above, so nothing to stack", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pressing a week in either chart picks it for the grids: the rows become that
    /// week's, the caption names it, the column reads as pressed in both charts, and the
    /// five-hour refusal marked is that week's — not the latest week's.
    /// </summary>
    [Fact]
    public void Pressing_a_week_in_either_chart_opens_it_in_the_grids_below()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        // Opens on the latest week: its refusal is Wednesday 19th at 11.
        Assert.Contains("W34", Squashed(pane.Find("[data-testid='dashboard-sessions-hours']").TextContent), StringComparison.Ordinal);
        Assert.Equal("true", pane.FindAll("[data-testid='dashboard-sessions-weekly-bars'] .metric-stacked-bars__column")[1].GetAttribute("aria-pressed"));
        var flagged = pane.FindAll("[data-testid='dashboard-sessions-hours'] .metric-heatmap__cell--flag-blocked");
        Assert.Equal(4, flagged.Count);
        Assert.Contains("blocked by the 5-hour limit", flagged[0].GetAttribute("title"), StringComparison.Ordinal);
        Assert.Contains("Wed 19", Squashed(flagged[0].ParentElement!.QuerySelector("th")!.TextContent), StringComparison.Ordinal);

        // Press the earlier week on the repository chart.
        pane.FindAll("[data-testid='dashboard-sessions-repository-bars'] .metric-stacked-bars__column")[0].Click();

        var grid = pane.Find("[data-testid='dashboard-sessions-hours']");
        Assert.Contains("the week of W33, picked above", Squashed(grid.TextContent), StringComparison.Ordinal);
        Assert.Contains("Thu 06", grid.QuerySelector("tbody th")!.TextContent, StringComparison.Ordinal);

        Assert.Equal("true", pane.FindAll("[data-testid='dashboard-sessions-weekly-bars'] .metric-stacked-bars__column")[0].GetAttribute("aria-pressed"));
        Assert.Equal("true", pane.FindAll("[data-testid='dashboard-sessions-repository-bars'] .metric-stacked-bars__column")[0].GetAttribute("aria-pressed"));

        // That week's refusal: Friday 7th at 15, with no reach recorded, so one cell.
        var earlier = Assert.Single(pane.FindAll("[data-testid='dashboard-sessions-hours'] .metric-heatmap__cell--flag-blocked"));
        Assert.Contains("Fri 07", Squashed(earlier.ParentElement!.QuerySelector("th")!.TextContent), StringComparison.Ordinal);
    }

    /// <summary>
    /// A five-hour wall is drawn as far as it stood: the hour of the refusal and every
    /// hour up to the one its window reset in carry the stripe, the refusal's own hour
    /// the dot as well, and a weekly refusal gets a hollow dot and no stripe.
    /// </summary>
    [Fact]
    public void A_five_hour_wall_is_drawn_from_the_refusal_to_the_hour_its_window_reset()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var grid = pane.Find("[data-testid='dashboard-sessions-hours']");

        // Wednesday 19th: 11 through 14 blocked, 11 the hit as well.
        var wednesday = grid.QuerySelectorAll("tbody tr").Single(row => row.QuerySelector("th")!.TextContent.Contains("Wed 19", StringComparison.Ordinal));
        var cells = wednesday.QuerySelectorAll(".metric-heatmap__cell");

        Assert.Equal([11, 12, 13, 14], Enumerable.Range(0, 24).Where(hour => cells[hour].ClassList.Contains("metric-heatmap__cell--flag-blocked")));
        Assert.Contains("blocked by the 5-hour limit, from the refusal to the reset", cells[13].GetAttribute("title"), StringComparison.Ordinal);

        // No mark of its own for the refusal's hour: the run's start is the moment.
        Assert.Empty(grid.QuerySelectorAll(".metric-heatmap__cell--flag-five-hour"));

        // Tuesday 18th at 9: the weekly refusal, a dot of its own kind and no stripe.
        var tuesday = grid.QuerySelectorAll("tbody tr").Single(row => row.QuerySelector("th")!.TextContent.Contains("Tue 18", StringComparison.Ordinal));
        var nine = tuesday.QuerySelectorAll(".metric-heatmap__cell")[9];

        Assert.Contains("metric-heatmap__cell--flag-weekly", nine.ClassList);
        Assert.DoesNotContain("metric-heatmap__cell--flag-blocked", nine.ClassList);
        Assert.Contains("weekly limit hit", nine.GetAttribute("title"), StringComparison.Ordinal);

        // Both kinds are in the legend.
        var legend = Squashed(grid.QuerySelector(".metric-heatmap__legend")!.TextContent);
        Assert.Contains("blocked by the 5-hour limit, from the refusal to the reset", legend, StringComparison.Ordinal);
        Assert.Contains("weekly limit hit", legend, StringComparison.Ordinal);
    }

    /// <summary>The refusals as a tile, split by allowance, whose they are said.</summary>
    [Fact]
    public void The_limit_hits_tile_counts_the_refusals_by_allowance()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var tile = Squashed(pane.Find("[data-testid='dashboard-sessions-limit-hits']").TextContent);

        Assert.Contains("Limit hits", tile, StringComparison.Ordinal);
        Assert.Contains("9", tile, StringComparison.Ordinal);
        Assert.Contains("8 × 5-hour · 1 × weekly", tile, StringComparison.Ordinal);
        Assert.Contains("Copilot records none", tile, StringComparison.Ordinal);
    }

    /// <summary>Under a usage reset the grid caption says a row runs from the reset hour,
    /// so a reader does not take a row's date for a calendar day.</summary>
    [Fact]
    public void Under_a_usage_reset_the_grid_says_its_rows_are_cut_at_the_reset()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                Week = new UsageWeekInfo(WeekSource.Detected, "Monday 21:00")
            })));

        var pane = context.Render<DashboardPane>();
        var text = Squashed(pane.Find("[data-testid='dashboard-sessions-hours']").TextContent);

        Assert.Contains("as calendar days cut at the reset (Monday 21:00)", text, StringComparison.Ordinal);
        Assert.Contains("the hours outside it are blank", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configured repository's band wears its identity hue when the shell answers one
    /// — the header's Colors switch, seen from this pane — and the folded bands never
    /// do: they are not a repository, so there is no hue for them to wear.
    /// </summary>
    [Fact]
    public void A_repository_band_wears_the_hue_the_shell_answers_and_the_folded_bands_do_not()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>(parameters => parameters
            .Add(p => p.RepositoryColour, alias => alias == "backlog" ? 2 : 5));

        var chart = pane.Find("[data-testid='dashboard-sessions-repository-bars']");
        var first = chart.QuerySelectorAll(".metric-stacked-bars__column")[0].QuerySelectorAll(".metric-stacked-bars__segment");

        // Unrecorded first (most hours), then backlog.
        Assert.DoesNotContain("metric-stacked-bars__segment--identity-5", first[0].ClassList);
        Assert.Contains("metric-stacked-bars__segment--identity-2", first[1].ClassList);
        Assert.Contains("metric-stacked-bars__swatch--identity-2", chart.QuerySelector("[data-testid='dashboard-sessions-repository-bars-toggle-backlog'] .metric-stacked-bars__swatch")!.ClassList);
    }

    /// <summary>
    /// The repository scope reaches the sessions part now — for the one chart that can
    /// honour it. The scope goes to the insight, which is where the rows are narrowed,
    /// and the caption says the folded bands are out of it.
    /// </summary>
    [Fact]
    public void Focusing_a_repository_reaches_the_sessions_part_and_the_caption_says_what_is_out()
    {
        var sessions = new RecordingSessionInsights();

        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(sessions));

        var pane = context.Render<DashboardPane>();
        FocusRepository(pane, "backlog");

        Assert.Contains(sessions.Scopes, scope => scope.Repositories.Contains("backlog"));
    }

    [Fact]
    public void Under_a_repository_scope_the_caption_says_the_unrecorded_sessions_are_out()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                ByRepository = [Insight().ByRepository[1]]
            })));

        var pane = context.Render<DashboardPane>();
        FocusRepository(pane, "backlog");

        var text = Squashed(pane.Find("[data-testid='dashboard-sessions-repository-bars']").TextContent);

        Assert.Contains("Narrowed to the header's repository scope", text, StringComparison.Ordinal);
        Assert.Contains("are out of it", text, StringComparison.Ordinal);
    }

    /// <summary>An axis or nothing, on the sessions chart's rule: a series that arrived
    /// empty draws no chart rather than an empty one under tiles that report figures.</summary>
    [Fact]
    public void The_weekly_charts_are_not_drawn_without_an_axis()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight() with
            {
                SessionsPerWeek = [],
                ByRepository = []
            })));

        var pane = context.Render<DashboardPane>();

        Assert.Empty(pane.FindAll("[data-testid='dashboard-sessions-weekly-bars']"));
        Assert.Empty(pane.FindAll("[data-testid='dashboard-sessions-repository-bars']"));
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

    /// <summary>
    /// The peak and the hour it happened, because the number alone is unplaceable — "four
    /// at once" is a different fact at ten on a Wednesday and at three in the morning. The
    /// hour is local, which makes it the one moment on this part not given in UTC, and the
    /// footnote says so where a reader meets it.
    /// </summary>
    [Fact]
    public void The_most_sessions_at_once_tile_shows_the_peak_and_the_hour_it_happened()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var tile = pane.Find("[data-testid='dashboard-sessions-at-once']");

        Assert.Contains("Most sessions at once", Squashed(tile.TextContent), StringComparison.Ordinal);
        Assert.Equal("4", tile.QuerySelector(".metric-tile__value")!.TextContent.Trim());

        Assert.Contains(
            "First reached in the hour of Wed 19, 09:00 on your local clock — the one moment "
            + "on this part not given in UTC.",
            Squashed(tile.TextContent),
            StringComparison.Ordinal);
    }

    /// <summary>The same, for the other population. The fixture gives the two different
    /// peaks in different hours, so a tile wired to the wrong record fails here rather
    /// than reading plausibly.</summary>
    [Fact]
    public void The_most_agents_at_once_tile_shows_the_peak_and_the_hour_it_happened()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var tile = pane.Find("[data-testid='dashboard-sessions-agents-at-once']");

        Assert.Contains("Most agents at once", Squashed(tile.TextContent), StringComparison.Ordinal);
        Assert.Equal("11", tile.QuerySelector(".metric-tile__value")!.TextContent.Trim());

        Assert.Contains(
            "first reached in the hour of Wed 19, 11:00 on your local clock",
            Squashed(tile.TextContent),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A period nothing produced in has no busiest hour, and a dash is how this surface
    /// already says "no figure" as against "a figure of zero". A zero here would sit beside
    /// a real day and hour and claim both.
    /// </summary>
    [Fact]
    public void The_most_sessions_at_once_tile_shows_a_dash_when_there_was_no_peak()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(
                new ReadySessionInsights(Insight() with { MostSessionsAtOnce = null })));

        var pane = context.Render<DashboardPane>();
        var tile = pane.Find("[data-testid='dashboard-sessions-at-once']");

        Assert.Equal("—", tile.QuerySelector(".metric-tile__value")!.TextContent.Trim());

        // And no hour is named, because there is no hour to name.
        Assert.DoesNotContain("First reached", Squashed(tile.TextContent), StringComparison.Ordinal);
    }

    /// <summary>
    /// The figure is Claude's alone and is counted apart from everything above it. Both
    /// have to be said: a reader who takes this for a share of the sessions tile beside it
    /// would be dividing two numbers that do not bound each other, and one who takes it for
    /// both assistants would be reading a Claude-only figure under a general name.
    /// </summary>
    [Fact]
    public void The_most_agents_at_once_tile_says_the_figure_is_Claudes_alone()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var tile = Squashed(pane.Find("[data-testid='dashboard-sessions-agents-at-once']").TextContent);

        Assert.Contains(
            "Counted apart from the sessions themselves: no figure above this one includes "
            + "them, and no session is counted twice for having spawned one.",
            tile,
            StringComparison.Ordinal);

        Assert.Contains("Claude's alone, because Copilot spawns none.", tile, StringComparison.Ordinal);
    }

    /// <summary>
    /// A third grid on the same axes, counting a third thing. The fixture gives it an hour
    /// the two session grids are empty in, so a grid wired to either of their measures
    /// fails here rather than rendering a plausible copy.
    /// </summary>
    [Fact]
    public void The_agent_grid_is_drawn_with_the_two_session_grids()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        Assert.NotNull(pane.Find("[data-testid='dashboard-sessions-hours']"));
        Assert.NotNull(pane.Find("[data-testid='dashboard-sessions-open']"));

        var agents = pane.Find("[data-testid='dashboard-sessions-agents']");
        var cells = agents.QuerySelectorAll("tbody tr")[0].QuerySelectorAll(".metric-heatmap__cell");

        Assert.Equal("11", cells[9].QuerySelector(".metric-heatmap__value")!.TextContent);

        // The hour that belongs to this grid alone: nothing produced and nothing was open,
        // and three agents were running.
        Assert.Equal("3", cells[11].QuerySelector(".metric-heatmap__value")!.TextContent);

        // And it says what it counts, and that it is neither of the other two.
        var label = Squashed(agents.TextContent);

        Assert.Contains("Agents at once, by hour", label, StringComparison.Ordinal);
        Assert.Contains("Your local clock, the week of W34, picked above", label, StringComparison.Ordinal);
        Assert.Contains("they are in neither grid above", label, StringComparison.Ordinal);
    }

    /// <summary>Three grids or none. They are the same seven days read three ways, so one
    /// drawn without the others would be an axis with part of an answer on it.</summary>
    [Fact]
    public void The_agent_grid_is_not_drawn_when_there_is_no_grid_to_draw()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(
                new ReadySessionInsights(Insight() with { ActivityByHour = [], Grids = [] })));

        var pane = context.Render<DashboardPane>();

        Assert.Empty(pane.FindAll("[data-testid='dashboard-sessions-agents']"));
    }

    /// <summary>The outline is the reader's own working hours and comes off the same cell
    /// the other two grids read, so the three cannot disagree about when the reader
    /// works.</summary>
    [Fact]
    public void The_agent_grid_outlines_the_same_working_hours_as_the_session_grids()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        var agents = pane.Find("[data-testid='dashboard-sessions-agents']")
            .QuerySelectorAll("tbody tr")[0]
            .QuerySelectorAll(".metric-heatmap__cell");

        var sessions = pane.Find("[data-testid='dashboard-sessions-hours']")
            .QuerySelectorAll("tbody tr")[0]
            .QuerySelectorAll(".metric-heatmap__cell");

        Assert.Equal(
            sessions.Select(cell => cell.ClassList.Contains("metric-heatmap__cell--marked")),
            agents.Select(cell => cell.ClassList.Contains("metric-heatmap__cell--marked")));

        // Not two rows of falses agreeing with each other.
        Assert.Contains("metric-heatmap__cell--marked", agents[9].ClassList);
    }

    /// <summary>Every hour on every row, quiet ones included. A row that omitted them
    /// would render short and read as "not reported" where the honest answer is that no
    /// agent ran.</summary>
    [Fact]
    public void The_agent_grid_carries_every_hour_including_the_quiet_ones()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var agents = pane.Find("[data-testid='dashboard-sessions-agents']");

        Assert.Equal(7, agents.QuerySelectorAll("tbody tr").Length);
        Assert.Equal(24, agents.QuerySelectorAll(".metric-heatmap__bucket").Length);
        Assert.Equal(7 * 24, agents.QuerySelectorAll(".metric-heatmap__cell").Length);
    }

    /// <summary>
    /// The session grids' cell detail is two session durations, so wiring it here would
    /// print how long sessions ran under a label about agents. The shade and the number are
    /// the whole answer this grid has, and saying only that is the honest version.
    /// </summary>
    [Fact]
    public void The_agent_grid_does_not_wear_the_session_grids_cell_detail()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();

        var busiest = pane.Find("[data-testid='dashboard-sessions-agents']")
            .QuerySelectorAll(".metric-heatmap__cell")[9]
            .QuerySelector(".sr-only")!
            .TextContent;

        Assert.DoesNotContain("agent-active", busiest, StringComparison.Ordinal);
        Assert.DoesNotContain("waiting", busiest, StringComparison.Ordinal);

        // The same cell on the first grid does carry them, so this is a difference between
        // the two rather than a component that stopped rendering detail.
        Assert.Contains(
            "2h agent-active, 45m waiting",
            pane.Find("[data-testid='dashboard-sessions-hours']")
                .QuerySelectorAll(".metric-heatmap__cell")[9]
                .QuerySelector(".sr-only")!
                .TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The prerequisite relabel. The first grid opened "Peak agents running at once" and
    /// counted sessions, which was harmless while it was the only grid using the word.
    /// Ship an agent grid beside it and one word means two populations within one scroll.
    /// </summary>
    [Fact]
    public void The_session_grid_no_longer_calls_a_session_an_agent()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var grid = Squashed(pane.Find("[data-testid='dashboard-sessions-hours']").TextContent);

        Assert.Contains("Sessions producing at once", grid, StringComparison.Ordinal);
        Assert.DoesNotContain("agents running at once", grid, StringComparison.Ordinal);
    }

    /// <summary>
    /// The part now mixes three windowed tiles with three fixed grids, and a surface may do
    /// that only if it says so. The note owns the mixture because it is the one place that
    /// can speak about the whole part at once.
    /// </summary>
    [Fact]
    public void The_note_says_the_two_at_once_figures_follow_the_period_and_the_grids_do_not()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<ISessionInsights>(new ReadySessionInsights(Insight())));

        var pane = context.Render<DashboardPane>();
        var note = Squashed(pane.Find("[data-testid='dashboard-sessions-note']").TextContent);

        Assert.Contains(
            "The two 'at once' figures follow the period like the tiles beside them; all "
            + "three grids below are the same 7 days whichever period is selected.",
            note,
            StringComparison.Ordinal);

        Assert.Contains(
            "A session that spawned no agents is not missing from anything and lowers "
            + "nothing — it simply has none to contribute, and a period nobody spawned one "
            + "in shows no peak rather than a zero.",
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
            Unreadable: [],
            Breakdown:
            [
                new AssistantSessionRow("tower", "DEV-TOWER", 8, TimeSpan.FromHours(4), null),
                new AssistantSessionRow("laptop", "DEV-LAPTOP", 4, TimeSpan.FromHours(1), null)
            ])
        {
            Waiting = TimeSpan.FromHours(9),
            IdleAfter = TimeSpan.FromMinutes(5),

            // A fraction, so a tile that rounded to a whole number would show and fail;
            // and over fewer sessions than the count, because the footnote's whole job
            // is to say so.
            PromptsPerSession = 7.5m,
            SessionsWithPrompts = 8,
            PromptsPerSessionPerWeek = [new("W33", 6m), new("W34", 9m)],
            SessionsPerWeek = [new("W33", 3m), new("W34", 9m)],

            // Different peaks in different hours, so a tile wired to the wrong record
            // renders a plausible figure and fails rather than passing quietly. 2026-08-19
            // is the Wednesday the grid's week ends on.
            MostSessionsAtOnce = new ConcurrencyPeak(4, new DateOnly(2026, 8, 19), 9),
            MostAgentsAtOnce = new ConcurrencyPeak(11, new DateOnly(2026, 8, 19), 11),
            ActivityByHour = Grid(),

            // The four series cut by week, on the same two buckets the prompts series
            // uses. Hours as decimals, and a stacked pair whose sum differs per week so a
            // part that drew one series twice would show and fail.
            ActiveTimePerWeek = [new("W33", 3m), new("W34", 2m)],
            WaitingPerWeek = [new("W33", 6m), new("W34", 3m)],
            MostSessionsAtOncePerWeek = [new("W33", 2m), new("W34", 4m)],
            MostAgentsAtOncePerWeek = [new("W33", 0m), new("W34", 11m)],

            // Two rows: one recorded, one for the sessions that recorded none — which
            // is the row the part must draw rather than drop. The hours add up to the
            // series above; the peaks deliberately do not (3 + 2 over a total of 4).
            ByRepository =
            [
                new RepositoryWeekly(
                    RepositoryWeekly.UnrecordedName,
                    RepositoryBandKind.Unrecorded,
                    SessionsPerWeek: [new("W33", 2m), new("W34", 6m)],
                    PromptsPerSessionPerWeek: [new("W33", 6m), new("W34", 9m)],
                    ActiveTimePerWeek: [new("W33", 2m), new("W34", 1m)],
                    WaitingPerWeek: [new("W33", 6m), new("W34", 3m)],
                    MostSessionsAtOncePerWeek: [new("W33", 2m), new("W34", 3m)],
                    MostAgentsAtOncePerWeek: [new("W33", 0m), new("W34", 11m)]),
                new RepositoryWeekly(
                    "backlog",
                    RepositoryBandKind.Configured,
                    SessionsPerWeek: [new("W33", 1m), new("W34", 3m)],
                    PromptsPerSessionPerWeek: [new("W33", 0m), new("W34", 0m)],
                    ActiveTimePerWeek: [new("W33", 1m), new("W34", 1m)],
                    WaitingPerWeek: [new("W33", 0m), new("W34", 0m)],
                    MostSessionsAtOncePerWeek: [new("W33", 1m), new("W34", 2m)],
                    MostAgentsAtOncePerWeek: [new("W33", 0m), new("W34", 0m)])
            ],
            ActivityByDay = Days(),

            // Two weeks of grids, on the two buckets the series above use, the latest
            // last — the one the part opens on. Each carries one five-hour refusal, on
            // different cells, so a part reading the wrong week's marks shows and fails.
            Grids =
            [
                new WeekGrid("2026-W33", "W33", new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero), Grid(new DateOnly(2026, 8, 6)), Days(new DateOnly(2026, 8, 6)),
                    [new LimitMark(new DateOnly(2026, 8, 7), 15, AssistantLimitKind.FiveHour, new DateTimeOffset(2026, 8, 7, 15, 20, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 7, 17, 0, 0, TimeSpan.Zero))
                    {
                        UntilDay = new DateOnly(2026, 8, 7),
                        UntilHour = 15
                    }]),
                new WeekGrid("2026-W34", "W34", new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero), Grid(), Days(),
                    [
                        new LimitMark(new DateOnly(2026, 8, 19), 11, AssistantLimitKind.FiveHour, new DateTimeOffset(2026, 8, 19, 11, 40, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero))
                        {
                            UntilDay = new DateOnly(2026, 8, 19),
                            UntilHour = 14
                        },
                        new LimitMark(new DateOnly(2026, 8, 18), 9, AssistantLimitKind.SevenDay, new DateTimeOffset(2026, 8, 18, 9, 5, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 24, 21, 0, 0, TimeSpan.Zero))
                    ])
            ],

            // Eight five-hour walls and one weekly, so the tile's split shows and a part
            // that printed the total under either label alone would fail.
            LimitHits = new LimitHitCounts(8, 1)
        };

    /// <summary>The three counts differ on purpose, and deliberately do not add up: a
    /// session that ran across the edge of the working day is in both halves, so a
    /// fixture where they summed would let a part that mixed the columns up pass.</summary>
    private static IReadOnlyList<ActivityDay> Days(DateOnly? first = null) =>
    [
        .. Enumerable.Range(0, 7).Select(day =>
            new ActivityDay((first ?? new DateOnly(2026, 8, 13)).AddDays(day), day + 1, day, day + 1))
    ];

    /// <summary>The full grid the part is handed in practice: seven dated days, all
    /// twenty-four hours each, quiet hours included. A fixture that omitted the quiet
    /// ones would let a part that drops them pass.</summary>
    private static IReadOnlyList<ActivityHour> Grid(DateOnly? first = null) =>
    [
        .. Enumerable.Range(0, 7).SelectMany(day => Enumerable.Range(0, 24).Select(hour =>
            new ActivityHour(
                (first ?? new DateOnly(2026, 8, 13)).AddDays(day),
                hour,
                hour == 9 ? 4 : 0,
                hour == 9 ? TimeSpan.FromHours(2) : TimeSpan.Zero,
                hour == 9 ? TimeSpan.FromMinutes(45) : TimeSpan.Zero,

                // Higher than the peak, because a session waiting on a prompt is on the go
                // and is not producing. Equal figures would let the two grids be wired to
                // the same measure without a test noticing.
                hour == 9 ? 7 : hour == 10 ? 2 : 0,

                // Mon-Fri 09:00-17:30 against a week that starts on a Thursday.
                WorkingHours.Default.Covers(new DateOnly(2026, 8, 13).AddDays(day).DayOfWeek, hour))
            {
                // Different from both figures above it in the hour they share, and present
                // in an hour where neither of them is. One session can hold five agents at
                // once, so nothing bounds this by either — and three grids wired to one
                // measure would otherwise render identically and pass.
                PeakAgents = hour == 9 ? 11 : hour == 11 ? 3 : 0
            }))
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

        Assert.Contains("Full marks for volume is a quarter above your own best four weeks", note, StringComparison.Ordinal);

        // The figure it works out to, the record behind it, and when that record was
        // set — all three, because any two of them leave the third unarguable.
        Assert.Contains("380 merged pull requests", note, StringComparison.Ordinal);
        Assert.Contains("304", note, StringComparison.Ordinal);
        Assert.Contains("12 May 2026", note, StringComparison.Ordinal);
        Assert.Contains("09 Jun 2026", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// No history, no target, and the volume card simply empty. A reader who cannot
    /// see that volume dropped out reads the quality score as the score of
    /// everything — so the note says it, and the card itself says it rather than
    /// claiming it was scored from inputs the view does not show.
    /// </summary>
    [Fact]
    public void The_score_note_says_when_volume_is_not_being_scored()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(
                new ReadyProductivityInsights(Score() with
                {
                    Target = null,
                    Volume = ProductivityScore.Empty
                })));

        var pane = context.Render<DashboardPane>();
        var note = Squashed(pane.Find("[data-testid='dashboard-score-note']").TextContent);

        Assert.Contains("Volume is not being scored", note, StringComparison.Ordinal);
        Assert.Contains("could not be read, or there is not enough of it yet", note, StringComparison.Ordinal);

        var volume = Squashed(pane.Find("[data-testid='dashboard-score-volume']").TextContent);

        Assert.Contains("Not scored: there is no history to set a target from", volume, StringComparison.Ordinal);
        Assert.DoesNotContain("inputs this view does not show", volume, StringComparison.Ordinal);

        // And the other card is untouched by it.
        var quality = Squashed(pane.Find("[data-testid='dashboard-score-quality']").TextContent);

        Assert.Contains("First review within a day", quality, StringComparison.Ordinal);
    }

    /// <summary>
    /// The split on screen: two cards, each with its own composition, and no
    /// sessions row on either — the note says where sessions went, permanently,
    /// because a reader of the previous version will look for the row.
    /// </summary>
    [Fact]
    public void The_score_part_draws_volume_and_quality_as_two_cards()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(new ReadyProductivityInsights(Score())));

        var pane = context.Render<DashboardPane>();

        var volume = Squashed(pane.Find("[data-testid='dashboard-score-volume']").TextContent);
        var quality = Squashed(pane.Find("[data-testid='dashboard-score-quality']").TextContent);

        Assert.Contains("Volume", volume, StringComparison.Ordinal);
        Assert.Contains("Pull requests merged", volume, StringComparison.Ordinal);
        Assert.Contains("Issues closed", volume, StringComparison.Ordinal);
        Assert.DoesNotContain("First review within a day", volume, StringComparison.Ordinal);

        Assert.Contains("Quality", quality, StringComparison.Ordinal);
        Assert.Contains("First review within a day", quality, StringComparison.Ordinal);
        Assert.Contains("Merged touching 10 files or fewer", quality, StringComparison.Ordinal);
        Assert.DoesNotContain("Pull requests merged", quality, StringComparison.Ordinal);

        Assert.DoesNotContain("Assistant sessions", volume + quality, StringComparison.Ordinal);

        var note = Squashed(pane.Find("[data-testid='dashboard-score-note']").TextContent);

        Assert.Contains("Assistant sessions are scored in neither", note, StringComparison.Ordinal);
        Assert.Contains("count effort rather than output", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rework part carries the two figures the reviewers wrote down, and the
    /// headline the median commit count with the population it was taken over.
    /// </summary>
    [Fact]
    public void Review_rounds_change_requests_and_commits_per_pull_request_reach_the_screen()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(new ReadyProductivityInsights(Score())));

        var pane = context.Render<DashboardPane>();

        var rounds = Squashed(pane.Find("[data-testid='dashboard-rework-rounds']").TextContent);
        var requested = Squashed(pane.Find("[data-testid='dashboard-rework-changes-requested']").TextContent);
        var commits = Squashed(pane.Find("[data-testid='dashboard-headline-commits']").TextContent);

        Assert.Contains("Review rounds", rounds, StringComparison.Ordinal);
        Assert.Contains("41", rounds, StringComparison.Ordinal);
        Assert.Contains("Changes requested", requested, StringComparison.Ordinal);
        Assert.Contains("7", requested, StringComparison.Ordinal);

        Assert.Contains("Commits per pull request", commits, StringComparison.Ordinal);
        Assert.Contains("4", commits, StringComparison.Ordinal);
        Assert.Contains("Median, across 290 pull requests whose detail was read", commits, StringComparison.Ordinal);
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
    /// The two scores with their whole compositions behind them: the two volume
    /// inputs read against the reader's own record, and the five proportions.
    /// </summary>
    private static ProductivityScoreInsight Score() =>
        new(
            new ProductivityScore(
                80m,
                [
                    new ProductivityScoreInput("Pull requests merged", 304m, 380m, 3m),
                    new ProductivityScoreInput("Issues closed", 72m, 90m, 2m)
                ]),
            new ProductivityScore(
                66m,
                [
                    new ProductivityScoreInput("First review within a day", 40m, 50m, 2m),
                    new ProductivityScoreInput("Merged without post-review churn", 30m, 50m, 1m),
                    new ProductivityScoreInput("Merged without a conflicted sync", 60m, 132m, 1m),
                    new ProductivityScoreInput("Merged under 400 changed lines", 20m, 44m, 1m),
                    new ProductivityScoreInput("Merged touching 10 files or fewer", 24m, 44m, 1m)
                ]))
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
    private sealed class ReadyProductivityInsights(ProductivityScoreInsight score, ReworkInsight? rework = null) : IProductivityInsights
    {
        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityHeadline>.Ready(
                new ProductivityHeadline(304, 72, 0.2m, TimeSpan.FromHours(5), [], [], [])
                {
                    Complete = score.Complete,
                    MedianCommitsPerPullRequest = 4,
                    PullRequestsWithCommitCount = 290
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
                (rework ?? new ReworkInsight(6, 30, 12, 2, 9, true, [new InsightPoint("W34", 3m)], [])
                {
                    PullRequestsSynced = 14,
                    PullRequestsWithConflictedSync = 3,
                    SyncMerges = 21,
                    ConflictedSyncMerges = 4
                }) with
                {
                    Complete = score.Complete,
                    ReviewRounds = 41,
                    ChangesRequested = 7
                }));

        public void Invalidate(DashboardScope scope)
        {
        }
    }

    /// <summary>
    /// The second kind of rework, beside the first: conflicted syncs with the base
    /// branch, over the pull requests that synced at all. The denominator is on the
    /// tile because "3" means nothing without knowing whether it is 3 of 14 or 3 of
    /// 300, and the conflicted-merge count says it is a floor, because it is.
    /// </summary>
    [Fact]
    public void The_rework_part_shows_conflicted_syncs_over_the_pull_requests_that_synced()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(new ReadyProductivityInsights(Score())));

        var pane = context.Render<DashboardPane>();

        var conflicted = Squashed(pane.Find("[data-testid='dashboard-rework-conflicted']").TextContent);
        Assert.Contains("3", conflicted, StringComparison.Ordinal);
        Assert.Contains("of 14 synced", conflicted, StringComparison.Ordinal);

        Assert.Contains("21", pane.Find("[data-testid='dashboard-rework-syncs']").TextContent, StringComparison.Ordinal);

        var conflicts = Squashed(pane.Find("[data-testid='dashboard-rework-conflicts']").TextContent);
        Assert.Contains("4", conflicts, StringComparison.Ordinal);
        Assert.Contains("At least", conflicts, StringComparison.Ordinal);

        // The churn grid is still there beside it; neither kind hides the other.
        Assert.NotEmpty(pane.FindAll("[data-testid='dashboard-rework-tiles']"));
    }

    /// <summary>
    /// A window nobody reviewed is not an empty window when its branches synced.
    /// Zero conflicts over twelve syncs is a result, and it renders as one — with
    /// the churn grid absent rather than reading "0 of 0 reviewed".
    /// </summary>
    [Fact]
    public void An_unreviewed_window_with_synced_branches_still_shows_its_syncs()
    {
        using var context = Context(configure: services =>
            services.AddSingleton<IProductivityInsights>(new ReadyProductivityInsights(
                Score(),
                new ReworkInsight(0, 0, 0, 0, 0, true, [], [])
                {
                    PullRequestsSynced = 12,
                    PullRequestsWithConflictedSync = 0,
                    SyncMerges = 15,
                    ConflictedSyncMerges = 0
                })));

        var pane = context.Render<DashboardPane>();

        Assert.Contains("of 12 synced", Squashed(pane.Find("[data-testid='dashboard-rework-conflicted']").TextContent), StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='dashboard-rework-tiles']"));
        Assert.Empty(pane.FindAll("[data-testid='dashboard-rework-status']"));
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
    /// changing: the only controls on the panel are the filter, the per-part refresh
    /// and info mark, the three section folds, and the close button.
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
        // No repository select: the header's scope is the repository control.
        Assert.Empty(pane.FindAll("[data-testid='dashboard-repository-filter']"));
        Assert.Single(pane.FindAll("[data-testid='dashboard-machine-filter'] select"));
        Assert.Equal(2, pane.FindAll("[data-testid='dashboard-window-filter'] button").Count);
        Assert.Equal(8, pane.FindAll("[data-testid$='-refresh']").Count);
        // The folds show and hide what is already there; they arrange nothing.
        Assert.Equal(3, pane.FindAll("[data-testid$='-toggle'].fold__trigger").Count);
        // The info marks open a caption; they change nothing.
        Assert.Equal(8, pane.FindAll("[data-testid$='-info'].info-hint__trigger").Count);
        Assert.Single(pane.FindAll("[aria-label='Close dashboard']"));

        var controls = pane.FindAll("button, select, input, textarea");

        // One close, one filter select, two window buttons, eight refreshes, three
        // folds, eight info marks.
        Assert.Equal(1 + 1 + 2 + 8 + 3 + 8, controls.Count);
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
        first.Find("[data-testid='dashboard-machine-filter'] select").Change(DashboardTestHost.MachineId);

        productivity.Scopes.Clear();

        var second = context.Render<DashboardPane>();
        _ = second;

        Assert.All(productivity.Scopes, scope => Assert.True(scope.IsAllMachines));
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
    /// that it still renders this pane's own class names - app.css styles all four
    /// and did not move. Not the shared pane-header assertion: that one expects a
    /// subtitle, and this pane has none on purpose — the section headings say what
    /// it holds, and a sentence above them saying it again was text in front of the
    /// figures.
    /// </summary>
    [Fact]
    public void The_header_is_the_shared_component_wearing_this_panes_classes()
    {
        using var context = Context();

        var pane = context.Render<DashboardPane>();
        var header = pane.Find(".dashboard-panel__header");

        Assert.Equal("HEADER", header.TagName);
        Assert.Equal("dashboard-panel__header", header.GetAttribute("class"));

        var text = header.Children[0];
        Assert.Equal("DIV", text.TagName);
        Assert.Null(text.GetAttribute("class"));
        Assert.Equal(["P", "H2"], text.Children.Select(child => child.TagName));
        Assert.Equal(
            ["dashboard-panel__eyebrow", "dashboard-panel__title"],
            text.Children.Select(child => child.GetAttribute("class")));
        Assert.Equal("dashboard-title", text.Children[1].GetAttribute("id"));

        SectionHeaderAdoptionTests.AssertPaneHeaderActions(header, "dashboard-panel");
        Assert.NotNull(header.QuerySelector(".dashboard-panel__header-actions button"));
    }

    /// <summary>What the shell does when a scope chip is pressed: hands the pane its
    /// scope through the parameter. The pane has no control of its own to press,
    /// which is the point of these tests going through the parameter.</summary>
    private static void FocusRepository(IRenderedComponent<DashboardPane> pane, params string[] aliases) =>
        pane.Render(parameters => parameters.Add(p => p.RepositoryAliases, aliases));

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

    /// <summary>Records, for every fetch, whether the fetch before it had already been
    /// cancelled by the time this one arrived.</summary>
    private sealed class TokenOrderSessionInsights : ISessionInsights
    {
        private CancellationToken? _previous;

        public int Calls { get; private set; }

        public List<bool> PredecessorWithdrawnOnArrival { get; } = [];

        public Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            if (_previous is { } previous)
            {
                PredecessorWithdrawnOnArrival.Add(previous.IsCancellationRequested);
            }

            _previous = cancellationToken;

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
