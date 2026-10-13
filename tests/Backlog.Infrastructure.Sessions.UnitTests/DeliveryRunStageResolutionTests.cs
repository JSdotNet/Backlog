namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// How each stage of a delivery run ran, resolved over recorded run files — the port
/// of the <c>delivery-run-view</c> plugin's stage resolution.
/// <para>
/// The fixtures under <c>tests/Fixtures/delivery-runs/</c> are trimmed copies of real
/// run files from this machine's profile, read through the reader the product uses, so
/// the resolution meets the shapes the writers actually produced: a phase map nested
/// under its flow and one written flat, <c>delegated</c> spelled two ways, a Personal
/// Validation recorded as inline, effort runners with no phase map at all, and
/// fallbacks recorded on an inline run. Only the fields the reader does not use were
/// dropped; no value was changed.
/// </para>
/// </summary>
public sealed class DeliveryRunStageResolutionTests : IDisposable
{
    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        "backlog-stage-resolution-tests",
        Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task A_run_that_ran_as_configured_shows_no_difference()
    {
        var stages = await ResolveAsync("delegated-phase-map.json");

        Assert.All(stages, stage => Assert.Empty(stage.Differences));
        Assert.All(stages, stage => Assert.True(stage.ModeRecorded));
        Assert.All(stages, stage => Assert.True(stage.RunRecordsPhases));

        Assert.Equal(
            [
                DeliveryRunStageMode.Inline, DeliveryRunStageMode.Delegate, DeliveryRunStageMode.Delegate, DeliveryRunStageMode.Fork,
                DeliveryRunStageMode.Delegate, DeliveryRunStageMode.Delegate, DeliveryRunStageMode.Delegate, DeliveryRunStageMode.Inline,
                DeliveryRunStageMode.Gate, DeliveryRunStageMode.Inline, DeliveryRunStageMode.Delegate, DeliveryRunStageMode.Inline
            ],
            stages.Select(stage => stage.Mode));
    }

    [Fact]
    public async Task A_delegated_stage_names_its_agent_the_model_it_ran_on_and_its_configuration()
    {
        var scope = (await ResolveAsync("delegated-phase-map.json"))[1];

        Assert.Equal("Scope", scope.Name);
        Assert.Equal("architecture:architect", scope.Agent);

        // What ran: the configuration named no model, the sub-agent's record does.
        Assert.Equal("claude-opus-5-5", scope.Model);
        Assert.Null(scope.Effort);
        Assert.Equal("delivery:phase-scope", scope.Skill);

        // The MCP servers come from the resolved entry where the stage recorded none.
        Assert.Equal(["backlog"], scope.Mcp);

        var configured = Assert.IsType<DeliveryRunStageConfiguration>(scope.Configured);
        Assert.Equal("architecture:architect", configured.Agent);
        Assert.Null(configured.Model);
        Assert.Equal("team default, merged with user overlay", configured.Origin);

        var worker = Assert.Single(scope.Workers);
        Assert.Equal("architecture:architect", worker.Agent);
        Assert.Equal(646278, worker.DurationMs);
        Assert.False(worker.Revise);
        Assert.False(worker.Declared);

        Assert.Equal(37, scope.ToolCalls);
        Assert.Equal(964, scope.OutputTokens);
    }

    [Fact]
    public async Task The_skills_run_before_and_after_a_stage_come_from_its_entry()
    {
        var stages = await ResolveAsync("delegated-phase-map.json");

        Assert.Equal(["devbook:validate"], stages[0].Before);
        Assert.Equal(["devbook:update"], stages[11].After);
        Assert.Empty(stages[11].Before);

        // A stage whose entry and execution say nothing about servers says nothing.
        Assert.Null(stages[7].Mcp);
    }

    [Fact]
    public async Task Personal_Validation_is_the_gate_whatever_the_run_recorded()
    {
        // This writer recorded the gate as "inline", in the stage and in the map.
        var stages = await ResolveAsync("flat-phase-map.json");
        var gate = stages[8];

        Assert.Equal("Personal Validation", gate.Name);
        Assert.Equal(DeliveryRunStageMode.Gate, gate.Mode);
        Assert.True(gate.ModeRecorded);
        Assert.Empty(gate.Differences);
    }

