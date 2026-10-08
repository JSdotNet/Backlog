using Backlog.Modules.Sessions.UI;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The rebuilt pane's own parts: the filter bar's Live tally, picking a row into the
/// detail panel, the model and the effort a row ran at, the origin notes, and the
/// run, pull request and task cards — the pull request wearing the verdict the host
/// reads for it.
/// </summary>
public sealed class SessionsPaneDetailTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private const string Folder = @"D:\Repos\Backlog\.claude\worktrees\keen-bose-667825";

    private static readonly string Worktree = DeliveryRunWorktrees.KeyOf(Folder)!;

    [Fact]
    public void The_first_row_drawn_is_in_the_panel_until_another_is_pressed()
    {
        using var context = Context([Claude, Copilot], []);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var rows = pane.FindAll("[data-testid='sessions-row']");

            Assert.Equal(["true", "false"], rows.Select(row => row.GetAttribute("aria-pressed")));
            Assert.Equal("keen-bose-667825", pane.Find("[data-testid='sessions-detail-title']").TextContent.Trim());
        });

        SessionsPaneTests.Pick(pane, "Copilot/0012e2c7");

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["false", "true"], pane.FindAll("[data-testid='sessions-row']").Select(row => row.GetAttribute("aria-pressed")));
            Assert.Equal("JSdotNet/Backlog", pane.Find("[data-testid='sessions-detail-title']").TextContent.Trim());
        });
    }

    /// <summary>A panel about a row the filters hide would contradict the list, so a
    /// filter that hides the picked row moves the panel to the first one drawn.</summary>
    [Fact]
    public void A_filter_that_hides_the_picked_row_moves_the_panel_to_the_first_drawn()
    {
        using var context = Context([Claude, Copilot with { EnvironmentId = "laptop", Environment = "DEV-LAPTOP" }], []);

        var pane = context.Render<SessionsPane>();

        SessionsPaneTests.Pick(pane, "Copilot/0012e2c7");
        pane.Find("[data-testid='sessions-machine-filter'] select").Change("tower");

        pane.WaitForAssertion(() =>
            Assert.Equal("Claude/5905cf2d", pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key")));
    }

    [Fact]
    public void Live_carries_how_many_rows_it_would_show_under_the_other_filters()
    {
        using var context = Context([Claude, Copilot with { State = AgentSessionState.Finished, LastActivityAt = Noon.AddDays(-1) }], []);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.Equal("1", pane.Find("[data-testid='sessions-live-tally']").TextContent.Trim()));

        // Show does not move it — it counts what Live would show, not what is shown.
        pane.Find("[data-testid='sessions-view-all']").Click();
        pane.WaitForAssertion(() => Assert.Equal("1", pane.Find("[data-testid='sessions-live-tally']").TextContent.Trim()));

        // A filter that removes the live row does.
        pane.Find("[data-testid='sessions-runs-only']").Click();
        pane.WaitForAssertion(() => Assert.Equal("0", pane.Find("[data-testid='sessions-live-tally']").TextContent.Trim()));
    }

    [Fact]
    public void The_heading_says_running_now_on_live_and_names_both_agents_on_all()
    {
        using var context = Context([Claude], []);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.Equal("Running now", pane.Find("#sessions-title").TextContent.Trim()));

        pane.Find("[data-testid='sessions-view-all']").Click();

        pane.WaitForAssertion(() => Assert.Equal("Claude and Copilot", pane.Find("#sessions-title").TextContent.Trim()));
    }

    /// <summary>
    /// The effort the owner session ran at, as the run file's inline stages recorded
    /// it, on the row after the model and among the panel's facts — in the ladder's
    /// own treatment.
    /// </summary>
    [Fact]
    public void The_effort_a_run_recorded_for_its_owner_session_is_a_chip_on_the_row_and_in_the_panel()
    {
        var session = Claude with { ModelUsage = [new AgentModelUsage("claude-opus-5-5", 100, 182_000, 0, 0)] };
        var run = Run() with
        {
            Stages =
            [
                new DeliveryRunStage("Scope", "done", 60_000, 1) { Execution = """{"mode":"inline","effort":"xhigh"}""" },
                new DeliveryRunStage("Implement", "in_progress", null, 0) { Execution = """{"mode":"delegate","agent":"csharp-coding:coding","effort":"low"}""" }
            ]
        };

        using var context = Context([session], [run]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var row = pane.Find("[data-testid='sessions-row']");
            var chip = row.QuerySelector("[data-testid='sessions-effort']")!;

            Assert.Contains("Opus 5.5", row.QuerySelector("[data-testid='sessions-model']")!.TextContent);
            Assert.Equal("xhigh", chip.TextContent.Trim());
            Assert.Contains("effort-chip--xhigh", chip.ClassName);
            Assert.Equal("Effort: xhigh", chip.GetAttribute("title"));
            Assert.Equal("182.0K out", row.QuerySelector("[data-testid='sessions-tokens']")!.TextContent.Trim());

            var fact = pane.Find("[data-testid='sessions-fact-effort']");
            Assert.Equal("xhigh", fact.QuerySelector("[data-testid='sessions-fact-effort-chip']")!.TextContent.Trim());
            Assert.Equal("Opus 5.5", pane.Find("[data-testid='sessions-fact-model']").TextContent.Trim());
        });
    }

    [Fact]
    public void With_no_recorded_effort_there_is_no_chip_and_the_panel_says_not_recorded()
    {
        using var context = Context([Claude], [Run()]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll(".effort-chip"));
            Assert.Equal("not recorded", pane.Find("[data-testid='sessions-fact-effort']").TextContent.Trim());
        });
    }

    [Fact]
    public void A_copilot_row_says_its_state_is_read_from_silence()
    {
        using var context = Context([Claude, Copilot], []);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='sessions-copilot-liveness']")));

        SessionsPaneTests.Pick(pane, "Copilot/0012e2c7");

        pane.WaitForAssertion(() => Assert.Contains(
            "Copilot leaves no liveness marker",
            pane.Find("[data-testid='sessions-copilot-liveness']").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_session_with_no_run_says_so_where_the_run_card_would_be()
    {
        using var context = Context([Claude], []);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='sessions-run-card']"));
            Assert.Empty(pane.FindAll("[data-testid='sessions-stage-strip']"));
            Assert.StartsWith("No delivery run reported from this session.", pane.Find("[data-testid='sessions-no-run']").TextContent.Trim(), StringComparison.Ordinal);
            Assert.Equal("No pull request yet.", pane.Find("[data-testid='sessions-pull-request-card'] .sessions-detail__none").TextContent.Trim());
            Assert.Equal("No task names this session.", pane.Find("[data-testid='sessions-task-card'] .sessions-detail__none").TextContent.Trim());
        });
    }

    /// <summary>
    /// The pull request card wears the readiness verdict the host read for it — the
    /// label, the tone's chip and the reason in its title — asked once per pull
    /// request however often the panel redraws.
    /// </summary>
    [Fact]
    public void The_pull_request_card_wears_the_verdict_the_host_read()
    {
        var session = Claude with
        {
            PullRequests = [new AgentPullRequest("JSdotNet/Backlog", 1036, "https://github.com/JSdotNet/Backlog/pull/1036", Noon.AddHours(-1))]
        };
        var asked = new List<DeliveryRunReference>();

        using var context = Context([session], []);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.PullRequestStatus, pull =>
            {
                asked.Add(pull);
                return Task.FromResult<SessionPullRequestStatus?>(new("Failing checks", "fault", "Roll out devbook 1.19 duration fields", "2 checks failed on main."));
            }));

        pane.WaitForAssertion(() =>
        {
            var card = pane.Find("[data-testid='sessions-pull-request-card']");
            var chip = card.QuerySelector("[data-testid='sessions-pull-request-verdict']")!;

            Assert.Equal("Failing checks", chip.TextContent.Trim());
            Assert.Contains("badge--tone-fault", chip.ClassName);
            Assert.Equal("2 checks failed on main.", chip.GetAttribute("title"));
            Assert.Contains("Roll out devbook 1.19 duration fields", card.TextContent);
            Assert.Equal("https://github.com/JSdotNet/Backlog/pull/1036", card.QuerySelector("[data-testid='sessions-pull-request']")!.GetAttribute("href"));
        });

        pane.Find("[data-testid='sessions-view-all']").Click();
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-pull-request-verdict']")));

        var only = Assert.Single(asked);
        Assert.Equal("JSdotNet/Backlog", only.Repository);
        Assert.Equal("PR #1036", only.Label);
    }

    [Fact]
    public void Without_a_verdict_the_pull_request_card_claims_none()
    {
        var session = Claude with
        {
            PullRequests = [new AgentPullRequest("JSdotNet/Backlog", 1036, "https://github.com/JSdotNet/Backlog/pull/1036", Noon.AddHours(-1))]
        };

        using var context = Context([session], []);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.PullRequestStatus, _ => Task.FromException<SessionPullRequestStatus?>(new HttpRequestException("offline"))));

        pane.WaitForAssertion(() =>
        {
            Assert.Single(pane.FindAll("[data-testid='sessions-pull-request-card'] [data-testid='sessions-pull-request']"));
            Assert.Empty(pane.FindAll("[data-testid='sessions-pull-request-verdict']"));
        });
    }

    [Fact]
    public void The_task_card_names_the_entry_and_opens_it()
    {
        var entry = new DeliveryRunReference(DeliveryRunReferenceKind.Task, "BL-418", "PR and session pages redesign", null, null, EntryId: Guid.NewGuid());
        DeliveryRunReference? opened = null;

        using var context = Context([Claude], []);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.SessionTask, _ => entry)
            .Add(p => p.OnOpenTask, EventCallback.Factory.Create<DeliveryRunReference>(this, reference => opened = reference)));

        pane.WaitForAssertion(() =>
        {
            var card = pane.Find("[data-testid='sessions-task-card']");

            Assert.Contains("BL-418", card.QuerySelector("[data-testid='sessions-task']")!.TextContent);
            Assert.Contains("PR and session pages redesign", card.TextContent);
        });

        pane.Find("[data-testid='sessions-task-card'] [data-testid='sessions-task']").Click();

        pane.WaitForAssertion(() => Assert.Same(entry, opened));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0.5, "under a minute")]
    [InlineData(38.0, "38 min")]
    [InlineData(60.0, "1 h")]
    [InlineData(72.0, "1 h 12 min")]
    [InlineData(3000.0, "2 d")]
    public void A_duration_reads_the_way_a_person_says_one(double? minutes, string? expected) =>
        Assert.Equal(expected, SessionsPane.DurationLabel(minutes is { } value ? TimeSpan.FromMinutes(value) : null));

    /// <summary>
    /// What the session cost: Claude Code's reported sum where its telemetry reached
    /// the app, an estimate from the run's tokens at the Settings prices marked so where
    /// it did not, and no fact at all with neither — never a zero.
    /// </summary>
    [Fact]
    public void The_panel_shows_the_cost_Claude_Code_reported_for_the_session()
    {
        var requests = new StubRequestStore(
        [
            new ClaudeApiRequest("req_1", Claude.Id, Noon.AddMinutes(-30), "claude-opus-5-5", "high", 1_500_000, 1, 2, 3, 4, 5, "repl_main_thread", null, null, null),
            new ClaudeApiRequest("req_2", Claude.Id, Noon.AddMinutes(-20), "claude-opus-5-5", "high", 250_000, 1, 2, 3, 4, 5, "agent:custom", "Explore", null, null)
        ]);

        using var context = Context([Claude], [Run()]);
        context.Services.AddSingleton<IClaudeApiRequestStore>(requests);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var cost = pane.Find("[data-testid='sessions-fact-cost']");

            Assert.Equal("$1.75", cost.TextContent.Trim());
            Assert.Equal("reported", cost.GetAttribute("data-source"));
        });
    }

    [Fact]
    public void Without_telemetry_the_panel_estimates_the_cost_from_tokens_and_says_so()
    {
        var run = Run() with
        {
            Stages = [new DeliveryRunStage("Scope", "done", 60_000, 1) { Execution = """{"mode":"inline","model":"claude-opus-5-5"}""" }],
            TokenUsage = new DeliveryRunTokenUsage(
                new DeliveryRunTokens(1, 1_000_000, 0, 0, 0, 0),
                new DeliveryRunTokens(0, 0, 0, 0, 0, 0),
                [new DeliveryRunStageTokens("Scope", new DeliveryRunTokens(1, 1_000_000, 0, 0, 0, 0), new DeliveryRunTokens(0, 0, 0, 0, 0, 0))],
                ["claude-opus-5-5"])
        };

        using var context = Context([Claude], [run]);
        context.Services.AddSingleton<IClaudeApiRequestStore>(new StubRequestStore([]));
        context.Services.AddSingleton<IModelPriceStore>(new StubPriceStore(new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m)])));

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var cost = pane.Find("[data-testid='sessions-fact-cost']");

            Assert.Equal("estimated", cost.GetAttribute("data-source"));
            Assert.Contains("$5.00", cost.TextContent);
            Assert.Contains("estimated from tokens", cost.TextContent);
        });
    }

    [Fact]
    public void A_refresh_reads_the_prices_again_and_works_the_cost_out_anew()
    {
        var session = Claude with { ModelUsage = [new AgentModelUsage("claude-opus-5-5", 1_000_000, 0, 0, 0)] };
        var prices = new StubPriceStore(ModelPriceTable.Empty);

        using var context = Context([session], [Run()]);
        context.Services.AddSingleton<IModelPriceStore>(prices);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='sessions-fact-output']")));
        Assert.Empty(pane.FindAll("[data-testid='sessions-fact-cost']"));

        _ = prices.SaveAsync(new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m)]), TestContext.Current.CancellationToken);
        pane.Find("[data-testid='sessions-refresh']").Click();

        pane.WaitForAssertion(() => Assert.Contains("$5.00", pane.Find("[data-testid='sessions-fact-cost']").TextContent));
    }

    [Fact]
    public void With_neither_telemetry_nor_a_price_the_panel_shows_no_cost()
    {
        using var context = Context([Claude], [Run()]);
        context.Services.AddSingleton<IClaudeApiRequestStore>(new StubRequestStore([]));

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='sessions-fact-output']")));
        Assert.Empty(pane.FindAll("[data-testid='sessions-fact-cost']"));
    }

    /// <summary>
    /// A run against its usual: the cost on the run's header in place of the output
    /// tokens, each stage's share and ratio, and the insight line naming the stage
    /// that explains the difference — against five earlier finished runs of its flow.
    /// </summary>
    [Fact]
    public void A_finished_run_reads_its_cost_against_the_usual_run_of_its_flow()
    {
        static DeliveryRunTokenUsage Spent(long input) => new(
            new DeliveryRunTokens(1, input, 0, 0, 0, 0),
            new DeliveryRunTokens(0, 0, 0, 0, 0, 0),
            [new DeliveryRunStageTokens("Implement", new DeliveryRunTokens(1, input, 0, 0, 0, 0), new DeliveryRunTokens(0, 0, 0, 0, 0, 0))],
            ["claude-opus-5-5"]);

        static DeliveryRun Priced(string id, DateTimeOffset started, long input, int done = 1) =>
            SessionRowsTests.Run(id, Worktree, started, started.AddMinutes(30)) with
            {
                SkillId = "flow-code",
                Stages = [new DeliveryRunStage("Implement", "done", 60_000, done) { Execution = """{"mode":"inline","model":"claude-opus-5-5"}""" }],
                TokenUsage = Spent(input)
            };

        var now = Priced("run-now", Noon.AddMinutes(-90), 2_000_000, done: 2) with { SessionIds = [Claude.Id] };
        var earlier = Enumerable.Range(1, 5).Select(day => Priced($"run-{day}", Noon.AddDays(-day), 1_000_000) with { Worktree = $"wt-{day}-1a2b3c4d" });

        using var context = Context([Claude], [now, .. earlier]);
        context.Services.AddSingleton<IClaudeApiRequestStore>(new StubRequestStore([]));
        context.Services.AddSingleton<IModelPriceStore>(new StubPriceStore(new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m)])));

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var line = pane.Find("[data-testid='sessions-run'][data-run-id='run-now']");

            Assert.Contains("$10.00 est. cost", line.QuerySelector(".fold__trigger")!.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("output tokens", line.QuerySelector(".fold__trigger")!.TextContent, StringComparison.Ordinal);

            var insight = line.QuerySelector("[data-testid='sessions-run-insight']")!;
            Assert.Equal("higher", insight.GetAttribute("data-band"));
            Assert.Equal("2.0× the usual flow-code run · Implement explains most of it: $5.00 over its usual, entered again once", insight.TextContent);

            Assert.StartsWith("100%", line.QuerySelector("[data-testid='sessions-stage-share']")!.TextContent);
            Assert.StartsWith("2.0×", line.QuerySelector("[data-testid='sessions-stage-ratio']")!.TextContent);
        });
    }

    /// <summary>
    /// The session's and each run's share of the week's reported cost, and once a weekly
    /// refusal is recorded, an estimate of the weekly limit beside it — Fable's on a line
    /// of its own.
    /// </summary>
    [Fact]
    public void The_panel_and_the_run_show_their_share_of_the_week()
    {
        var requests = new StubRequestStore(
        [
            new ClaudeApiRequest("req_1", Claude.Id, Noon.AddMinutes(-30), "claude-opus-5-5", "high", 1_000_000, 1, 2, 3, 4, 5, "repl_main_thread", null, null, null),
            new ClaudeApiRequest("req_2", "another", Noon.AddDays(-2), "claude-opus-5-5", "high", 3_000_000, 1, 2, 3, 4, 5, "repl_main_thread", null, null, null),
            new ClaudeApiRequest("req_old", "another", Noon.AddDays(-9), "claude-opus-5-5", "high", 50_000_000, 1, 2, 3, 4, 5, "repl_main_thread", null, null, null)
        ]);
        var run = Run() with
        {
            Stages = [new DeliveryRunStage("Scope", "done", 60_000, 1) { StartedAt = Noon.AddMinutes(-40), CompletedAt = Noon.AddMinutes(-20), Execution = """{"mode":"inline","model":"claude-opus-5-5"}""" }]
        };

        using var context = Context([Claude], [run]);
        context.Services.AddSingleton<IClaudeApiRequestStore>(requests);
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Noon));

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("25% of this week", pane.Find("[data-testid='sessions-share'] [data-testid='usage-share-week']").TextContent.Trim());
            Assert.Contains("25% of this week", pane.Find("[data-testid='sessions-run'] [data-testid='sessions-run-share']").TextContent);
            Assert.Empty(pane.FindAll("[data-testid='usage-share-limit']"));
        });
    }

    [Fact]
    public void After_a_weekly_refusal_the_share_carries_an_estimate_of_the_limit()
    {
        var reset = Noon.AddDays(3);
        var requests = new StubRequestStore(
        [
            new ClaudeApiRequest("req_before", "another", Noon.AddDays(-1).AddHours(-1), "claude-opus-5-5", "high", 9_000_000, 1, 2, 3, 4, 5, "repl_main_thread", null, null, null),
            new ClaudeApiRequest("req_fable", "another", Noon.AddHours(-6), "claude-fable-1", "high", 2_000_000, 1, 2, 3, 4, 5, "repl_main_thread", null, null, null),
            new ClaudeApiRequest("req_1", Claude.Id, Noon.AddMinutes(-30), "claude-opus-5-5", "high", 900_000, 1, 2, 3, 4, 5, "repl_main_thread", null, null, null),
            new ClaudeApiRequest("req_2", Claude.Id, Noon.AddMinutes(-25), "claude-fable-1", "high", 100_000, 1, 2, 3, 4, 5, "repl_main_thread", null, null, null)
        ]);
        var hits = new AgentSessionActivity("other", AgentSessionKind.Claude, "tower", "DEV-TOWER", [], [])
        {
            LimitHits =
            [
                new AgentLimitHit(Noon.AddDays(-1), AgentLimitKind.Weekly, "seven_day") { ResetsAt = reset },
                new AgentLimitHit(Noon.AddHours(-5), AgentLimitKind.WeeklyFable, "seven_day_overage_included") { ResetsAt = reset }
            ]
        };

        using var context = Context([Claude], [Run()]);
        context.Services.AddSingleton<IClaudeApiRequestStore>(requests);
        context.Services.AddSingleton<IAgentSessionRecordStore>(new StubRecordStore(new AgentSessionRecord(Copilot, hits, Noon)));
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Noon));

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var share = pane.Find("[data-testid='sessions-share']");

            Assert.Equal("8.3% of this week", share.QuerySelector("[data-testid='usage-share-week']")!.TextContent.Trim());
            Assert.Equal("≈ 11% of the weekly limit estimate", Squash(share.QuerySelector("[data-testid='usage-share-limit']")!.TextContent));
            Assert.Equal("Fable ≈ 5% of its weekly limit estimate", Squash(share.QuerySelector("[data-testid='usage-share-fable']")!.TextContent));
        });
    }

    private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class StubRecordStore(params AgentSessionRecord[] records) : IAgentSessionRecordStore
    {
        public IReadOnlyList<AgentSessionRecord> All() => records;

        public SessionRecordUpdate Save(IReadOnlyList<AgentSessionRecord> readings) => SessionRecordUpdate.None;
    }

    internal sealed class StubRequestStore(IReadOnlyList<ClaudeApiRequest> requests) : IClaudeApiRequestStore
    {
        public Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> added, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClaudeApiRequest>>([.. requests.Where(request => sessionId is null || request.SessionId == sessionId)]);
    }

    internal sealed class StubPriceStore(ModelPriceTable table) : IModelPriceStore
    {
        public ModelPriceTable Table { get; private set; } = table;

        public Task<ModelPriceTable> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Table);

        public Task SaveAsync(ModelPriceTable saved, CancellationToken cancellationToken = default)
        {
            Table = saved;
            return Task.CompletedTask;
        }
    }

    private static DeliveryRun Run() =>
        SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10), status: "in_progress") with
        {
            SessionIds = [Claude.Id]
        };

    private static BunitContext Context(IReadOnlyList<AgentSession> sessions, IReadOnlyList<DeliveryRun> runs)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IAgentSessionSource>(new StubSessionSource(sessions));
        context.Services.AddSingleton<IDeliveryRunSource>(new SessionsPaneTests.StubRunSource(runs, []));

        return context;
    }

    private static readonly AgentSession Claude = new(
        Id: "5905cf2d",
        Kind: AgentSessionKind.Claude,
        EnvironmentId: "tower",
        Environment: "DEV-TOWER",
        Title: "keen-bose-667825",
        WorkingFolder: Folder,
        Repository: null,
        Branch: "claude/desktop-session-area",
        StartedAt: Noon.AddHours(-2),
        LastActivityAt: Noon.AddMinutes(-3),
        State: AgentSessionState.Running,
        TurnCount: 12,
        Origin: AgentSessionOrigin.Local);

    private static readonly AgentSession Copilot = new(
        Id: "0012e2c7",
        Kind: AgentSessionKind.Copilot,
        EnvironmentId: "tower",
        Environment: "DEV-TOWER",
        Title: "JSdotNet/Backlog",
        WorkingFolder: @"C:\Users\dev\.copilot\repos\Backlog",
        Repository: "JSdotNet/Backlog",
        Branch: "main",
        StartedAt: Noon.AddHours(-1),
        LastActivityAt: Noon.AddMinutes(-20),
        State: AgentSessionState.Running,
        TurnCount: null,
        Origin: AgentSessionOrigin.Local);

    private sealed class StubSessionSource(IReadOnlyList<AgentSession> sessions) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Count));
    }
}
