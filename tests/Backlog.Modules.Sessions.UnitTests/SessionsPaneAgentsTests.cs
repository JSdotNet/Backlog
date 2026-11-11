using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
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
        string? scheduledTask = null) =>
        new AgentSession(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: title,
            WorkingFolder: $@"D:\Repos\Backlog\.claude\worktrees\{id}",
            Repository: "JSdotNet/Backlog",
            Branch: "main",
            StartedAt: Noon.AddHours(-3),
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

        public Harness(params AgentSession[] sessions)
        {
            _sessions = sessions;
            Register = new InMemoryLoopRegister(_clock);
            Requests = new InMemorySessionRequestStore(_clock);
        }

        public InMemoryLoopRegister Register { get; }
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

        public IRenderedComponent<SessionsPane> Render(string view = "live")
        {
            _context.JSInterop.Mode = JSRuntimeMode.Loose;
            var source = new Sessions(_sessions);
            _context.Services.AddSingleton<TimeProvider>(_clock);
            _context.Services.AddSingleton<IAgentSessionSource>(source);
            _context.Services.AddSingleton<IDeliveryRunSource>(new SessionsPaneTests.StubRunSource([], []));
            _context.Services.AddSingleton<ILoopRegister>(Register);
            _context.Services.AddSingleton(new LoopSessionMonitor(Register, source, _clock));
            _context.Services.AddSingleton<ISessionRequestStore>(Requests);
            _context.Services.AddSingleton<IWaitingQuestionStore>(Questions);
            _context.Services.AddSingleton<ICapacityReader>(new FixedCapacity(this));
            _context.Services.AddSingleton<IScheduleRegister>(Schedules);

            var pane = _context.Render<SessionsPane>();

            if (view != "live")
            {
                pane.WaitForElement($"[data-testid='sessions-view-{view}']").Click();
            }

            return pane;
        }

        public void Dispose() => _context.Dispose();

        private sealed class FixedCapacity(Harness harness) : ICapacityReader
        {
            public Task<CapacityReading> ReadAsync(CapacityLimits limits, string? dispatcher = null, CancellationToken cancellationToken = default) =>
                Task.FromResult(harness.Capacity ?? new CapacityReading(null, null, null, null, limits));
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