    [Fact]
    public async Task A_flat_phase_map_and_the_delegated_spelling_are_read()
    {
        var stages = await ResolveAsync("flat-phase-map.json");

        Assert.All(stages, stage => Assert.True(stage.RunRecordsPhases));
        Assert.Equal(DeliveryRunStageMode.Delegate, stages[1].Mode);
        Assert.Equal("architecture:architect", stages[1].Configured?.Agent);
        Assert.Equal(DeliveryRunStageMode.Fork, stages[3].Mode);
        Assert.Equal(2, stages[2].Passes);
    }

    [Fact]
    public async Task A_stage_the_phase_map_has_no_mode_for_is_inferred_and_says_why()
    {
        var stages = await ResolveAsync("flat-phase-map.json");

        // Create Pull Request: the entry carries servers and no mode, and no sub-agent ran.
        var pullRequest = stages[9];
        Assert.Equal(DeliveryRunStageMode.Inline, pullRequest.Mode);
        Assert.True(pullRequest.Inferred);
        Assert.Contains("Neither the stage nor its phase entry recorded a mode: it is inferred.", DeliveryRunStageResolution.Explanations(pullRequest));

        // Report Back: the map holds no entry for it at all.
        var reportBack = stages[10];
        Assert.Null(reportBack.Configured);
        Assert.True(reportBack.Inferred);
        Assert.Contains("The run's phase map has no entry for this stage: its mode is inferred.", DeliveryRunStageResolution.Explanations(reportBack));
    }

    [Fact]
    public async Task Without_resolved_phases_mode_and_agent_are_inferred_from_what_ran()
    {
        var stages = await ResolveAsync("no-phase-map-runners.json");

        Assert.All(stages, stage => Assert.False(stage.RunRecordsPhases));
        Assert.All(stages, stage => Assert.Null(stage.Configured));

        // A stage a sub-agent worked in was delegated; the rest ran inline.
        var scope = stages[1];
        Assert.Equal(DeliveryRunStageMode.Delegate, scope.Mode);
        Assert.False(scope.ModeRecorded);
        Assert.Equal("general-purpose", scope.Agent);
        Assert.Equal(
            ["This run records no resolved phases: mode and agent are inferred."],
            DeliveryRunStageResolution.Explanations(scope));

        Assert.Equal(DeliveryRunStageMode.Inline, stages[0].Mode);
        Assert.True(stages[0].Inferred);

        // The gate is known without a record.
        Assert.Equal(DeliveryRunStageMode.Gate, stages[8].Mode);
        Assert.False(stages[8].Inferred);
    }

    [Fact]
    public async Task An_effort_runner_names_the_agent_behind_it_and_its_effort()
    {
        var implement = (await ResolveAsync("no-phase-map-runners.json"))[2];

        Assert.Equal(DeliveryRunStageMode.Delegate, implement.Mode);

        // No phase map named the agent, so the runner carried general-purpose.
        Assert.Equal("general-purpose", implement.Agent);
        Assert.Equal("high", implement.Effort);
        Assert.Equal("opus", implement.Model);
        Assert.Equal("delivery:runner-high", Assert.Single(implement.Workers).Agent);
    }

    [Fact]
    public async Task A_fallback_is_a_difference_and_says_what_ran_instead()
    {
        var stages = await ResolveAsync("inline-fallbacks.json");
        var implement = stages[2];

        Assert.Equal(DeliveryRunStageMode.Inline, implement.Mode);
        Assert.Null(implement.Agent);
        Assert.Equal("csharp-coding:coding", implement.Fallback);
        Assert.True(implement.Drifted);
        Assert.Equal("Not as configured: csharp-coding:coding did not resolve", DeliveryRunStageResolution.DriftTitle(implement));
        Assert.Contains("csharp-coding:coding did not resolve; ran in the owner session.", DeliveryRunStageResolution.Explanations(implement));

        // The owner's own slices are not sub-agents.
        Assert.Empty(implement.Workers);

        // A fallback the stage never recorded is still the resolved entry's.
        Assert.Equal("architecture:architect", stages[1].Fallback);
    }

