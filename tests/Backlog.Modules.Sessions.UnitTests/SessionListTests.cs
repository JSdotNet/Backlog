namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// What the rebuilt session list reads off a row before it draws one: the When and
/// Repository groupings, and the model, effort, output and duration each row shows.
/// All pure functions over records, so none of them needs a profile underneath.
/// </summary>
public sealed class SessionListTests
{
    /// <summary>Noon on a Wednesday, in a zone an hour ahead of UTC, so a day
    /// boundary in UTC and one on the reader's clock are an hour apart.</summary>
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

    private static readonly DateTimeOffset Now = new(2026, 10, 7, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void By_when_live_sessions_come_first_then_the_finished_ones_by_day()
    {
        var rows = Rows(
            Session("running", Now.AddMinutes(-1), AgentSessionState.Running),
            Session("stalled", Now.AddHours(-2), AgentSessionState.Stalled),
            Session("this-morning", Now.AddHours(-3)),
            Session("yesterday", Now.AddDays(-1)),
            Session("monday", Now.AddDays(-2)),
            Session("last-month", Now.AddDays(-30)));

        var groups = AgentSessionGroups.Of(rows, AgentSessionGrouping.When, Now, Zone);

        Assert.Equal(["Live now", "Earlier today", "Yesterday", "Past week", "Older"], groups.Select(group => group.Name));
        Assert.Equal(["running", "stalled"], groups[0].Rows.Select(row => row.Id));
        Assert.Equal(["live", "today", "yesterday", "week", "older"], groups.Select(group => group.Key));
    }

    /// <summary>The day a finished session lands in is the reader's day, not UTC's:
    /// 23:30 UTC is already tomorrow an hour east.</summary>
    [Fact]
    public void By_when_days_are_counted_on_the_readers_clock()
    {
        // 00:30 local on the 7th is 23:30 UTC on the 6th.
        var justAfterMidnight = new DateTimeOffset(2026, 10, 6, 23, 30, 0, TimeSpan.Zero);

        var group = Assert.Single(AgentSessionGroups.Of(Rows(Session("early", justAfterMidnight)), AgentSessionGrouping.When, Now, Zone));

        Assert.Equal("Earlier today", group.Name);
    }

    [Fact]
    public void By_when_skips_a_bucket_with_nothing_in_it()
    {
        var groups = AgentSessionGroups.Of(Rows(Session("old", Now.AddDays(-40))), AgentSessionGrouping.When, Now, Zone);

        Assert.Equal(["Older"], groups.Select(group => group.Name));
    }

    [Fact]
    public void By_repository_is_a_section_per_repository_by_name_with_the_unplaced_last()
    {
        var rows = Rows(
            Session("b", Now, repository: "JSdotNet/marketplace"),
            Session("a", Now.AddMinutes(-1), repository: "JSdotNet/Backlog"),
            Session("none", Now.AddMinutes(-2)),
            Session("a2", Now.AddMinutes(-3), repository: "jsdotnet/backlog"));

        var groups = AgentSessionGroups.Of(rows, AgentSessionGrouping.Repository, Now, Zone);

        Assert.Equal(["JSdotNet/Backlog", "JSdotNet/marketplace", null], groups.Select(group => group.Key));
        Assert.Equal(["a", "a2"], groups[0].Rows.Select(row => row.Id));
        Assert.Equal("No repository", groups[2].Name);
    }

    [Theory]
    [InlineData(AgentSessionGrouping.When)]
    [InlineData(AgentSessionGrouping.Repository)]
    public void The_new_groupings_carry_every_row(AgentSessionGrouping grouping)
    {
        var rows = Rows(
            Session("a", Now, AgentSessionState.Running, repository: "JSdotNet/Backlog"),
            Session("b", Now.AddDays(-1)),
            Session("c", Now.AddDays(-9), repository: "JSdotNet/devbook"));

        var grouped = AgentSessionGroups.Of(rows, grouping, Now, Zone).SelectMany(group => group.Rows);

        Assert.Equal(["a", "b", "c"], grouped.Select(row => row.Id).Order());
    }

    [Fact]
    public void The_owner_sessions_effort_is_the_latest_inline_stage_that_recorded_one()
    {
        var run = Run(
            Stage("Scope", "done", """{"mode":"inline","effort":"high"}"""),
            Stage("Implement", "done", """{"mode":"delegate","agent":"csharp-coding:coding","effort":"xhigh"}"""),
            Stage("Ready", "done", """{"mode":"inline","model":"opus","effort":"max"}"""),
            Stage("Create Pull Request", "pending", null));

        Assert.Equal("max", DeliveryRunExecutions.OwnerEffort(run));
    }

    /// <summary>A delegated stage's effort is its sub-agent's, never the owner's.</summary>
    [Fact]
    public void A_delegated_stages_effort_is_not_the_owners()
    {
        var run = Run(Stage("Implement", "done", """{"mode":"delegate","agent":"csharp-coding:coding","effort":"xhigh"}"""));

        Assert.Null(DeliveryRunExecutions.OwnerEffort(run));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"mode":"inline"}""")]
    [InlineData("""{"mode":"inline","effort":""}""")]
    [InlineData("""["inline"]""")]
    public void No_recorded_effort_is_none(string? execution)
    {
        Assert.Null(DeliveryRunExecutions.OwnerEffort(Run(Stage("Scope", "done", execution))));
    }

    /// <summary>A stage that recorded no mode and no agent ran where the run did: in
    /// the owner session.</summary>
    [Fact]
    public void A_stage_with_no_mode_and_no_agent_is_the_owners()
    {
        Assert.Equal("high", DeliveryRunExecutions.OwnerEffort(Run(Stage("Scope", "done", """{"effort":"high"}"""))));
        Assert.Null(DeliveryRunExecutions.OwnerEffort(Run(Stage("Scope", "done", """{"agent":"qa:qa","effort":"high"}"""))));
    }

    [Fact]
    public void A_rows_effort_is_its_first_run_that_recorded_one()
    {
        var row = new SessionRow(
            Session("s", Now),
            [Run(Stage("Scope", "done", null)), Run(Stage("Scope", "done", """{"mode":"inline","effort":"medium"}"""))]);

        Assert.Equal("medium", row.Effort);
        Assert.Null(SessionRow.Of(Session("t", Now)).Effort);
    }

    private static ClaudeApiRequest Request(string id, string session, DateTimeOffset at, string? effort, string? agent = null) =>
        new(id, session, at, "claude-opus-5-5", effort, 1, 1, 1, 1, 1, 1, agent is null ? "repl_main_thread" : "agent:custom", agent, null, null);

    [Fact]
    public void Telemetry_effort_is_the_owner_sessions_latest_main_loop_request_that_reported_one()
    {
        var requests = new[]
        {
            Request("early", "s", Now.AddMinutes(-30), "medium"),
            Request("latest", "s", Now.AddMinutes(-10), "high"),
            // A sub-agent's effort is its own, even when it is the latest request.
            Request("sub-agent", "s", Now.AddMinutes(-5), "max", agent: "Explore"),
            // An unnamed sub-agent request is a sub-agent's too.
            new ClaudeApiRequest("unnamed", "s", Now.AddMinutes(-4), "claude-opus-5-5", "low", 1, 1, 1, 1, 1, 1, "agent:custom", null, null, null),
            // A request that carried no effort says nothing.
            Request("silent", "s", Now.AddMinutes(-2), null),
            // Another session's effort is not this one's.
            Request("other", "t", Now.AddMinutes(-1), "xhigh")
        };

        Assert.Equal("high", DeliveryRunExecutions.TelemetryEffort("s", requests));
        Assert.Null(DeliveryRunExecutions.TelemetryEffort("nobody", requests));
    }

    /// <summary>A side query Claude Code makes for itself — a title, a compaction —
    /// carries its own effort, often on a small model, and is not the session's.</summary>
    [Fact]
    public void Telemetry_effort_skips_side_queries_and_requests_on_another_model()
    {
        var requests = new[]
        {
            Request("main", "s", Now.AddMinutes(-30), "high"),
            new ClaudeApiRequest("sdk-main", "s", Now.AddMinutes(-20), "claude-opus-5-5", "xhigh", 1, 1, 1, 1, 1, 1, "sdk", null, null, null),
            new ClaudeApiRequest("compact", "s", Now.AddMinutes(-10), "claude-opus-5-5", "low", 1, 1, 1, 1, 1, 1, "compact", null, null, null),
            new ClaudeApiRequest("title", "s", Now.AddMinutes(-5), "claude-haiku-5-5", "medium", 1, 1, 1, 1, 1, 1, "sdk", null, null, null)
        };

        // Without the session's model, an sdk request on haiku is indistinguishable from the main loop.
        Assert.Equal("medium", DeliveryRunExecutions.TelemetryEffort("s", requests));
        // With it, the haiku side query is left out, and the compaction always is.
        Assert.Equal("xhigh", DeliveryRunExecutions.TelemetryEffort("s", requests, "claude-opus-5-5"));
        Assert.Equal("xhigh", DeliveryRunExecutions.TelemetryEffort("s", requests, "Opus 5.5"));
        Assert.Null(DeliveryRunExecutions.TelemetryEffort(null, requests));
    }

    [Fact]
    public void A_rows_effort_prefers_telemetry_then_the_run_file_then_none()
    {
        var row = new SessionRow(Session("s", Now), [Run(Stage("Scope", "done", """{"mode":"inline","effort":"medium"}"""))]);

        Assert.Equal("xhigh", row.EffortFrom([Request("r", "s", Now, "xhigh")]));
        Assert.Equal("medium", row.EffortFrom([]));
        Assert.Equal("medium", row.EffortFrom([Request("r", "s", Now, "xhigh", agent: "Explore")]));
        Assert.Null(SessionRow.Of(Session("t", Now)).EffortFrom([]));

        // A row standing for a run alone reads the session its run names.
        var runOnly = new SessionRow(null, [Run(Stage("Scope", "done", null)) with { SessionIds = ["u"] }]);
        Assert.Equal("low", runOnly.EffortFrom([Request("r", "u", Now, "low")]));
    }

    [Fact]
    public void A_rows_model_is_the_one_it_spent_most_output_on()
    {
        var session = Session("s", Now) with
        {
            ModelUsage =
            [
                new AgentModelUsage("claude-haiku-4-5", 900, 10, 0, 0),
                new AgentModelUsage("claude-opus-5-5", 100, 5000, 0, 0)
            ]
        };

        var row = SessionRow.Of(session);

        Assert.Equal("claude-opus-5-5", row.Model);
        Assert.Equal(5010, row.OutputTokens);
    }

    [Fact]
    public void A_run_only_rows_model_and_output_come_from_the_run()
    {
        var run = Run() with
        {
            TokenUsage = new DeliveryRunTokenUsage(
                new DeliveryRunTokens(3, 100, 42_000, 0, 0, 0),
                new DeliveryRunTokens(0, 0, 0, 0, 0, 0),
                [],
                ["claude-sonnet-5-5"])
        };

        var row = new SessionRow(null, [run]);

        Assert.Equal("claude-sonnet-5-5", row.Model);
        Assert.Equal(42_000, row.OutputTokens);
    }

    [Fact]
    public void Nothing_recorded_is_no_model_and_no_output()
    {
        var row = SessionRow.Of(Session("s", Now));

        Assert.Null(row.Model);
        Assert.Null(row.OutputTokens);
    }

    [Fact]
    public void A_rows_duration_runs_from_its_start_to_its_last_activity()
    {
        var row = SessionRow.Of(Session("s", Now) with { StartedAt = Now.AddMinutes(-38) });

        Assert.Equal(TimeSpan.FromMinutes(38), row.Duration);
        Assert.Null(SessionRow.Of(Session("t", Now) with { StartedAt = null }).Duration);
    }

    [Theory]
    [InlineData("claude-opus-5-5", "Opus 5.5")]
    [InlineData("claude-sonnet-4-5-20250929", "Sonnet 4.5")]
    [InlineData("claude-haiku-4-5", "Haiku 4.5")]
    [InlineData("claude-opus-5-5[1m]", "Opus 5.5 [1m]")]
    [InlineData("opus", "Opus")]
    [InlineData("gpt-5", "gpt-5")]
    public void A_model_id_reads_as_its_family_and_version(string id, string expected) =>
        Assert.Equal(expected, AgentModels.Label(id));

    private static IReadOnlyList<SessionRow> Rows(params AgentSession[] sessions) =>
        [.. sessions.Select(SessionRow.Of)];

    private static AgentSession Session(
        string id,
        DateTimeOffset lastActivity,
        AgentSessionState state = AgentSessionState.Finished,
        string? repository = null) =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: id,
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: repository,
            Branch: null,
            StartedAt: lastActivity.AddMinutes(-10),
            LastActivityAt: lastActivity,
            State: state,
            TurnCount: null,
            Origin: AgentSessionOrigin.Local);

    private static DeliveryRunStage Stage(string name, string status, string? execution) =>
        new(name, status, DurationMs: null, DoneCount: status == "done" ? 1 : 0) { Execution = execution };

    private static DeliveryRun Run(params DeliveryRunStage[] stages) =>
        SessionRowsTests.Run("run", "backlog-1a2b3c4d", Now.AddHours(-1), Now) with { Stages = stages };
}
