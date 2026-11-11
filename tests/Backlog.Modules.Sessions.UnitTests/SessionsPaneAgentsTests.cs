using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
using Backlog.SharedKernel;
using Backlog.Tests;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The loop sessions in the Sessions pane (local ADR 0051 §3, §4, §6; the design's
/// "Sessions pane" and "Loop session states" artboards): the Agents section that leads
/// the list, the Agent purpose, a loop session's card with its capacity tiles, its work
/// list and its Pause, Resume and Stop, and the Scheduled fold's count of the schedules
/// that need the person with its way to the Schedules page.
/// </summary>
public sealed class SessionsPaneAgentsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private const string DispatcherId = "dispatcher-1";
    private const string TenderId = "tender-1";

    // --- The Agents section ----------------------------------------------------

    [Fact]
    public void The_agents_section_leads_the_list_with_one_row_per_loop_session()
    {
        using var context = new Harness(
            Session("task-1", "Fix the importer", lastActivity: Noon.AddMinutes(-1)),
            Session(DispatcherId, "📡 SESSION DISPATCHER", lastActivity: Noon.AddMinutes(-30)),
            Session(TenderId, "🧠 BRAIN TENDER", lastActivity: Noon.AddMinutes(-40)));

        var pane = context.Render();

        pane.WaitForAssertion(() =>
        {
            var groups = pane.FindAll("[data-testid='sessions-group']");
            Assert.Equal(AgentSessionGroups.AgentsKey, groups[0].GetAttribute("data-group-key"));
            Assert.Equal("Agents", groups[0].QuerySelector("[data-testid='sessions-group-name']")!.TextContent.Trim());

            var titles = groups[0].QuerySelectorAll("[data-testid='sessions-title']").Select(title => title.TextContent.Trim()).ToList();
            Assert.Equal(["📡 SESSION DISPATCHER", "🧠 BRAIN TENDER"], titles);

            // The task session is not among them: it sits in the sections after.
            Assert.DoesNotContain(groups.Skip(1), group => group.QuerySelectorAll("[data-testid='sessions-title']").Any(title => title.TextContent.Contains("DISPATCHER", StringComparison.Ordinal)));
            Assert.Contains(groups.Skip(1), group => group.TextContent.Contains("Fix the importer", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("purpose")]
    [InlineData("plan")]
    public void The_agents_section_leads_under_every_grouping(string grouping)
    {
        using var context = new Harness(
            Session("task-1", "Fix the importer", lastActivity: Noon.AddMinutes(-1)),
            Session(DispatcherId, "📡 SESSION DISPATCHER", lastActivity: Noon.AddMinutes(-30)));

        var pane = context.Render();
        pane.WaitForElement("[data-testid='sessions-row']");

        pane.Find("[data-testid='sessions-grouping-filter'] select").Change(grouping);

        pane.WaitForAssertion(() =>
        {
            var groups = pane.FindAll("[data-testid='sessions-group']");
            Assert.Equal(AgentSessionGroups.AgentsKey, groups[0].GetAttribute("data-group-key"));
            Assert.Equal("Agents", groups[0].QuerySelector("[data-testid='sessions-group-name']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void A_loop_row_wears_its_loop_state_and_a_line_of_what_it_holds()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, held: new LoopHeldWork(Requests: 1, Deliveries: 1, Sessions: 1, Notes: 0), at: Noon.AddSeconds(-40));
        context.Requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-5)));
        context.Requests.Add(SessionRequests.ForPlan("loop-health", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-4)));

        var pane = context.Render();

        pane.WaitForAssertion(() =>
        {
            var row = pane.Find("[data-testid='sessions-row']");
            Assert.Equal("Agent", row.QuerySelector("[data-testid='sessions-purpose']")!.TextContent.Trim());
            Assert.Equal("Listening", row.QuerySelector("[data-testid='sessions-loop-row-state']")!.TextContent.Trim());
            Assert.Equal("2 requests waiting · 1 of 3 sessions working · 1 answer to deliver", row.QuerySelector("[data-testid='sessions-loop-holds']")!.TextContent.Trim());
            Assert.Equal("checked in 40 s ago", row.QuerySelector("[data-testid='sessions-loop-row-check-in']")!.TextContent.Trim());

            // The process's own chip gives way to the loop's.
            Assert.Null(row.QuerySelector(".badge--integration-running"));
        });
    }

    [Fact]
    public void A_loop_that_never_checked_in_says_so_on_its_row_and_in_its_card()
    {
        using var context = new Harness(Session(TenderId, "🧠 BRAIN TENDER"));

        var pane = context.Render();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(LoopSessionView.NoCheckIn, pane.Find("[data-testid='sessions-loop-row-state']").TextContent.Trim());
            Assert.Empty(pane.FindAll("[data-testid='sessions-loop-holds']"));
            Assert.Equal("/loop /backlog-tools:tend-brain", pane.Find("[data-testid='sessions-loop-command']").TextContent.Trim());
            Assert.NotNull(pane.Find("[data-testid='sessions-loop-command-copy']"));
            Assert.NotNull(pane.Find("[data-testid='sessions-loop-no-check-in']"));
            Assert.Empty(pane.FindAll("[data-testid='sessions-loop-pause']"));
        });
    }

    [Theory]
    [InlineData(-1, AgentSessionState.Running, false, "Listening", "live")]
    [InlineData(-45, AgentSessionState.Running, false, "Stalled", "alert")]
    [InlineData(-1, AgentSessionState.Finished, false, "Not running", "fault")]
    [InlineData(-1, AgentSessionState.Running, true, "Not running", "fault")]
    public void A_loop_rows_chip_follows_its_loop_state(int checkedInMinutesAgo, AgentSessionState process, bool archived, string label, string tone)
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER", state: process, archived: archived));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, heartbeatMinutes: 30, at: Noon.AddMinutes(checkedInMinutesAgo));

        var pane = context.Render(view: "all");

        pane.WaitForAssertion(() =>
        {
            var chip = pane.Find("[data-testid='sessions-loop-row-state']");
            Assert.Equal(label, chip.TextContent.Trim());
            Assert.Contains($"badge--tone-{tone}", chip.ClassName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_dispatcher_holding_reads_Holding_and_the_brain_tender_never_does()
    {
        using var context = new Harness(
            Session(DispatcherId, "📡 SESSION DISPATCHER", lastActivity: Noon.AddMinutes(-1)),
            Session(TenderId, "🧠 BRAIN TENDER", lastActivity: Noon.AddMinutes(-2)));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, activity: LoopActivity.Holding);
        context.CheckIn(TenderId, LoopKind.BrainTender, activity: LoopActivity.Holding, held: new LoopHeldWork(0, 0, 0, 3));

        var pane = context.Render();

        pane.WaitForAssertion(() =>
        {
            var chips = pane.FindAll("[data-testid='sessions-loop-row-state']").Select(chip => chip.TextContent.Trim()).ToList();
            Assert.Equal(["Holding", "Listening"], chips);
            Assert.Equal("3 notes in hand", pane.FindAll("[data-testid='sessions-loop-holds']")[1].TextContent.Trim());
        });
    }

    // --- The Purpose filter ----------------------------------------------------

    [Fact]
    public void The_purpose_filter_offers_Agent_with_its_tally_and_narrows_to_the_loops()
    {
        using var context = new Harness(
            Session("task-1", "Fix the importer", lastActivity: Noon.AddMinutes(-1)),
            Session(DispatcherId, "📡 SESSION DISPATCHER", lastActivity: Noon.AddMinutes(-30)),
            Session(TenderId, "🧠 BRAIN TENDER", lastActivity: Noon.AddMinutes(-40)));

        var pane = context.Render();

        pane.WaitForAssertion(() =>
        {
            var agent = pane.Find("[data-testid='sessions-purpose-agent']");
            Assert.StartsWith("Agent", agent.TextContent.Trim(), StringComparison.Ordinal);
            Assert.Equal("2", agent.QuerySelector("[data-testid='sessions-purpose-tally']")!.TextContent.Trim());

            // Offered first, after All, as the section leads the list.
            var options = pane.FindAll("[data-testid='sessions-purpose-filter'] button").Select(button => button.GetAttribute("data-testid")).ToList();
            Assert.Equal(["sessions-purpose-all", "sessions-purpose-agent"], options.Take(2));
        });

        pane.Find("[data-testid='sessions-purpose-agent']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal(
                ["📡 SESSION DISPATCHER", "🧠 BRAIN TENDER"],
                pane.FindAll("[data-testid='sessions-title']").Select(title => title.TextContent.Trim())));
    }

    // --- The loop card -----------------------------------------------------------

    [Fact]
    public void A_dispatchers_card_shows_its_command_last_check_in_and_wake_reasons()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, heartbeatMinutes: 30, at: Noon.AddSeconds(-40));

        var pane = context.Render();

        pane.WaitForAssertion(() =>
        {
            var card = pane.Find("[data-testid='sessions-detail'] [data-testid='sessions-loop']");
            Assert.Equal("/loop /backlog-tools:dispatch-sessions", card.QuerySelector("[data-testid='sessions-loop-command']")!.TextContent.Trim());
            Assert.Equal("Listening", card.QuerySelector("[data-testid='sessions-loop-state']")!.TextContent.Trim());
            Assert.StartsWith("Last check-in 40 s ago · every 30 minutes", card.QuerySelector("[data-testid='sessions-loop-check-in']")!.TextContent.Trim(), StringComparison.Ordinal);
            Assert.Equal(
                "Wakes on a new request, a freed slot, an answer to deliver, or the 30-minute heartbeat.",
                card.QuerySelector("[data-testid='sessions-loop-wakes']")!.TextContent.Trim());

            // A loop runs no delivery flow and produces no pull request.
            Assert.Empty(pane.FindAll("[data-testid='sessions-no-run']"));
            Assert.Empty(pane.FindAll("[data-testid='sessions-pull-request-card']"));
            Assert.Equal("Listening", pane.Find("[data-testid='sessions-detail-loop-state']").TextContent.Trim());
        });
    }

    [Fact]
    public void A_dispatchers_card_shows_the_four_capacity_tiles_and_marks_the_limit_reached()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher);
        context.Capacity = new CapacityReading(1, 84, 34, 11.6, CapacityLimits.Default);

        var pane = context.Render();

        pane.WaitForAssertion(() =>
        {
            var tiles = pane.Find("[data-testid='sessions-loop-capacity']");
            Assert.Equal(("Sessions", "1 / 3", "working now"), Tile(tiles, "loop-capacity-sessions"));
            Assert.Equal(("Plan usage", "84%", "limit 80% · reached"), Tile(tiles, "loop-capacity-usage"));
            Assert.Equal(("CPU", "34%", "limit 80%"), Tile(tiles, "loop-capacity-cpu"));
            Assert.Equal(("Free memory", "11 GB", "needs 4 GB"), Tile(tiles, "loop-capacity-memory"));

            Assert.Equal("true", tiles.QuerySelector("[data-testid='loop-capacity-usage']")!.GetAttribute("data-held"));
            Assert.Equal("false", tiles.QuerySelector("[data-testid='loop-capacity-cpu']")!.GetAttribute("data-held"));
        });
    }

    [Fact]
    public void A_capacity_figure_the_machine_cannot_give_reads_unavailable()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher);
        context.Capacity = new CapacityReading(0, null, null, null, CapacityLimits.Default);

        var pane = context.Render();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(4, pane.FindAll("[data-testid='sessions-loop-capacity'] .metric-tile").Count);
            Assert.Contains("Not measured on this machine", pane.Find("[data-testid='loop-capacity-cpu']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_brain_tenders_card_has_no_capacity_and_no_work_list()
    {
        using var context = new Harness(Session(TenderId, "🧠 BRAIN TENDER"));
        context.CheckIn(TenderId, LoopKind.BrainTender, heartbeatMinutes: 60);
        context.Capacity = new CapacityReading(1, 10, 10, 20, CapacityLimits.Default);

        var pane = context.Render();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("/loop /backlog-tools:tend-brain", pane.Find("[data-testid='sessions-loop-command']").TextContent.Trim());
            Assert.Equal("Wakes on the 1-hour heartbeat to file the inbox.", pane.Find("[data-testid='sessions-loop-wakes']").TextContent.Trim());
            Assert.Empty(pane.FindAll("[data-testid='sessions-loop-capacity']"));
            Assert.Empty(pane.FindAll("[data-testid='sessions-loop-work']"));
        });
    }

    [Fact]
    public void A_dispatchers_card_lists_the_requests_and_deliveries_it_works_through()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher);
        var entry = Guid.Parse("0b6c3f2e-1111-4222-8333-944445555666");
        context.Requests.Add(SessionRequests.ForEntry(entry, "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-10)));
        context.Requests.Add(SessionRequests.ForPlan("loop-health", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-5)));
        var linked = SessionRequests.ForPlan("done-plan", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-20)) with { State = SessionRequestState.Linked };
        context.Requests.Add(linked);
        context.Questions.AddPendingDelivery("waiting-1", "Which landing branch?", "main", Noon.AddMinutes(-2));

        var pane = context.Render();

        pane.WaitForAssertion(() =>
        {
            var items = pane.FindAll("[data-testid='sessions-loop-item']");
            Assert.Equal(["entry", "plan", "delivery"], items.Select(item => item.GetAttribute("data-kind")));
            Assert.Equal("Entry 0b6c3f2e", items[0].QuerySelector(".sessions-loop__what")!.TextContent.Trim());
            Assert.Equal("Requested", items[0].QuerySelector(".sessions-loop__item-state")!.TextContent.Trim());
            Assert.Equal("+loop-health", items[1].QuerySelector(".sessions-loop__what")!.TextContent.Trim());
            Assert.Equal("Which landing branch?", items[2].QuerySelector(".sessions-loop__what")!.TextContent.Trim());
            Assert.Equal("To deliver", items[2].QuerySelector(".sessions-loop__item-state")!.TextContent.Trim());
        });
    }

    // --- Pause, Resume and Stop ----------------------------------------------------

    [Fact]
    public async Task Pause_sets_the_command_the_next_check_in_carries_and_Resume_lifts_it()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher);

        var pane = context.Render();
        pane.WaitForElement("[data-testid='sessions-loop-pause']");

        Assert.True(pane.Find("[data-testid='sessions-loop-resume']").HasAttribute("disabled"));

        pane.Find("[data-testid='sessions-loop-pause']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.StartsWith("Paused", pane.Find("[data-testid='sessions-loop-asked']").TextContent.Trim(), StringComparison.Ordinal);
            Assert.True(pane.Find("[data-testid='sessions-loop-pause']").HasAttribute("disabled"));
            Assert.False(pane.Find("[data-testid='sessions-loop-resume']").HasAttribute("disabled"));
            Assert.StartsWith("Paused · ", pane.Find("[data-testid='sessions-loop-holds']").TextContent.Trim(), StringComparison.Ordinal);
        });

        Assert.Equal(LoopCommand.Pause, (await context.Register.GetAsync(DispatcherId, TestContext.Current.CancellationToken))!.Command);

        pane.Find("[data-testid='sessions-loop-resume']").Click();

        pane.WaitForAssertion(() => Assert.Equal("Resume asked", pane.Find("[data-testid='sessions-loop-asked']").TextContent.Trim().Split(" · ")[0]));
        Assert.Equal(LoopCommand.Resume, (await context.Register.GetAsync(DispatcherId, TestContext.Current.CancellationToken))!.Command);
    }

    [Fact]
    public async Task Stop_asks_first_and_sets_the_command_only_when_confirmed()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher);

        var pane = context.Render();
        pane.WaitForElement("[data-testid='sessions-loop-stop']");

        pane.Find("[data-testid='sessions-loop-stop']").Click();

        Assert.Equal(LoopCommand.None, (await context.Register.GetAsync(DispatcherId, TestContext.Current.CancellationToken))!.Command);

        pane.WaitForElement("[data-testid='sessions-loop-stop-confirm']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.StartsWith("Stop asked", pane.Find("[data-testid='sessions-loop-asked']").TextContent.Trim(), StringComparison.Ordinal);
            Assert.True(pane.Find("[data-testid='sessions-loop-stop']").HasAttribute("disabled"));
        });
        Assert.Equal(LoopCommand.Stop, (await context.Register.GetAsync(DispatcherId, TestContext.Current.CancellationToken))!.Command);
    }

    // --- The schedules that need the person ------------------------------------------

    [Fact]
    public async Task The_Scheduled_fold_says_how_many_need_you_and_opens_the_Schedules_page()
    {
        using var context = new Harness(
            Session("task-1", "Fix the importer", archived: false, lastActivity: Noon.AddMinutes(-1)),
            Session("routine-1", "Weekly update", archived: false, scheduledTask: "weekly-update", state: AgentSessionState.Finished));
        await context.ScheduleAsync("weekly-update", needsYou: true);
        await context.ScheduleAsync("issue-sweep", held: true);
        await context.ScheduleAsync("package-update", failed: true);

        var pane = context.Render(view: "open");

        pane.WaitForAssertion(() =>
        {
            var fold = pane.Find($"[data-group-key='{AgentSessionGroups.ScheduledKey}']");

            // Needs you and Held, the count the header's badge keeps: a failed run is not counted.
            Assert.Equal("2 need you", fold.QuerySelector("[data-testid='sessions-scheduled-needs-you']")!.TextContent.Trim());
            Assert.Equal("2", pane.Find("[data-testid='sessions-schedules-badge']").TextContent.Replace("need you", string.Empty, StringComparison.Ordinal).Trim());

            // The link is the fold trigger's sibling, never inside it.
            var link = fold.QuerySelector("[data-testid='sessions-scheduled-open-schedules']")!;
            Assert.Equal("Open Schedules", link.TextContent.Trim());
            Assert.Null(link.Closest("[data-testid='sessions-scheduled-fold']"));
        });

        pane.Find("[data-testid='sessions-scheduled-open-schedules']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.NotNull(pane.Find("[data-testid='schedules-page']"));
            Assert.Empty(pane.FindAll("[data-testid='sessions-list']"));
        });
    }

    [Fact]
    public async Task No_schedule_needing_you_leaves_the_fold_without_a_chip()
    {
        using var context = new Harness(
            Session("routine-1", "Change report", archived: false, scheduledTask: "change-report", state: AgentSessionState.Finished));
        await context.ScheduleAsync("change-report");

        var pane = context.Render(view: "open");

        pane.WaitForElement("[data-testid='sessions-scheduled-open-schedules']");
        Assert.Empty(pane.FindAll("[data-testid='sessions-scheduled-needs-you']"));
    }

    [Fact]
    public async Task In_the_Open_view_the_agents_lead_and_the_scheduled_runs_stay_folded_last()
    {
        using var context = new Harness(
            Session("task-1", "Fix the importer", archived: false, lastActivity: Noon.AddMinutes(-1)),
            Session(DispatcherId, "📡 SESSION DISPATCHER", archived: false, lastActivity: Noon.AddMinutes(-2)),
            Session("routine-1", "Weekly update", archived: false, scheduledTask: "weekly-update", state: AgentSessionState.Finished));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher);
        await context.ScheduleAsync("weekly-update", needsYou: true);

        var pane = context.Render(view: "open");

        pane.WaitForAssertion(() =>
        {
            var keys = pane.FindAll("[data-testid='sessions-group']").Select(group => group.GetAttribute("data-group-key")).ToList();
            Assert.Equal(AgentSessionGroups.AgentsKey, keys[0]);
            Assert.Equal(AgentSessionGroups.ScheduledKey, keys[^1]);
            Assert.NotNull(pane.Find("[data-testid='sessions-scheduled-fold']"));
            Assert.Equal("1 needs you", pane.Find("[data-testid='sessions-scheduled-needs-you']").TextContent.Trim());
        });
    }

    [Fact]
    public void A_finished_loop_session_that_never_checked_in_reads_Not_running()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER", state: AgentSessionState.Finished));

        var pane = context.Render(view: "all");

        pane.WaitForAssertion(() =>
            Assert.Equal("Not running", pane.Find("[data-testid='sessions-loop-row-state']").TextContent.Trim()));
    }

    // --- Cancel requests on a Not running dispatcher -------------------------------------

    [Fact]
    public async Task A_not_running_dispatcher_offers_to_cancel_the_requests_waiting_for_it_and_cancels_them_when_confirmed()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER", state: AgentSessionState.Finished));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher);
        var first = context.Requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-10)));
        var second = context.Requests.Add(SessionRequests.ForPlan("loop-health", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-5)));
        var started = context.Requests.Add(SessionRequests.ForPlan("running", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-30)) with { State = SessionRequestState.Started, SessionId = "child-1" });

        var pane = context.Render(view: "all");

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("Not running", pane.Find("[data-testid='sessions-loop-state']").TextContent.Trim());
            Assert.Equal("2 requests wait for it.", pane.Find("[data-testid='sessions-loop-recover'] .sessions-loop__recover-line").TextContent.Trim());
            Assert.Equal("Cancel requests", pane.Find("[data-testid='sessions-loop-cancel-requests']").TextContent.Trim());

            // Beside the command that starts the loop again.
            Assert.NotNull(pane.Find("[data-testid='sessions-loop-command-copy']"));
        });

        pane.Find("[data-testid='sessions-loop-cancel-requests']").Click();

        // It asks first: nothing is cancelled until the person confirms.
        Assert.Equal(SessionRequestState.Requested, (await context.Requests.GetAsync(first.Id, TestContext.Current.CancellationToken))!.State);

        pane.WaitForElement("[data-testid='sessions-loop-cancel-confirm']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='sessions-loop-cancel-requests']"));
            Assert.Equal(["plan"], pane.FindAll("[data-testid='sessions-loop-item']").Select(item => item.GetAttribute("data-kind")));
        });

        Assert.Equal(SessionRequestState.Cancelled, (await context.Requests.GetAsync(first.Id, TestContext.Current.CancellationToken))!.State);
        Assert.Equal(SessionRequestState.Cancelled, (await context.Requests.GetAsync(second.Id, TestContext.Current.CancellationToken))!.State);

        // A request that already has its session is not the person's to cancel.
        Assert.Equal(SessionRequestState.Started, (await context.Requests.GetAsync(started.Id, TestContext.Current.CancellationToken))!.State);
    }

    [Fact]
    public async Task Keeping_the_requests_cancels_nothing()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER", state: AgentSessionState.Finished));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher);
        var request = context.Requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-10)));

        var pane = context.Render(view: "all");
        pane.WaitForElement("[data-testid='sessions-loop-cancel-requests']").Click();
        pane.WaitForElement("[data-testid='sessions-loop-cancel-keep']").Click();

        Assert.Equal(SessionRequestState.Requested, (await context.Requests.GetAsync(request.Id, TestContext.Current.CancellationToken))!.State);
        Assert.NotNull(pane.Find("[data-testid='sessions-loop-cancel-requests']"));
    }

    [Theory]
    [InlineData(AgentSessionState.Finished, 0)]
    [InlineData(AgentSessionState.Running, 2)]
    public void Cancel_requests_is_offered_only_while_a_stopped_dispatcher_has_requests_waiting(AgentSessionState process, int waiting)
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER", state: process));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher);

        for (var i = 0; i < waiting; i++)
        {
            context.Requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-10 + i)));
        }

        var pane = context.Render(view: "all");

        pane.WaitForElement("[data-testid='sessions-loop-state']");
        Assert.Empty(pane.FindAll("[data-testid='sessions-loop-cancel-requests']"));
    }

    [Fact]
    public void An_old_dispatcher_that_was_replaced_offers_no_cancel_while_the_new_one_runs()
    {
        using var context = new Harness(
            Session("dispatcher-old", "📡 SESSION DISPATCHER", state: AgentSessionState.Finished, lastActivity: Noon.AddHours(-2)),
            Session(DispatcherId, "📡 SESSION DISPATCHER", lastActivity: Noon.AddMinutes(-1)));
        context.CheckIn("dispatcher-old", LoopKind.Dispatcher, at: Noon.AddHours(-2));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, at: Noon.AddMinutes(-1));
        context.Requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-10)));

        var pane = context.Render(view: "all");
        pane.WaitForElement("[data-testid='sessions-row'][data-row-key*='dispatcher-old']").Click();

        // The requests are the live dispatcher's to claim and start.
        pane.WaitForAssertion(() => Assert.Equal("Not running", pane.Find("[data-testid='sessions-loop-state']").TextContent.Trim()));
        Assert.Empty(pane.FindAll("[data-testid='sessions-loop-cancel-requests']"));
    }

    [Fact]
    public void A_forgotten_dispatcher_the_session_list_no_longer_holds_does_not_hide_the_cancel()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER", state: AgentSessionState.Finished));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, at: Noon.AddMinutes(-20));

        // Checked in hours ago from a session the list has since dropped: it reads Stalled
        // by its check-ins alone, and nothing says it still runs.
        context.CheckIn("dispatcher-forgotten", LoopKind.Dispatcher, at: Noon.AddHours(-5));
        context.Requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-10)));

        var pane = context.Render(view: "all");

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='sessions-loop-cancel-requests']")));
    }

    // --- See usage on a Holding dispatcher --------------------------------------------------

    [Fact]
    public void A_holding_dispatcher_names_its_limit_and_offers_See_usage()
    {
        var opened = 0;
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, activity: LoopActivity.Holding);
        context.Capacity = new CapacityReading(1, 84, 34, 11.6, CapacityLimits.Default);

        var pane = context.Render(seeUsage: () => opened++);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("Holding", pane.Find("[data-testid='sessions-loop-state']").TextContent.Trim());
            Assert.Equal("Plan usage is 84%, over the 80% limit.", pane.Find("[data-testid='sessions-loop-holding'] .sessions-loop__recover-line").TextContent.Trim());
            Assert.Equal("See usage", pane.Find("[data-testid='sessions-loop-see-usage']").TextContent.Trim());
        });

        pane.Find("[data-testid='sessions-loop-see-usage']").Click();

        pane.WaitForAssertion(() => Assert.Equal(1, opened));
    }

    [Fact]
    public void See_usage_is_not_offered_where_the_window_has_nowhere_to_show_usage_or_the_loop_is_listening()
    {
        using var holding = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        holding.CheckIn(DispatcherId, LoopKind.Dispatcher, activity: LoopActivity.Holding);

        var pane = holding.Render();
        pane.WaitForElement("[data-testid='sessions-loop-state']");
        Assert.Empty(pane.FindAll("[data-testid='sessions-loop-see-usage']"));

        // What holds it is still said: only the way to the usage goes.
        Assert.NotNull(pane.Find("[data-testid='sessions-loop-holding'] .sessions-loop__recover-line"));

        using var listening = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        listening.CheckIn(DispatcherId, LoopKind.Dispatcher);

        var other = listening.Render(seeUsage: () => { });
        other.WaitForElement("[data-testid='sessions-loop-state']");
        Assert.Empty(other.FindAll("[data-testid='sessions-loop-see-usage']"));
    }

    [Fact]
    public void See_usage_on_the_surface_opens_the_Dashboard_on_its_Sessions_tab()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, activity: LoopActivity.Holding);
        var navigator = new SurfaceNavigator();
        var requests = new List<SurfaceRequest>();
        navigator.ShowRequested += requests.Add;

        var surface = context.RenderSurface(navigator, offersDashboard: true);
        surface.WaitForElement("[data-testid='sessions-loop-see-usage']");
        surface.Find("[data-testid='sessions-loop-see-usage']").Click();

        // The click's handler can land after Click returns while the pane's own reads raise.
        surface.WaitForAssertion(() => Assert.Single(requests));
        var request = requests[0];
        Assert.Equal("dashboard", request.SurfaceId);
        Assert.Equal("sessions", request.Value("tab"));
    }

    [Fact]
    public void The_surface_offers_no_See_usage_while_the_Dashboard_is_off()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, activity: LoopActivity.Holding);

        var surface = context.RenderSurface(new SurfaceNavigator(), offersDashboard: false);

        surface.WaitForAssertion(() => Assert.Equal("Holding", surface.Find("[data-testid='sessions-loop-state']").TextContent.Trim()));
        Assert.Empty(surface.FindAll("[data-testid='sessions-loop-see-usage']"));
    }

    [Theory]
    [InlineData(3, 10.0, 10.0, 20.0, "3 of 3 sessions are working, the limit.")]
    [InlineData(1, 10.0, 91.0, 2.4, "CPU is 91%, over the 80% limit; free memory is 2 GB, under the 4 GB it keeps.")]
    public void The_holding_line_names_every_limit_reached(int working, double usage, double cpu, double free, string line) =>
        Assert.Equal(line, LoopSessionView.HoldingLine(new CapacityReading(working, usage, cpu, free, CapacityLimits.Default)));

    [Fact]
    public void A_holding_line_with_no_limit_measured_says_what_it_holds() =>
        Assert.Equal("It holds the waiting requests until a limit leaves room.", LoopSessionView.HoldingLine(null));

    // --- The loop's facts ------------------------------------------------------------------

    [Fact]
    public void A_dispatchers_detail_shows_its_last_check_in_how_long_it_has_run_and_what_it_dispatched_today()
    {
        using var context = new Harness(
            Session(DispatcherId, "📡 SESSION DISPATCHER"),
            Session("child-today", "Fix the importer", lastActivity: Noon.AddMinutes(-1), startedAt: Noon.AddHours(-1)),
            Session("child-asked-yesterday", "Add the export", lastActivity: Noon.AddMinutes(-2), startedAt: Noon.AddHours(-2)));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, at: Noon.AddSeconds(-40));
        context.Requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddHours(-1)) with { State = SessionRequestState.Started, SessionId = "child-today" });
        context.Requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddDays(-1)) with { State = SessionRequestState.Linked, SessionId = "child-asked-yesterday" });
        context.Requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddDays(-1)) with { State = SessionRequestState.Linked, SessionId = "gone-from-the-list" });
        context.Requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-5)));

        var pane = context.Render();
        pane.WaitForElement("[data-testid='sessions-row']");
        pane.FindAll("[data-testid='sessions-row']").First(row => row.TextContent.Contains("DISPATCHER", StringComparison.Ordinal)).Click();

        pane.WaitForAssertion(() =>
        {
            var facts = pane.Find("[data-testid='sessions-detail'] [data-testid='sessions-facts']");
            Assert.Equal(
                ["Repository", "Machine", "Last check-in", "Running for", "Dispatched today"],
                facts.QuerySelectorAll("dt").Select(term => term.TextContent.Trim()));
            Assert.Equal("40 s ago", facts.QuerySelector("[data-testid='sessions-fact-check-in']")!.TextContent.Trim());
            Assert.Equal("3 h", facts.QuerySelector("[data-testid='sessions-fact-running-for']")!.TextContent.Trim());

            // Started today, or asked for yesterday and started today; not one started yesterday.
            Assert.Equal("2 sessions", facts.QuerySelector("[data-testid='sessions-fact-dispatched']")!.TextContent.Trim());

            // The generic session facts say nothing about whether a loop does its job.
            Assert.Null(facts.QuerySelector("[data-testid='sessions-fact-model']"));
            Assert.Null(facts.QuerySelector("[data-testid='sessions-branch']"));
        });
    }

    [Fact]
    public void A_stopped_brain_tenders_facts_say_how_long_it_ran_and_count_no_dispatches()
    {
        using var context = new Harness(Session(TenderId, "🧠 BRAIN TENDER", state: AgentSessionState.Finished, lastActivity: Noon.AddMinutes(-30)));

        var pane = context.Render(view: "all");

        pane.WaitForAssertion(() =>
        {
            var facts = pane.Find("[data-testid='sessions-detail'] [data-testid='sessions-facts']");
            Assert.Equal(["Repository", "Machine", "Last check-in", "Ran for"], facts.QuerySelectorAll("dt").Select(term => term.TextContent.Trim()));
            Assert.Equal("None yet", facts.QuerySelector("[data-testid='sessions-fact-check-in']")!.TextContent.Trim());
            Assert.Equal("2 h 30 min", facts.QuerySelector("[data-testid='sessions-fact-running-for']")!.TextContent.Trim());
        });
    }

    // --- The pane re-reads the loops on its own (QA finding F5) ---------------------------------

    [Fact]
    public void A_loop_rows_chip_follows_a_new_check_in_on_the_panes_own_timer()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, activity: LoopActivity.Holding, at: Noon.AddSeconds(-10));

        var pane = context.Render();
        pane.WaitForAssertion(() => Assert.Equal("Holding", pane.Find("[data-testid='sessions-loop-row-state']").TextContent.Trim()));

        // The loop checks in again, Listening this time; nobody clicks anything.
        context.CheckIn(DispatcherId, LoopKind.Dispatcher, activity: LoopActivity.Listening, at: Noon.AddSeconds(20));
        context.Advance(SessionsPane.LoopRefreshInterval);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("Listening", pane.Find("[data-testid='sessions-loop-row-state']").TextContent.Trim());
            Assert.Equal("checked in 10 s ago", pane.Find("[data-testid='sessions-loop-row-check-in']").TextContent.Trim());
        });
    }

    [Fact]
    public void The_timer_stops_with_the_pane()
    {
        using var context = new Harness(Session(DispatcherId, "📡 SESSION DISPATCHER"));
        context.CheckIn(DispatcherId, LoopKind.Dispatcher);

        var pane = context.Render();
        pane.WaitForElement("[data-testid='sessions-loop-row-state']");
        var timer = Assert.Single(context.Timers);
        Assert.False(timer.Disposed);
        var reads = context.Lists;

        context.DisposeComponents();
        context.Advance(SessionsPane.LoopRefreshInterval * 3);

        Assert.True(timer.Disposed);
        Assert.Equal(reads, context.Lists);
    }

    // --- Helpers ---------------------------------------------------------------------

    /// <summary>A capacity tile's label, figure and footnote.</summary>
    private static (string Label, string Value, string Footnote) Tile(AngleSharp.Dom.IElement tiles, string testId)
    {
        var tile = tiles.QuerySelector($"[data-testid='{testId}']")!;

        return (
            tile.QuerySelector(".metric-tile__label")!.TextContent.Trim(),
            tile.QuerySelector(".metric-tile__value")!.TextContent.Trim(),
            tile.QuerySelector(".metric-tile__footnote")!.TextContent.Trim());
    }

    private static AgentSession Session(
        string id,
        string title,
        AgentSessionState state = AgentSessionState.Running,
        DateTimeOffset? lastActivity = null,
        bool? archived = null,
        string? scheduledTask = null,
        DateTimeOffset? startedAt = null) =>
        new AgentSession(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: title,
            WorkingFolder: $@"D:\Repos\Backlog\.claude\worktrees\{id}",
            Repository: "JSdotNet/Backlog",
            Branch: "main",
            StartedAt: startedAt ?? Noon.AddHours(-3),
            LastActivityAt: lastActivity ?? Noon.AddMinutes(-3),
            State: state,
            TurnCount: 4,
            Origin: AgentSessionOrigin.Local)
        {
            Archived = archived,
            ScheduledTask = scheduledTask
        };

    /// <summary>A bUnit context composed the way the desktop composes the pane, with the
    /// loop, schedule, queue and question stores held in memory against a pinned clock.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly BunitContext _context = new();
        private readonly IReadOnlyList<AgentSession> _sessions;
        private readonly FakeTimeProvider _clock = new(Noon);
        private readonly TrackingClock _paneClock;

        public Harness(params AgentSession[] sessions)
        {
            _sessions = sessions;
            Register = new InMemoryLoopRegister(_clock);
            _counted = new CountingLoopRegister(Register);
            _paneClock = new TrackingClock(_clock);
            Requests = new InMemorySessionRequestStore(_clock);
        }

        private readonly CountingLoopRegister _counted;

        public InMemoryLoopRegister Register { get; }

        /// <summary>The timers the pane created on its clock.</summary>
        public IReadOnlyList<TrackedTimer> Timers => _paneClock.Timers;

        /// <summary>How many times the pane listed the Loop Register.</summary>
        public int Lists => _counted.Lists;

        /// <summary>Moves the pinned clock on, firing any timer due by then.</summary>
        public void Advance(TimeSpan by) => _clock.Advance(by);

        /// <summary>Disposes the rendered pane, as closing the takeover does.</summary>
        public void DisposeComponents() => _context.DisposeComponentsAsync().GetAwaiter().GetResult();
        public InMemoryScheduleRegister Schedules { get; } = new();
        public InMemorySessionRequestStore Requests { get; }
        public HeldWaitingQuestions Questions { get; } = new();
        public CapacityReading? Capacity { get; set; }

        public void CheckIn(
            string sessionId,
            LoopKind kind,
            LoopActivity activity = LoopActivity.Listening,
            int heartbeatMinutes = 30,
            LoopHeldWork? held = null,
            DateTimeOffset? at = null) =>
            Register.CheckInAsync(LoopSessions.CheckIn(sessionId, kind, activity, heartbeatMinutes, held ?? LoopHeldWork.None, at ?? Noon.AddMinutes(-1))).GetAwaiter().GetResult();

        public async Task ScheduleAsync(string scheduleId, bool needsYou = false, bool failed = false, bool held = false)
        {
            var run = $"run-{scheduleId}";

            await Schedules.RecordAsync(new ScheduledRunReport(scheduleId, scheduleId, "JSdotNet/Backlog", null, run, ScheduledRunEvent.Started), Noon.AddHours(-2), TestContext.Current.CancellationToken);

            var report = failed
                ? new ScheduledRunReport(scheduleId, null, null, null, run, ScheduledRunEvent.Failed, Verdict: "The build broke.")
                : held
                    ? new ScheduledRunReport(scheduleId, null, null, null, run, ScheduledRunEvent.Skipped, HeldBy: "run-earlier")
                    : new ScheduledRunReport(scheduleId, null, null, null, run, ScheduledRunEvent.Finished, Verdict: "ok", NeedsYou: needsYou ? ["Review two pull requests."] : []);

            await Schedules.RecordAsync(report, Noon.AddHours(-1), TestContext.Current.CancellationToken);
        }

        /// <summary>The pane as the shell shows it: the Sessions surface, with the window's
        /// navigator and a catalog that offers the Dashboard or not.</summary>
        public IRenderedComponent<SessionsSurface> RenderSurface(ISurfaceNavigator navigator, bool offersDashboard)
        {
            Compose();
            _context.Services.AddSingleton(navigator);
            _context.Services.AddSingleton<ISurfaceCatalog>(new Catalog(offersDashboard));

            return _context.Render<SessionsSurface>();
        }

        public IRenderedComponent<SessionsPane> Render(string view = "live", Action? seeUsage = null)
        {
            Compose();

            var pane = seeUsage is null
                ? _context.Render<SessionsPane>()
                : _context.Render<SessionsPane>(parameters => parameters.Add(p => p.OnSeeUsage, seeUsage));

            if (view != "live")
            {
                pane.WaitForElement($"[data-testid='sessions-view-{view}']").Click();
            }

            return pane;
        }

        private void Compose()
        {
            _context.JSInterop.Mode = JSRuntimeMode.Loose;
            var source = new Sessions(_sessions);
            _context.Services.AddSingleton<TimeProvider>(_paneClock);
            _context.Services.AddSingleton<IAgentSessionSource>(source);
            _context.Services.AddSingleton<IDeliveryRunSource>(new SessionsPaneTests.StubRunSource([], []));
            _context.Services.AddSingleton<ILoopRegister>(_counted);
            _context.Services.AddSingleton(new LoopSessionMonitor(_counted, source, _clock));
            _context.Services.AddSingleton<ISessionRequestStore>(Requests);
            _context.Services.AddSingleton<IWaitingQuestionStore>(Questions);
            _context.Services.AddSingleton<ICapacityReader>(new FixedCapacity(this));
            _context.Services.AddSingleton<IScheduleRegister>(Schedules);
        }

        public void Dispose() => _context.Dispose();

        /// <summary>A catalog that offers the Dashboard, or nothing.</summary>
        private sealed class Catalog(bool offersDashboard) : ISurfaceCatalog
        {
            private static readonly ShellSurface Dashboard = new("dashboard", "Dashboard", ShellSurfaceGroup.Takeover, 50, typeof(object));

            public IReadOnlyList<ShellSurface> Surfaces => offersDashboard ? [Dashboard] : [];

            public bool TryGet(string surfaceId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ShellSurface? surface)
            {
                surface = offersDashboard && surfaceId == Dashboard.Id ? Dashboard : null;
                return surface is not null;
            }
        }

        /// <summary>The pinned clock, remembering every timer created on it.</summary>
        private sealed class TrackingClock(FakeTimeProvider inner) : TimeProvider
        {
            private readonly List<TrackedTimer> _timers = [];

            public IReadOnlyList<TrackedTimer> Timers { get { lock (_timers) return [.. _timers]; } }

            public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

            public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

            public override long TimestampFrequency => inner.TimestampFrequency;

            public override long GetTimestamp() => inner.GetTimestamp();

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                var timer = new TrackedTimer(inner.CreateTimer(callback, state, dueTime, period));
                lock (_timers) _timers.Add(timer);
                return timer;
            }
        }

        /// <summary>The register, counting how often it is listed.</summary>
        private sealed class CountingLoopRegister(ILoopRegister inner) : ILoopRegister
        {
            private int _lists;

            public int Lists => Volatile.Read(ref _lists);

            public Task<LoopSession> CheckInAsync(LoopCheckIn checkIn, CancellationToken cancellationToken = default) => inner.CheckInAsync(checkIn, cancellationToken);

            public Task<LoopSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default) => inner.GetAsync(sessionId, cancellationToken);

            public Task<IReadOnlyList<LoopSession>> ListAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _lists);
                return inner.ListAsync(cancellationToken);
            }

            public Task<LoopSession?> SetCommandAsync(string sessionId, LoopCommand command, CancellationToken cancellationToken = default) =>
                inner.SetCommandAsync(sessionId, command, cancellationToken);
        }

        private sealed class FixedCapacity(Harness harness) : ICapacityReader
        {
            public Task<CapacityReading> ReadAsync(CapacityLimits limits, string? dispatcher = null, CancellationToken cancellationToken = default) =>
                Task.FromResult(harness.Capacity ?? new CapacityReading(null, null, null, null, limits));
        }
    }

    /// <summary>A timer that says whether it was disposed.</summary>
    private sealed class TrackedTimer(ITimer inner) : ITimer
    {
        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

        public void Dispose()
        {
            Disposed = true;
            inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return inner.DisposeAsync();
        }
    }

    private sealed class Sessions(IReadOnlyList<AgentSession> sessions) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Count));
    }
}