    [Fact]
    public async Task Sub_agent_calls_the_execution_lists_stand_in_where_the_insights_saw_none()
    {
        var review = (await ResolveAsync("inline-fallbacks.json"))[3];

        Assert.Equal(DeliveryRunStageMode.Fork, review.Mode);

        var worker = Assert.Single(review.Workers);
        Assert.Equal("general-purpose", worker.Agent);
        Assert.Equal("whole diff", worker.Slice);
        Assert.Null(worker.DurationMs);
    }

    [Fact]
    public async Task A_run_file_with_quality_signals_puts_them_on_each_stage_and_its_one_worker()
    {
        var run = await ReadAsync("quality-signals.json");
        var stages = DeliveryRunStageResolution.Of(run);

        // On the stage: what each pass's closing call reported, and nothing elsewhere.
        Assert.Equal(2, run.Stages[2].Round);
        Assert.Equal((2, 0), (run.Stages[3].Round!.Value, run.Stages[3].Blockers!.Value));
        Assert.Equal(1, run.Stages[5].DefectsFound);
        Assert.All(new[] { 0, 1, 4, 6 }, index =>
        {
            Assert.Null(run.Stages[index].Round);
            Assert.Null(run.Stages[index].Blockers);
            Assert.Null(run.Stages[index].DefectsFound);
        });

        // Implement's one sub-agent call is the round the stage reported.
        Assert.Equal(2, Assert.Single(stages[2].Workers).Round);

        // Review listed each round's call with its own round.
        Assert.Equal(new int?[] { 1, 2 }, stages[3].Workers.Select(worker => worker.Round));
    }

    [Fact]
    public async Task A_run_file_from_before_the_quality_signals_reads_them_as_absent()
    {
        foreach (var fixture in new[] { "delegated-phase-map.json", "flat-phase-map.json", "inline-fallbacks.json", "no-phase-map-runners.json" })
        {
            var run = await ReadAsync(fixture);

            Assert.All(run.Stages, stage =>
            {
                Assert.Null(stage.Round);
                Assert.Null(stage.Blockers);
                Assert.Null(stage.DefectsFound);
            });
            Assert.All(DeliveryRunStageResolution.Of(run).SelectMany(stage => stage.Workers), worker => Assert.Null(worker.Round));
        }
    }

    [Fact]
    public void A_stage_round_is_no_one_worker_s_when_several_ran()
    {
        var run = Run(
            null,
            new DeliveryRunStage("Implement", "done", null, 2)
            {
                Execution = """{"mode":"delegate","agent":"csharp-coding:coding","round":2}""",
                Round = 2,
                SubAgentRuns =
                [
                    new DeliveryRunSubAgentRun("csharp-coding:coding", "claude-opus-5-5", 60_000, 10_000, 12, Failed: false),
                    new DeliveryRunSubAgentRun("csharp-coding:coding", "claude-opus-5-5", 50_000, 9_000, 10, Failed: false)
                ]
            });

        var implement = Assert.Single(DeliveryRunStageResolution.Of(run));

        Assert.Equal(2, implement.Workers.Count);
        Assert.All(implement.Workers, worker => Assert.Null(worker.Round));
    }

    [Fact]
    public void An_observed_call_takes_the_round_its_execution_listed_for_it()
    {
        var run = Run(
            null,
            new DeliveryRunStage("Review", "done", null, 2)
            {
                Execution = """{"mode":"fork","round":2,"blockers":1,"runs":[{"agent":"general-purpose","round":1},{"agent":"general-purpose","round":2},{"agent":"qa:qa-monitor"}]}""",
                Round = 2,
                Blockers = 1,
                SubAgentRuns =
                [
                    new DeliveryRunSubAgentRun("general-purpose", "claude-opus-5-5", 60_000, 10_000, 12, Failed: false),
                    new DeliveryRunSubAgentRun("qa:qa-monitor", "claude-haiku-4-5", 5_000, 1_000, 3, Failed: false),
                    new DeliveryRunSubAgentRun("general-purpose", "claude-opus-5-5", 50_000, 9_000, 10, Failed: false)
                ]
            });

        var review = Assert.Single(DeliveryRunStageResolution.Of(run));

        Assert.Equal(new int?[] { 1, null, 2 }, review.Workers.Select(worker => worker.Round));
    }

    [Fact]
    public async Task Every_fixture_resolves_one_execution_per_stage()
    {
        foreach (var fixture in Directory.GetFiles(FixtureFolder, "*.json"))
        {
            var run = await ReadAsync(Path.GetFileName(fixture));

            Assert.Equal(run.Stages.Count, DeliveryRunStageResolution.Of(run).Count);
        }
    }

    // No recorded run yet configured a model or an effort, so the comparison of those
    // two is proved over a run built here in the recorded shape.

    [Fact]
    public void Configured_and_ran_are_compared_on_agent_model_family_and_effort()
    {
        var run = Run(
            """{"phases":{"flow-code":{"phase-implement":{"mode":"delegate","agent":"csharp-coding:coding","model":"sonnet","effort":"high","origin":"repository overlay"}}}}""",
            Stage("Implement", "done", """{"mode":"delegate","agent":"general-purpose","model":"claude-opus-5-5","effort":"medium"}"""));

        var implement = Assert.Single(DeliveryRunStageResolution.Of(run));

        Assert.Equal(
            [DeliveryRunStageField.Agent, DeliveryRunStageField.Model, DeliveryRunStageField.Effort],
            implement.Differences.Select(difference => difference.Field));
        Assert.Equal("repository overlay", implement.Configured?.Origin);
        Assert.Equal(
            "Not as configured: agent csharp-coding:coding → general-purpose · model Sonnet → Opus 5.5 · effort high → medium",
            DeliveryRunStageResolution.DriftTitle(implement));
        Assert.Contains(
            "Configured vs ran: agent csharp-coding:coding → general-purpose · model Sonnet → Opus 5.5 · effort high → medium.",
            DeliveryRunStageResolution.Explanations(implement));
    }

    [Fact]
    public void An_alias_and_the_id_it_resolved_to_are_the_same_model()
    {
        var run = Run(
            """{"phases":{"flow-code":{"phase-review":{"mode":"fork","model":"opus"}}}}""",
            Stage("Review", "done", """{"mode":"fork","model":"claude-opus-5-5"}"""));

        Assert.Empty(Assert.Single(DeliveryRunStageResolution.Of(run)).Differences);
    }

    [Fact]
    public void An_effort_nothing_recorded_running_at_is_not_a_difference()
    {
        var run = Run(
            """{"phases":{"flow-code":{"phase-verify":{"mode":"delegate","agent":"qa:qa","effort":"high"}}}}""",
            Stage("Verify", "done", """{"mode":"delegate","agent":"qa:qa"}"""));

        var verify = Assert.Single(DeliveryRunStageResolution.Of(run));

        Assert.Empty(verify.Differences);

        // The configuration's effort is still what the stage is shown at.
        Assert.Equal("high", verify.Effort);
    }

    [Fact]
    public void A_pending_stage_and_the_gate_are_never_compared()
    {
        var run = Run(
            """{"phases":{"flow-code":{"phase-verify":{"mode":"delegate","agent":"qa:qa"},"phase-personal-validation":{"mode":"gate","agent":"qa:qa"}}}}""",
            Stage("Verify", "pending", """{"mode":"inline","agent":null}"""),
            Stage("Personal Validation", "in_progress", """{"mode":"inline","agent":null}"""));

        Assert.All(DeliveryRunStageResolution.Of(run), stage => Assert.Empty(stage.Differences));
    }

    [Fact]
    public void A_stage_recorded_inline_with_no_agent_ran_in_the_owner_session()
    {
        var run = Run(
            """{"phases":{"flow-code":{"phase-scope":{"mode":"delegate","agent":"architecture:architect"}}}}""",
            Stage("Scope", "done", """{"mode":"inline","agent":null}"""));

        var scope = Assert.Single(DeliveryRunStageResolution.Of(run));

        Assert.Null(scope.Agent);
        var difference = Assert.Single(scope.Differences);
        Assert.Equal("Not as configured: agent architecture:architect → session", DeliveryRunStageResolution.DriftTitle(scope));
        Assert.Null(difference.Ran);
    }

    [Fact]
    public void A_sub_agent_that_ran_under_the_gate_is_a_revise_round()
    {
        var run = Run(
            null,
            Stage("Personal Validation", "done", null) with
            {
                SubAgentRuns = [new DeliveryRunSubAgentRun("csharp-coding:coding", "claude-opus-5-5", 120_000, 40_000, 22, Failed: false)]
            });

        var gate = Assert.Single(DeliveryRunStageResolution.Of(run));
        var worker = Assert.Single(gate.Workers);

        Assert.Equal(DeliveryRunStageMode.Gate, gate.Mode);
        Assert.True(worker.Revise);
        Assert.Equal(40_000, worker.Tokens);
        Assert.Equal(22, worker.ToolCalls);
    }

    [Fact]
    public void A_qualified_stage_reads_the_entry_it_ran_under()
    {
        var run = Run(
            """{"phases":{"flow-code":{"phase-implement":{"mode":"delegate","agent":"csharp-coding:coding"},"phase-implement:frontend":{"mode":"delegate","agent":"ux-design:ux-designer"}}}}""",
            Stage("Implement", "done", """{"qualifier":"frontend","mode":"delegate","agent":"ux-design:ux-designer"}"""));

        var implement = Assert.Single(DeliveryRunStageResolution.Of(run));

        Assert.Equal("ux-design:ux-designer", implement.Configured?.Agent);
        Assert.Empty(implement.Differences);
    }

    [Fact]
    public void The_checkers_own_layer_records_name_the_overlays_that_are_there()
    {
        var run = Run(
            """{"layers":[{"scope":"user","path":"C:/devbook/config.local.json","present":true},{"scope":"repository","path":"C:/devbook/repos/x/config.local.json","present":false}],"phases":{"flow-code":{"phase-scope":{"mode":"inline"}}}}""",
            Stage("Scope", "done", null));

        Assert.Equal("team default, merged with user overlay", Assert.Single(DeliveryRunStageResolution.Of(run)).Configured?.Origin);
    }

    [Fact]
    public void Layers_that_name_no_overlay_are_the_team_default()
    {
        var run = Run(
            """{"layers":[],"phases":{"flow-code":{"phase-scope":{"mode":"inline"}}}}""",
            Stage("Scope", "done", null));

        Assert.Equal("team default", Assert.Single(DeliveryRunStageResolution.Of(run)).Configured?.Origin);
    }

    private static DeliveryRunStage Stage(string name, string status, string? execution) =>
        new(name, status, null, 0) { Execution = execution };

    private static DeliveryRun Run(string? context, params DeliveryRunStage[] stages) =>
        new(
            Id: "run-1",
            Dashboard: "backlog",
            Worktree: "fixture-1a2b3c4d",
            WorktreeName: "fixture",
            EnvironmentId: "machine",
            Environment: "Machine",
            SkillId: "flow-code",
            Title: "Fixture",
            Status: "in_progress",
            ChangeKind: "feature",
            References: [],
            StartedAt: null,
            UpdatedAt: DateTimeOffset.UnixEpoch,
            Stages: stages,
            TokenUsage: null,
            Context: null,
            InsightsByCategory: [],
            InsightsByServer: [])
        {
            RunContext = context
        };

    private static string FixtureFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "delivery-runs");

    private async Task<IReadOnlyList<DeliveryRunStageExecution>> ResolveAsync(string fixture) =>
        DeliveryRunStageResolution.Of(await ReadAsync(fixture));

    /// <summary>The fixture through the product's own reader, laid out as the Backlog
    /// surface lays its folder out.</summary>
    private async Task<DeliveryRun> ReadAsync(string fixture)
    {
        var folder = Path.Combine(_home, Path.GetFileNameWithoutExtension(fixture), "backlog", "fixture-1a2b3c4d", "runs");

        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(FixtureFolder, fixture), Path.Combine(folder, fixture), overwrite: true);

        var catalog = await new LocalDeliveryRunSource(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(folder)))!, "machine", "Machine").GetRunsAsync();

        return Assert.Single(catalog.Runs);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
